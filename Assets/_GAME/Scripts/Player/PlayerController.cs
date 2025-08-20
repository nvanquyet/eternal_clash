using _GAME.Scripts.Player.Config;
using _GAME.Scripts.Player.Enum;
using _GAME.Scripts.Player.Locomotion;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Player
{
    // NETWORKING FLOW:
    // 1. Owner: Gather input → Send to Server (if not server) → Process locally
    // 2. Server: Receive input → Process physics → Update NetworkVariables
    // 3. Non-owners: Receive NetworkVariable updates → Interpolate position → Update animation
    
    public class PlayerController : NetworkBehaviour
    {
        [SerializeField] private PlayerMovementConfig playerConfig;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Animator animator;
        [SerializeField] private GameObject fppCamera;
        [SerializeField] private GameObject tppCamera;

        private Transform CameraTransform
        {
            get
            {
                var activeCamera = tppCamera.activeSelf ? tppCamera : fppCamera;
                return activeCamera.transform;
            }
        }

        // Core systems
        private PlayerLocomotion _playerLocomotion;
        private PlayerLocomotionAnimator _animationController;
        
        // Network sync variables - CHỈ Server write, tất cả read
        private NetworkVariable<PlayerTransformData> _networkTransform = new NetworkVariable<PlayerTransformData>(
            PlayerTransformData.Empty, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
            
        private NetworkVariable<PlayerPhysicData> _networkPhysical = new NetworkVariable<PlayerPhysicData>(
            PlayerPhysicData.Empty, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Camera directions - Owner write để server biết camera direction cho input processing
        private NetworkVariable<Vector3> _networkCameraForward = new NetworkVariable<Vector3>(
            Vector3.forward, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        
        private NetworkVariable<Vector3> _networkCameraRight = new NetworkVariable<Vector3>(
            Vector3.right, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Server-side input buffer
        private PlayerInputData _lastInputData = PlayerInputData.Empty;
        
        // PlayerController fields
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;

        private bool IsLocalOwner => IsOwner && IsClient;       // client nhìn chính mình
        private bool IsRemoteClient => !IsOwner && IsClient;     // client nhìn người khác
        
        #region Unity Lifecycle

        private void Awake()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            if (!characterController) characterController = GetComponentInChildren<CharacterController>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            DeactivateAllCameras();
        }

        #endregion

        #region Network Lifecycle
        public override void OnNetworkSpawn()
        {
            InitializeSystems();

            if (IsLocalOwner)
            {
                SetupOwner();
            }
            else
            {
                SetupNonOwner();
            }

            // Đăng ký sự kiện CHỈ ở client non-owner
            if (IsRemoteClient)
            {
                _networkTransform.OnValueChanged += OnTransformValueChanged;
                _networkPhysical.OnValueChanged += OnPhysicValueChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsRemoteClient)
            {
                _networkTransform.OnValueChanged -= OnTransformValueChanged;
                _networkPhysical.OnValueChanged -= OnPhysicValueChanged;
            }
        }


        private void InitializeSystems()
        {
            _playerLocomotion = new PlayerLocomotion(playerConfig, characterController, animator, this);
            _animationController = new PlayerLocomotionAnimator(animator, _playerLocomotion, this);
        }

        private void SetupOwner()
        {
            ActivateTPPCamera();
            if (characterController) characterController.enabled = true; // owner move local (host sẽ là cùng instance server)
        }

        private void SetupNonOwner()
        {
            DeactivateAllCameras();

            // Cực kỳ quan trọng: không để CC can thiệp nội suy ở client non-owner
            if (IsRemoteClient && characterController) characterController.enabled = false;

            // Ở server (không owner), CC phải bật để server mô phỏng
            if (IsServer && !IsOwner && characterController) characterController.enabled = true;
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            if (IsLocalOwner)
            {
                // 1) Chủ máy đọc input
                UpdateCameraDirections();

                var inputData = GatherInput();

                // Luôn lưu vào _lastInputData để server FixedUpdate có dữ liệu,
                // kể cả khi là host (IsServer = true)
                _lastInputData = inputData;

                // Chủ máy có thể chạy logic state (dash cooldown, chuyển state, v.v.)
                _playerLocomotion?.OnUpdate(inputData);

                // Nếu KHÔNG phải server (client thuần), gửi RPC
                if (!IsServer)
                {
                    Debug.Log($"[PlayerController] IsServer {false}");
                    SendInputToServerRpc(inputData);
                }
            }

            // 2) Client non-owner: nội suy transform + cập nhật anim từ dữ liệu mạng
            if (IsRemoteClient)
            {
                HandleRemoteClientInterpolationAndAnim();
            }

            // 3) Server-only không làm gì ở Update cho transform (FixedUpdate xử lý)
        }

        private void HandleRemoteClientInterpolationAndAnim()
        {
            // Nếu snap quá xa (teleport)
            if ((transform.position - _targetPosition).sqrMagnitude > 25f)
            {
                transform.position = _targetPosition;
                transform.rotation = _targetRotation;
            }
            else
            {
                var _netLerp = 15f;
                float t = _netLerp * Time.deltaTime;
                transform.position = Vector3.Lerp(transform.position, _targetPosition, t);
                transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, t);
            }

            // Cập nhật anim mỗi frame từ NetworkVariable hiện tại (mượt hơn chờ OnValueChanged)
            var phys = _networkPhysical.Value;
            _animationController?.UpdateFromNetworkData(phys.velocity, phys.isGrounded);
        }
        
        private void HandleNonOwnerUpdate()
        {
            // Smooth interpolation đến network position
            var lerpSpeed = 15f * Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, _networkTransform.Value.position, lerpSpeed);
            transform.rotation = Quaternion.Lerp(transform.rotation, _networkTransform.Value.rotation, lerpSpeed);
        }

        private void FixedUpdate()
        {
            if (IsLocalOwner)
            {
                _playerLocomotion?.OnFixedUpdate(_lastInputData);
            }
            if (!IsServer) return;
            _playerLocomotion?.OnFixedUpdate(_lastInputData);
            UpdateNetworkSync();
        }
        
        private void LateUpdate()
        {
            _animationController?.OnLateUpdate();
        }
        #endregion

        #region Input Handling

        private PlayerInputData GatherInput()
        {
            return new PlayerInputData
            {
                moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")),
                jumpPressed = Input.GetKeyDown(KeyCode.Space),
                sprintHeld = Input.GetKey(KeyCode.LeftShift),
                dashPressed = Input.GetKeyDown(playerConfig.DashConfig.DashKeyCode),
            };
        }

        private void UpdateCameraDirections()
        {
            if (!IsOwner) return;
            
            var camTransform = CameraTransform;
            
            if (camTransform != null)
            {
                // Update network variables để server biết camera direction
                _networkCameraForward.Value = camTransform.forward;
                _networkCameraRight.Value = camTransform.right;
            }
        }

        [ServerRpc]
        private void SendInputToServerRpc(PlayerInputData inputData)
        {
            // Server nhận input từ client và store để xử lý trong FixedUpdate
            _lastInputData = inputData;
        }

        #endregion

        #region Network Synchronization

        private void UpdateNetworkSync()
        {
            if (!IsServer) return;

            _networkTransform.Value = new PlayerTransformData
            {
                position = transform.position,
                rotation = transform.rotation
            };

            _networkPhysical.Value = new PlayerPhysicData
            {
                velocity = _playerLocomotion?.Velocity ?? Vector3.zero,
                isGrounded = _playerLocomotion?.IsGrounded ?? true
            };
        }
        
        // Network callbacks
        private void OnTransformValueChanged(PlayerTransformData oldValue, PlayerTransformData newValue)
        {
            _targetPosition = newValue.position;
            _targetRotation = newValue.rotation;
        }

        private void OnPhysicValueChanged(PlayerPhysicData oldValue, PlayerPhysicData newValue)
        {
            // Tuỳ chọn: có thể để trống, vì mỗi frame đã đọc _networkPhysical.Value trong HandleRemoteClientInterpolationAndAnim()
        }

        #endregion

        #region Public Methods for PlayerLocomotion
        
        public Vector3 GetCameraForward()
        {
            if (IsOwner && CameraTransform != null)
            {
                return CameraTransform.forward;
            }
            
            // Non-owners hoặc server sử dụng network synced camera direction
            return _networkCameraForward.Value;
        }
        
        public Vector3 GetCameraRight()
        {
            if (IsOwner && CameraTransform != null)
            {
                return CameraTransform.right;
            }
            
            // Non-owners hoặc server sử dụng network synced camera direction
            return _networkCameraRight.Value;
        }

        #endregion

        #region Camera Management

        private void ActivateTPPCamera()
        {
            if (!IsOwner) return;
            SetCameraState(false, true);
        }

        private void ActivateFPPCamera()
        {
            if (!IsOwner) return;
            SetCameraState(true, false);
        }

        private void DeactivateAllCameras()
        {
            SetCameraState(false, false);
        }

        private void SetCameraState(bool fppActive, bool tppActive)
        {
            if (fppCamera) fppCamera.SetActive(fppActive);
            if (tppCamera) tppCamera.SetActive(tppActive);
        }

        #endregion
    }
}
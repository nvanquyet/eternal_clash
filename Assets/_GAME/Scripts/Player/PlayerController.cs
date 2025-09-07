using _GAME.Scripts.HideAndSeek;
using _GAME.Scripts.HideAndSeek.Player;
using _GAME.Scripts.Player.Config;
using _GAME.Scripts.Player.Locomotion;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace _GAME.Scripts.Player
{
    // SIMPLE NETCODE: Sử dụng NetworkTransform + NetworkAnimator
    // 1. Owner: Local movement + input processing 
    // 2. NetworkTransform: Auto sync position/rotation
    // 3. NetworkAnimator: Auto sync animation parameters
    // 4. Server: Authority validation (optional)

    [RequireComponent(typeof(NetworkTransform))]
    public class PlayerController : NetworkBehaviour
    {
        [SerializeField] private PlayerMovementConfig playerConfig;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Animator animator;
        [SerializeField] private GameObject fppCamera;
        [SerializeField] private GameObject tppCamera;

        [Header("Input")] [SerializeField] private MobileInputBridge playerInput;

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

        // Input handling
        private PlayerInputData _lastInputData = PlayerInputData.Empty;

        private bool IsLocalOwner => IsOwner && IsClient;

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
            _networkRole.OnValueChanged += OnNetworkRoleChanged;
            if (IsLocalOwner)
            {
                SetupOwner();
            }
            else
            {
                SetupNonOwner();
            }
            base.OnNetworkSpawn();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _networkRole.OnValueChanged -= OnNetworkRoleChanged;
        }

        private void InitializeSystems()
        {
            _playerLocomotion = new PlayerLocomotion(playerConfig, characterController, animator, this);
            _animationController = new PlayerLocomotionAnimator(animator, _playerLocomotion, this);
        }

        private void SetupOwner()
        {
            ActivateTppCamera();
            // Owner có CharacterController để local movement
            if (characterController) characterController.enabled = true;

            playerInput?.SetOwner();
        }

        private void SetupNonOwner()
        {
            DeactivateAllCameras();
            // Non-owners tắt CharacterController, để NetworkTransform sync
            if (characterController) characterController.enabled = false;

            playerInput?.SetNonOwner();
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            // CHỈ owner xử lý input và movement
            if (!IsLocalOwner) return;

            var inputData = GatherInput();
            _lastInputData = inputData;

            // Local movement cho instant response
            _playerLocomotion?.OnUpdate(inputData);
        }

        private void FixedUpdate()
        {
            // CHỈ owner xử lý physics
            if (!IsLocalOwner) return;
            _playerLocomotion?.OnFixedUpdate(_lastInputData);
        }

        private void LateUpdate()
        {
            // CHỈ owner update animation (NetworkAnimator sẽ sync)
            if (!IsLocalOwner) return;

            _animationController?.OnLateUpdate();
        }

        #endregion

        #region Input Handling

        private PlayerInputData GatherInput()
        {
            return playerInput.GetPlayerInput();
        }

        #endregion

        #region Public Methods for PlayerLocomotion

        public Vector3 GetCameraForward()
        {
            if (IsLocalOwner && CameraTransform != null)
            {
                return CameraTransform.forward;
            }

            // Non-owners: fallback to transform forward
            return transform.forward;
        }

        public Vector3 GetCameraRight()
        {
            if (IsLocalOwner && CameraTransform != null)
            {
                return CameraTransform.right;
            }

            // Non-owners: fallback to transform right
            return transform.right;
        }

        #endregion

        #region Camera Management

        private void ActivateTppCamera()
        {
            SetCameraState(false, true);
        }

        private void ActivateFppCamera()
        {
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

        #region Rpc Region

        [Rpc(SendTo.NotOwner)]
        public void SyncAnimationRpc(float xVel, float zVel, float yVel, bool isGrounded)
        {
            if (animator == null) return;
            animator.SetFloat("xVelocity", xVel);
            animator.SetFloat("zVelocity", zVel);
            animator.SetFloat("yVelocity", yVel);
            animator.SetBool("isGrounded", isGrounded);
        }

        [Rpc(SendTo.NotOwner)]
        public void SyncTriggerRpc(string triggerName)
        {
            if (animator != null)
            {
                animator.SetTrigger(triggerName);
            }
        }

        #endregion

        #region Role System - COMPLETELY FIXED

        // Role system
        private RolePlayer _currentRoleComponent;
        
        private NetworkVariable<PlayerRole> _networkRole = new NetworkVariable<PlayerRole>(
            PlayerRole.None, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );
        /// <summary>
        /// Public getter for current role
        /// </summary>
        public PlayerRole CurrentRole => _networkRole.Value;

        /// <summary>
        /// Public getter for role component
        /// </summary>
        public RolePlayer CurrentRoleComponent => _currentRoleComponent;

        /// <summary>
        /// Set player role - SERVER ONLY
        /// This is the MAIN method that should be called by GameManager
        /// </summary>
        public void SetRole(PlayerRole role)
        {
            if (!IsServer)
            {
                Debug.LogWarning($"SetRole can only be called on server! Attempted to set {role}");
                return;
            }

            if (_networkRole.Value == role)
            {
                Debug.LogWarning($"Player {OwnerClientId} already has role {role}");
                return;
            }

            Debug.Log($"[SERVER] Setting Player {OwnerClientId} role: {_networkRole.Value} -> {role}");

            // ✅ Set NetworkVariable - this will automatically sync to all clients
            _networkRole.Value = role;
        }

        /// <summary>
        /// Network callback - triggered on ALL clients when role changes
        /// </summary>
        private void OnNetworkRoleChanged(PlayerRole previousRole, PlayerRole newRole)
        {
            Debug.Log(
                $"[CLIENT {NetworkManager.Singleton.LocalClientId}] Player {OwnerClientId} role changed: {previousRole} -> {newRole}");
            ApplyRoleLocally(newRole);
        }

        /// <summary>
        /// Apply role changes locally on each client
        /// </summary>
        private void ApplyRoleLocally(PlayerRole newRole)
        {
            // Remove old role component
            if (_currentRoleComponent != null)
            {
                Debug.Log($"[CLIENT] Removing old role component: {_currentRoleComponent.GetType().Name}");

                // Notify old component about role change
                _currentRoleComponent.OnRoleRemoved();

                if (_currentRoleComponent is Component comp)
                {
                    Destroy(comp);
                }

                _currentRoleComponent = null;
            }

            // Add new role component
            if (newRole != PlayerRole.None)
            {
                _currentRoleComponent = CreateRoleComponent(newRole);
                if (_currentRoleComponent != null)
                {
                    Debug.Log($"[CLIENT] Created new role component: {_currentRoleComponent.GetType().Name}");

                    // ✅ IMPORTANT: Set role in the component
                    _currentRoleComponent.SetRole(newRole);

                    // Notify about role assignment
                    _currentRoleComponent.OnRoleAssigned();
                }
            }
        }

        /// <summary>
        /// Create appropriate role component based on role type
        /// </summary>
        private RolePlayer CreateRoleComponent(PlayerRole role)
        {
            return role switch
            {
                PlayerRole.Hider => gameObject.AddComponent<HiderPlayer>(),
                PlayerRole.Seeker => gameObject.AddComponent<SeekerPlayer>(),
                PlayerRole.None => null,
                _ => throw new System.ArgumentException($"Unknown role: {role}")
            };
        }

        #endregion
    }
}
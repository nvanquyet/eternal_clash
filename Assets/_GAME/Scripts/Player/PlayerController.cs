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

        [Header("Input")]
        [SerializeField] private MobileInputBridge playerInput;
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

            if (IsLocalOwner)
            {
                SetupOwner();
            }
            else
            {
                SetupNonOwner();
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

        private void ActivateTPPCamera()
        {
            SetCameraState(false, true);
        }

        private void ActivateFPPCamera()
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
    }
}
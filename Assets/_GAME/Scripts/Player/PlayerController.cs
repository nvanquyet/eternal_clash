using _GAME.Scripts.Core.Components;
using _GAME.Scripts.HideAndSeek;
using _GAME.Scripts.HideAndSeek.Config;
using _GAME.Scripts.HideAndSeek.Player.Graphics;
using _GAME.Scripts.Player.Config;
using _GAME.Scripts.Player.Locomotion;
using _GAME.Scripts.UI.Base;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Serialization;

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

        [SerializeField] private PlayerCamera playerCamera;

        [FormerlySerializedAs("roleComponentComponent")] [SerializeField] private RoleComponent roleComponent;

        [Header("Model Switching")] [SerializeField]
        private PlayerModelSwitcher modelSwitcher;

        [SerializeField] private PlayerAnimationSync animationSync;

        public PlayerAnimationSync AnimationSync => animationSync;
        public PlayerCamera PlayerCamera => playerCamera;
        public RoleComponent RoleComponent => roleComponent;

        // Core systems
        private MobileInputBridge _playerInput;
        private PlayerLocomotion _playerLocomotion;
        private PlayerLocomotionAnimator _animationController;
        
        private bool _isNetworkInitialized = false;

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
            modelSwitcher = GetComponent<PlayerModelSwitcher>();
            if (modelSwitcher != null)
            {
                animationSync = modelSwitcher.GetAnimationSync();
                modelSwitcher.OnAnimatorChanged += OnAnimatorChanged;
            }

            PlayerCamera.ActiveCamera(false);
        }

        private void OnAnimatorChanged(Animator newAnimator)
        {
            animationSync.SetAnimator(newAnimator);

            // Reinitialize systems với animator mới
            if (_animationController != null)
            {
                _animationController.UpdateAnimator(newAnimator);
            }
        }

        private void Start()
        {
            GameEvent.OnGameEnded += OnGameEnd;
        }


        public override void OnDestroy()
        {
            GameEvent.OnGameEnded -= OnGameEnd;
            base.OnDestroy();
        }

        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            if(_isNetworkInitialized) return;
            _isNetworkInitialized = true;
            InitializeSystems();
            Debug.Log($"🔵 [OnNetworkSpawn] Registering callback for Player {OwnerClientId}");
            if (IsLocalOwner)
            {
                SetupOwner();
            }
            else
            {
                SetupNonOwner();
            }

            GameEvent.OnPlayerDeath += OnPlayerDeath;

            base.OnNetworkSpawn();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            Debug.Log($"🔴 [OnNetworkDespawn] Player {OwnerClientId} - Unregistering callbacks");
            if (modelSwitcher != null)
            {
                modelSwitcher.OnAnimatorChanged -= OnAnimatorChanged;
            }

            GameEvent.OnPlayerDeath -= OnPlayerDeath;
            _isNetworkInitialized = false;
        }

        private void InitializeSystems()
        {
            // Initialize systems
            _playerLocomotion = new PlayerLocomotion(playerConfig, characterController, this);
            _animationController =
                new PlayerLocomotionAnimator(animationSync.CurrentAnimator, _playerLocomotion, this);
        }

        private void SetupOwner()
        {
            PlayerCamera.ActiveCamera();
            // Owner có CharacterController để local movement
            if (characterController) characterController.enabled = true;
            if (_playerInput == null) _playerInput = HUD.Instance.GetUI<MobileInputBridge>(UIType.Input);
            _playerInput?.SetOwner();
        }

        private void SetupNonOwner()
        {
            PlayerCamera.ActiveCamera(false);
            // Non-owners tắt CharacterController, để NetworkTransform sync
            if (characterController) characterController.enabled = false;
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
            if (_playerInput == null)
                return new PlayerInputData()
                {
                    moveInput = Vector2.zero,
                    jumpPressed = false,
                    sprintHeld = false,
                    dashPressed = false
                };
            return _playerInput.GetPlayerInput();
        }

        #endregion
        

        public void EnableSoulMode(bool enable)
        {
            if (!IsOwner) return;
            if (enable)
            {
                playerCamera.ActiveCamera(false);
                _playerInput?.Hide(null);
            }
            else
            {
                playerCamera.ActiveCamera();
                _playerInput?.Show(null);
            }
        }

        //Register Event
        private void OnPlayerDeath(string namePlayer, ulong pId)
        {
            if (pId != OwnerClientId) return;
            _playerLocomotion.SetFreezeMovement(true);
        }

        private void OnGameEnd(Role obj)
        {
            if (IsOwner)
            {
                //Disable input 
                _playerInput?.Hide(null);
            }
        }
    }
}
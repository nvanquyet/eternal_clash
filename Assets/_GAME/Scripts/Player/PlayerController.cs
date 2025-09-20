using System;
using _GAME.Scripts.HideAndSeek;
using _GAME.Scripts.HideAndSeek.Config;
using _GAME.Scripts.HideAndSeek.Player;
using _GAME.Scripts.HideAndSeek.Player.Graphics;
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
        
        [SerializeField] private GameObject fppCamera;
        [SerializeField] private GameObject tppCamera;

        [SerializeField] private PlayerRoleSO playerRoleSO;
        [Header("Input")] [SerializeField] private MobileInputBridge playerInput;
        
        [Header("Model Switching")]
        [SerializeField] private PlayerModelSwitcher modelSwitcher;
        [SerializeField] private PlayerAnimationSync animationSync;

        public PlayerAnimationSync AnimationSync => animationSync;

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
            modelSwitcher = GetComponent<PlayerModelSwitcher>();
            if (modelSwitcher != null)
            {
                animationSync = modelSwitcher.GetAnimationSync();
                modelSwitcher.OnAnimatorChanged += OnAnimatorChanged;
            }
            DeactivateAllCameras();
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
            if (modelSwitcher != null)
            {
                modelSwitcher.OnAnimatorChanged -= OnAnimatorChanged;
            }
        }

        private void InitializeSystems()
        {
            // Initialize systems
            _playerLocomotion = new PlayerLocomotion(playerConfig, characterController, this);
            _animationController = new PlayerLocomotionAnimator(animationSync.GetCurrentAnimator(), _playerLocomotion, this);
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

        #region Role System - COMPLETELY FIXED
        
        private NetworkVariable<Role> _networkRole = new NetworkVariable<Role>(
            Role.None, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );
        /// <summary>
        /// Public getter for current role
        /// </summary>
        public Role CurrentRole => _networkRole.Value;


        /// <summary>
        /// Set player role - SERVER ONLY
        /// This is the MAIN method that should be called by GameManager
        /// </summary>
        public void SetRole(Role role)
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
        private void OnNetworkRoleChanged(Role previousRole, Role newRole)
        {
            // CHỈ server spawn network object
            if (IsServer)
            {
                SpawnRoleObjectOnServer(newRole);
            }
    
            // Tất cả clients apply local changes (UI, camera, etc.)
            ApplyRoleUIChanges(newRole);
        }

        /// <summary>
        /// Apply role changes locally on each client
        /// </summary>
        private void ApplyRoleUIChanges(Role newRole)
        {
            // CHỈ apply UI/camera/animation changes
            // KHÔNG spawn objects nữa
            // Ví dụ: change camera mode, UI elements, etc.
        }
        
        private void SpawnRoleObjectOnServer(Role newRole)
        {
            var prefab = playerRoleSO?.GetData(newRole).Prefab;
            if(prefab == null) return;
    
            var gO = Instantiate(prefab);
            
            gO.OnNetworkSpawned += () =>
            {
                Debug.Log($"[SERVER] Spawned role object for Player {OwnerClientId} as {newRole}");
                gO.transform.SetParent(transform);
                gO.transform.localPosition = Vector3.zero;
                gO.SetRole(newRole);
            };
            
            var networkObject = gO.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                networkObject.SpawnWithOwnership(OwnerClientId);
               
            }
        }
        
        #endregion
    }
}
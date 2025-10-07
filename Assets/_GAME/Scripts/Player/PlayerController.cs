using System;
using _GAME.Scripts.HideAndSeek;
using _GAME.Scripts.HideAndSeek.Config;
using _GAME.Scripts.HideAndSeek.Player;
using _GAME.Scripts.HideAndSeek.Player.Graphics;
using _GAME.Scripts.Player.Config;
using _GAME.Scripts.Player.Locomotion;
using _GAME.Scripts.UI.Base;
using Mono.CSharp;
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
        [SerializeField] private MobileInputBridge playerInput;
        [SerializeField] private PlayerCamera playerCamera;
        [SerializeField] private PlayerRoleSO playerRoleSO;

        [Header("Model Switching")] 
        [SerializeField] private PlayerModelSwitcher modelSwitcher;
        [SerializeField] private PlayerAnimationSync animationSync;

        [Header("Aiming Settings")]
        [SerializeField] private float aimRotationSpeed = 10f;
        [SerializeField] private float aimRotationThreshold = 0.1f;

        public PlayerAnimationSync AnimationSync => animationSync;
        public PlayerCamera PlayerCamera => playerCamera;

        // Core systems
        private PlayerLocomotion _playerLocomotion;
        private PlayerLocomotionAnimator _animationController;

        // Aiming state
        private bool _isAiming = false;
        private Quaternion _targetAimRotation;

        public MobileInputBridge PlayerInput
        {
            get
            {
                if (!playerInput) playerInput = GetComponentInChildren<MobileInputBridge>();
                return playerInput;
            }
        }
        
        private bool _isNetworkInitialized = false;
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

            PlayerCamera.DisableCams();
        }

        private void OnAnimatorChanged(Animator newAnimator)
        {
            animationSync.SetAnimator(newAnimator);
            if (_animationController != null)
            {
                _animationController.UpdateAnimator(newAnimator);
            }
        }

        private void Start()
        {
            GameEvent.OnRoleAssigned += RoleAssigned;
            GameEvent.OnGameEnded += OnGameEnd;
        }

        public override void OnDestroy()
        {
            GameEvent.OnRoleAssigned -= RoleAssigned;
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
            _networkRole.OnValueChanged += OnNetworkRoleChanged;
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
            _networkRole.OnValueChanged -= OnNetworkRoleChanged;
            if (modelSwitcher != null)
            {
                modelSwitcher.OnAnimatorChanged -= OnAnimatorChanged;
            }

            GameEvent.OnPlayerDeath -= OnPlayerDeath;
            _isNetworkInitialized = false;
        }

        private void InitializeSystems()
        {
            _playerLocomotion = new PlayerLocomotion(playerConfig, characterController, this);
            _animationController = new PlayerLocomotionAnimator(animationSync.CurrentAnimator, _playerLocomotion, this);
        }

        private void SetupOwner()
        {
            PlayerCamera.EnableCam();
            if (characterController) characterController.enabled = true;
            PlayerInput.SetOwner();
        }

        private void SetupNonOwner()
        {
            PlayerCamera.DisableCams();
            if (characterController) characterController.enabled = false;
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            if (!IsLocalOwner) return;

            var inputData = GatherInput();
            _lastInputData = inputData;

            // ⭐ Xử lý rotation khi aiming TRƯỚC movement
            if (_isAiming)
            {
                HandleAimingRotation();
            }

            _playerLocomotion?.OnUpdate(inputData);
        }

        private void FixedUpdate()
        {
            if (!IsLocalOwner) return;
            _playerLocomotion?.OnFixedUpdate(_lastInputData);
        }

        private void LateUpdate()
        {
            if (!IsLocalOwner) return;
            _animationController?.OnLateUpdate();
        }

        #endregion

        #region Input Handling

        private PlayerInputData GatherInput()
        {
            if (PlayerInput == null)
                return new PlayerInputData()
                {
                    moveInput = Vector2.zero,
                    jumpPressed = false,
                    sprintHeld = false,
                    dashPressed = false
                };
            return PlayerInput.GetPlayerInput();
        }

        #endregion

        #region Aiming System

        /// <summary>
        /// Bật chế độ ngắm
        /// - Rotate character theo hướng camera
        /// - Ngăn auto-rotation khi di chuyển
        /// - Zoom camera in
        /// </summary>
        public void StartAiming()
        {
            if (!IsLocalOwner) return;
            if (_isAiming) return; // Already aiming
            
            _isAiming = true;
            
            // Lấy hướng camera (bỏ trục Y để chỉ xoay trên mặt phẳng)
            Vector3 cameraForward = PlayerCamera.GetCameraForward();
            cameraForward.y = 0;
            
            if (cameraForward.sqrMagnitude > 0.01f)
            {
                cameraForward.Normalize();
                _targetAimRotation = Quaternion.LookRotation(cameraForward);
            }
            else
            {
                _targetAimRotation = transform.rotation;
            }
            
            // ⭐ Thông báo cho PlayerLocomotion ngăn auto-rotation
            _playerLocomotion?.SetAimingMode(true);
            
            // ⭐ Zoom camera
            PlayerCamera.SetAimingMode(true);
            
            Debug.Log($"🎯 [StartAiming] Player {OwnerClientId} entered aiming mode");
        }

        /// <summary>
        /// Tắt chế độ ngắm
        /// </summary>
        public void StopAiming()
        {
            if (!IsLocalOwner) return;
            if (!_isAiming) return; // Not aiming
            
            _isAiming = false;
            
            // ⭐ Cho phép PlayerLocomotion xoay tự do lại
            _playerLocomotion?.SetAimingMode(false);
            
            // ⭐ Zoom out camera
            PlayerCamera.SetAimingMode(false);
            
            Debug.Log($"🎯 [StopAiming] Player {OwnerClientId} exited aiming mode");
        }

        /// <summary>
        /// Toggle aiming mode
        /// </summary>
        public void ToggleAiming()
        {
            if (_isAiming)
                StopAiming();
            else
                StartAiming();
        }

        /// <summary>
        /// Kiểm tra đang aiming không
        /// </summary>
        public bool IsAiming => _isAiming;

        /// <summary>
        /// Xử lý rotation khi aiming - character luôn quay theo camera
        /// </summary>
        private void HandleAimingRotation()
        {
            // Cập nhật target rotation liên tục theo camera
            Vector3 cameraForward = PlayerCamera.GetCameraForward();
            cameraForward.y = 0;
            
            if (cameraForward.sqrMagnitude < 0.01f) return;
            
            cameraForward.Normalize();
            Quaternion newTargetRotation = Quaternion.LookRotation(cameraForward);
            
            // Chỉ update nếu góc thay đổi đủ lớn (tránh jitter)
            float angleDifference = Quaternion.Angle(_targetAimRotation, newTargetRotation);
            if (angleDifference > aimRotationThreshold)
            {
                _targetAimRotation = newTargetRotation;
            }
            
            // Smooth rotation đến target
            transform.rotation = Quaternion.Slerp(
                transform.rotation, 
                _targetAimRotation, 
                Time.deltaTime * aimRotationSpeed
            );
        }

        #endregion

        #region Role System

        private readonly NetworkVariable<Role> _networkRole = new NetworkVariable<Role>(
            Role.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public Role CurrentRole => _networkRole.Value;

        private void RoleAssigned()
        {
            if (!IsServer) return;
            Debug.Log($"🟠 [RoleAssigned] Server assigning role for Player {OwnerClientId}");
            var role = GameManager.Instance.GetPlayerRoleWithId(this.OwnerClientId);
            SetRole(role);
        }

        public void SetRole(Role role)
        {
            Debug.Log($"🟡 [SetRole] Called for Player {OwnerClientId} with role {role} - IsServer: {IsServer}");

            if (!IsServer)
            {
                Debug.LogWarning($"❌ [SetRole] Can only be called on server! Player {OwnerClientId} attempted to set {role}");
                return;
            }

            if (_networkRole.Value == role)
            {
                Debug.LogWarning($"⚠️ [SetRole] Player {OwnerClientId} already has role {role}");
                return;
            }

            Debug.Log($"✅ [SetRole] SERVER - Setting Player {OwnerClientId} role: {_networkRole.Value} -> {role}");
            _networkRole.Value = role;
            Debug.Log($"✅ [SetRole] SERVER - NetworkVariable value after set: {_networkRole.Value}");
        }

        private void OnNetworkRoleChanged(Role previousRole, Role newRole)
        {
            Debug.Log($"🟢 [OnNetworkRoleChanged] CALLBACK TRIGGERED! Client {NetworkManager.Singleton.LocalClientId} - Player {OwnerClientId}: {previousRole} -> {newRole}");
            Debug.Log($"🟢 [OnNetworkRoleChanged] IsServer: {IsServer}, IsClient: {IsClient}");

            if (IsServer)
            {
                Debug.Log($"🟢 [OnNetworkRoleChanged] Server calling SpawnRoleObject for role {newRole}");
                SpawnRoleObject(newRole);
            }
            else
            {
                Debug.Log($"🟢 [OnNetworkRoleChanged] Client - handling role change UI/effects");
                OnRoleChangedClientSide(previousRole, newRole);
            }
        }

        private void OnRoleChangedClientSide(Role previousRole, Role newRole)
        {
            Debug.Log($"🔷 [OnRoleChangedClientSide] Player {OwnerClientId} role changed to {newRole}");
        }

        private void SpawnRoleObject(Role newRole)
        {
            Debug.Log($"🟣 [SpawnRoleObject] START - Player {OwnerClientId}, Role {newRole}, IsServer: {IsServer}");

            if (!IsServer)
            {
                Debug.LogError($"❌ [SpawnRoleObject] Called on client! This should only run on server!");
                return;
            }

            if (playerRoleSO == null)
            {
                Debug.LogError($"❌ [SpawnRoleObject] PlayerRoleSO is null for Player {OwnerClientId}!");
                return;
            }

            var roleData = playerRoleSO.GetData(newRole);
            var prefab = roleData.Prefab;
            
            if (prefab == null)
            {
                Debug.LogError($"❌ [SpawnRoleObject] No prefab found for role {newRole}!");
                return;
            }

            Debug.Log($"🟣 [SpawnRoleObject] Found prefab: {prefab.name} for role {newRole}");

            if (prefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError($"❌ [SpawnRoleObject] Prefab {prefab.name} doesn't have NetworkObject component!");
                return;
            }

            Debug.Log($"🟣 [SpawnRoleObject] Instantiating prefab {prefab.name}");
            var gO = Instantiate(prefab);

            if (gO == null)
            {
                Debug.LogError($"❌ [SpawnRoleObject] Failed to instantiate prefab!");
                return;
            }

            Debug.Log($"🟣 [SpawnRoleObject] Successfully instantiated {gO.name}");
            gO.OnNetworkSpawned += () =>
            {
                Debug.Log($"[PlayerController] Spawned role object for Player {OwnerClientId} as {newRole}");
                gO.NetworkObject.TrySetParent(transform, false);
                gO.transform.localPosition = Vector3.zero;
                gO.SetRole(newRole);
            };

            Debug.Log($"🟣 [SpawnRoleObject] About to spawn with ownership - Owner: {OwnerClientId}");

            try
            {
                gO.NetworkObject.SpawnWithOwnership(OwnerClientId);
                Debug.Log($"✅ [SpawnRoleObject] Successfully spawned {gO.name} for Player {OwnerClientId} as {newRole}");
                gO.SetRole(newRole);
                Debug.Log($"✅ [SpawnRoleObject] Set role on spawned object");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ [SpawnRoleObject] Exception during spawn: {e.Message}");
                Debug.LogError($"❌ [SpawnRoleObject] Stack trace: {e.StackTrace}");
                if (gO != null)
                {
                    Destroy(gO);
                }
            }
        }

        #endregion

        #region Event Handlers

        public void EnableSoulMode(bool enable)
        {
            if (!IsOwner) return;
            if (enable)
            {
                playerCamera.DisableCams();
                PlayerInput?.Hide(null);
                
                // Tắt aiming khi vào soul mode
                if (_isAiming)
                {
                    StopAiming();
                }
            }
            else
            {
                playerCamera.EnableCam();
                PlayerInput?.Show(null);
            }
        }

        private void OnPlayerDeath(string namePlayer, ulong pId)
        {
            if (pId != OwnerClientId) return;
            _playerLocomotion.SetFreezeMovement(true);
            
            // Tắt aiming khi chết
            if (_isAiming)
            {
                StopAiming();
            }
        }

        private void OnGameEnd(Role obj)
        {
            if (IsOwner)
            {
                PlayerInput?.Hide(null);
                
                // Tắt aiming khi game kết thúc
                if (_isAiming)
                {
                    StopAiming();
                }
            }
        }

        #endregion
    }
}
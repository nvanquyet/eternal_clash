using System;
using _GAME.Scripts.HideAndSeek;
using _GAME.Scripts.HideAndSeek.Config;
using _GAME.Scripts.HideAndSeek.Player;
using _GAME.Scripts.HideAndSeek.Player.Graphics;
using _GAME.Scripts.Player.Config;
using _GAME.Scripts.Player.Locomotion;
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

        [SerializeField] private PlayerCamera playerCamera;

        [SerializeField] private PlayerRoleSO playerRoleSO;
        [Header("Input")] [SerializeField] private MobileInputBridge playerInput;

        [Header("Model Switching")] [SerializeField]
        private PlayerModelSwitcher modelSwitcher;

        [SerializeField] private PlayerAnimationSync animationSync;

        public PlayerAnimationSync AnimationSync => animationSync;
        public PlayerCamera PlayerCamera => playerCamera;

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
            PlayerCamera.DisableAllCams();
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
            // ✅ Fixed: Removed * syntax error
            _networkRole.OnValueChanged -= OnNetworkRoleChanged;
            if (modelSwitcher != null)
            {
                modelSwitcher.OnAnimatorChanged -= OnAnimatorChanged;
            }
            GameEvent.OnPlayerDeath -= OnPlayerDeath;

        }

        private void InitializeSystems()
        {
            // Initialize systems
            _playerLocomotion = new PlayerLocomotion(playerConfig, characterController, this);
            _animationController =
                new PlayerLocomotionAnimator(animationSync.GetCurrentAnimator(), _playerLocomotion, this);
        }

        private void SetupOwner()
        {
            PlayerCamera.EnableTppCam();
            // Owner có CharacterController để local movement
            if (characterController) characterController.enabled = true;

            playerInput?.SetOwner();
        }

        private void SetupNonOwner()
        {
            PlayerCamera.DisableAllCams();
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

        #region Role System - COMPLETELY FIXED

        private readonly NetworkVariable<Role> _networkRole = new NetworkVariable<Role>(
            Role.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        /// <summary>
        /// Public getter for current role
        /// </summary>
        public Role CurrentRole => _networkRole.Value;

        private void RoleAssigned()
        {
            if(!IsServer) return;
            Debug.Log($"🟠 [RoleAssigned] Server assigning role for Player {OwnerClientId}");
            var role = GameManager.Instance.GetPlayerRoleWithId(this.OwnerClientId);
            SetRole(role);
        }


        /// <summary>
        /// Set player role - SERVER ONLY
        /// This is the MAIN method that should be called by GameManager
        /// </summary>
        /// <summary>
        /// Set player role - SERVER ONLY
        /// </summary>
        public void SetRole(Role role)
        {
            Debug.Log($"🟡 [SetRole] Called for Player {OwnerClientId} with role {role} - IsServer: {IsServer}");

            if (!IsServer)
            {
                Debug.LogWarning(
                    $"❌ [SetRole] Can only be called on server! Player {OwnerClientId} attempted to set {role}");
                return;
            }

            if (_networkRole.Value == role)
            {
                Debug.LogWarning($"⚠️ [SetRole] Player {OwnerClientId} already has role {role}");
                return;
            }

            Debug.Log($"✅ [SetRole] SERVER - Setting Player {OwnerClientId} role: {_networkRole.Value} -> {role}");

            // This should trigger OnNetworkRoleChanged callback
            _networkRole.Value = role;

            // Verify the value was set
            Debug.Log($"✅ [SetRole] SERVER - NetworkVariable value after set: {_networkRole.Value}");
        }

        /// <summary>
        /// Network callback - triggered on ALL clients when role changes
        /// </summary>
        private void OnNetworkRoleChanged(Role previousRole, Role newRole)
        {
            Debug.Log(
                $"🟢 [OnNetworkRoleChanged] CALLBACK TRIGGERED! Client {NetworkManager.Singleton.LocalClientId} - Player {OwnerClientId}: {previousRole} -> {newRole}");
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

        /// <summary>
        /// Client-side handling of role changes
        /// </summary>
        private void OnRoleChangedClientSide(Role previousRole, Role newRole)
        {
            Debug.Log($"🔷 [OnRoleChangedClientSide] Player {OwnerClientId} role changed to {newRole}");
            // Add client-side logic here (UI updates, effects, etc.)
        }

        // private void SpawnRoleObject(Role newRole)
        // {
        //     if (!IsServer) return; // Chỉ server mới spawn object
        //     var prefab = playerRoleSO?.GetData(newRole).Prefab;
        //     if (prefab == null) return;
        //
        //     var gO = Instantiate(prefab);
        //
        //     gO.OnNetworkSpawned += () =>
        //     {
        //         Debug.Log($"[PlayerController] Spawned role object for Player {OwnerClientId} as {newRole}");
        //         gO.transform.SetParent(transform);
        //         gO.transform.localPosition = Vector3.zero;
        //         gO.SetRole(newRole);
        //     };
        //
        //     gO.NetworkObject.SpawnWithOwnership(OwnerClientId);
        // }

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

            // Check if prefab has NetworkObject
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
                // Spawn with ownership
                gO.NetworkObject.SpawnWithOwnership(OwnerClientId);
                Debug.Log(
                    $"✅ [SpawnRoleObject] Successfully spawned {gO.name} for Player {OwnerClientId} as {newRole}");
                
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

        public void EnableSoulMode(bool enable)
        {
            if (!IsOwner) return;
            playerInput?.gameObject.SetActive(enable);
            if (enable)
            {
                playerCamera.DisableAllCams();
            }
            else
            {
                playerCamera.EnableTppCam();
            }
        }
        
        
        //Register Event
        private void OnPlayerDeath(ulong pId)
        {
            if(pId != OwnerClientId) return;
            _playerLocomotion.SetFreezeMovement(true);
        }
        
        private void OnGameEnd(Role obj)
        {
            if (IsOwner)
            {
                //Disable input 
                playerInput?.gameObject.SetActive(false);
            }
        }
        
    }
}
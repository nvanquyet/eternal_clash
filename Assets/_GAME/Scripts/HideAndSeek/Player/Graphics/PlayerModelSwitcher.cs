// ==================== OPTIMIZED PlayerModelSwitcher ====================
using System.Collections.Generic;
using _GAME.Scripts.Player;
using _GAME.Scripts.Utils;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.HideAndSeek.Player.Graphics
{
    public class PlayerModelSwitcher : NetworkBehaviour
    {
        [Header("Configuration")] 
        [SerializeField] private ModelConfigSO modelConfig;
        [SerializeField] private PlayerAnimationSync animationSync;
        [SerializeField] private InputActionReference switchModelAction;
        [SerializeField] private GameMode currentGameMode = GameMode.PersonVsPerson;

        [Header("Performance")] 
        [SerializeField] private float switchCooldown = 0.5f;
        [SerializeField] private float reEquipDelay = 0.2f;

        [Header("Model Container")] 
        [SerializeField] private Transform modelContainer;

        [Header("Debug")] 
        [SerializeField] private bool enableDebugLogging;

        // Network state
        private NetworkVariable<int> currentModelIndex = new(0, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server);

        // Cached components (lazy-loaded)
        private PlayerController _playerController;
        private PlayerEquipment _playerEquipment;
        private InputAction _switchModelAction;

        // Current state
        private GameObject currentModel;
        private List<ModelConfigData> availableModels = new();
        private float lastSwitchTime;
        private bool _isInitialized;

        // Public state
        public Animator CurrentAnimator { get; private set; }
        public ModelConfigData CurrentModelData { get; private set; }
        public bool CanSwitch => (Time.time - lastSwitchTime) > switchCooldown;

        // Events
        public System.Action<Animator> OnAnimatorChanged;
        public System.Action<GameObject, ModelConfigData> OnModelChanged;

        // Properties with lazy initialization
        private PlayerController PlayerController => 
            _playerController ? _playerController : (_playerController = GetComponent<PlayerController>());

        private PlayerEquipment PlayerEquipment => 
            _playerEquipment ? _playerEquipment : (_playerEquipment = GetComponentInChildren<PlayerEquipment>());

        #region Initialization

        private void Awake()
        {
            if (!modelConfig)
            {
                Debug.LogError($"[ModelSwitcher] ModelConfigSO missing on {gameObject.name}!");
                enabled = false;
                return;
            }

            EnsureModelContainer();
        }

        private void EnsureModelContainer()
        {
            if (!modelContainer)
            {
                var containerObj = new GameObject("ModelContainer");
                containerObj.transform.SetParent(transform);
                containerObj.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                modelContainer = containerObj.transform;
            }
        }

        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            Log($"OnNetworkSpawn - Owner:{IsOwner}, Server:{IsServer}, Client:{OwnerClientId}");

            // Initialize available models ONCE
            if (!_isInitialized)
            {
                RefreshAvailableModels();
                _isInitialized = true;
            }

            // Listen to model changes
            currentModelIndex.OnValueChanged += OnModelIndexChanged;

            if (IsOwner)
            {
                SetupInputAction();
                
                // Request initial model if needed
                if (availableModels.Count > 0)
                {
                    SwitchToModelServerRpc(0);
                }
            }

            // Apply current network state (for late joiners)
            if (currentModelIndex.Value >= 0 && currentModelIndex.Value < availableModels.Count)
            {
                UpdateModel(currentModelIndex.Value);
            }
        }

        private void SetupInputAction()
        {
            if (!switchModelAction) return;

            _switchModelAction = InputActionFactory.CreateUniqueAction(switchModelAction, GetInstanceID());
            _switchModelAction.Enable();
            _switchModelAction.performed += OnSwitchModelInput;
        }

        public override void OnNetworkDespawn()
        {
            Log($"OnNetworkDespawn - Client:{OwnerClientId}");

            currentModelIndex.OnValueChanged -= OnModelIndexChanged;

            if (_switchModelAction != null)
            {
                _switchModelAction.performed -= OnSwitchModelInput;
                _switchModelAction.Disable();
            }
        }

        #endregion

        #region Model Management

        private void RefreshAvailableModels()
        {
            if (!modelConfig || !PlayerController) return;

            Role currentRole = PlayerController.RoleComponent.CurrentRole;
            availableModels = modelConfig.GetModelsForGameMode(currentGameMode, currentRole);

            Log($"Available models for {currentGameMode}-{currentRole}: {availableModels.Count}");
        }

        private void OnSwitchModelInput(InputAction.CallbackContext ctx)
        {
            if (!IsOwner || !CanSwitch || availableModels.Count <= 1) return;

            int nextIndex = (currentModelIndex.Value + 1) % availableModels.Count;
            Log($"Requesting switch: {currentModelIndex.Value} -> {nextIndex}");

            SwitchToModelServerRpc(nextIndex);
            lastSwitchTime = Time.time;
        }

        [ServerRpc(RequireOwnership = true)]
        private void SwitchToModelServerRpc(int modelIndex)
        {
            Log($"Server: SwitchToModelServerRpc({modelIndex})");

            // Validate index
            if (modelIndex < 0 || modelIndex >= availableModels.Count)
            {
                Debug.LogError($"[ModelSwitcher] Invalid index {modelIndex}! Available: {availableModels.Count}");
                return;
            }

            // Update NetworkVariable (triggers OnModelIndexChanged on all clients)
            currentModelIndex.Value = modelIndex;
        }

        private void OnModelIndexChanged(int oldValue, int newValue)
        {
            Log($"ModelIndex changed: {oldValue} -> {newValue} (Server:{IsServer}, Host:{IsHost})");
            UpdateModel(newValue);
        }

        private void UpdateModel(int modelIndex)
        {
            if (modelIndex < 0 || modelIndex >= availableModels.Count)
            {
                Debug.LogError($"[ModelSwitcher] Invalid index {modelIndex}! Available: {availableModels.Count}");
                return;
            }

            ModelConfigData selectedModel = availableModels[modelIndex];
            CurrentModelData = selectedModel;

            Log($"Updating to: {selectedModel.modelName}");

            // Refresh equipment bindings
            PlayerEquipment?.RefeshEquipableItemsForModel();

            // Swap model
            DestroyCurrentModel();
            CreateNewModel(selectedModel);

            Log($"Model switched successfully: {selectedModel.modelName}");
        }

        private void DestroyCurrentModel()
        {
            if (!currentModel) return;

            Log($"Destroying: {currentModel.name}");

            if (currentModel.TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
            {
                netObj.Despawn(true);
            }
            else
            {
                Destroy(currentModel, 0.1f);
            }

            currentModel = null;
            CurrentAnimator = null;
        }

        private void CreateNewModel(ModelConfigData modelData)
        {
            if (!modelData.modelPrefab)
            {
                Debug.LogError($"[ModelSwitcher] Null prefab for {modelData.modelName}");
                return;
            }

            Log($"Creating: {modelData.modelName}");

            // Instantiate
            currentModel = Instantiate(modelData.modelPrefab, modelContainer);
            currentModel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Spawn NetworkObject if needed
            if (currentModel.TryGetComponent<NetworkObject>(out var netObj) && !netObj.IsSpawned && IsServer)
            {
                netObj.SpawnWithOwnership(OwnerClientId);
            }

            // Setup animator
            SetupAnimator(modelData);

            // Re-equip weapon after short delay
            if (PlayerEquipment) Invoke(nameof(ReEquipWeapon), reEquipDelay);

            // Notify listeners
            OnAnimatorChanged?.Invoke(CurrentAnimator);
            OnModelChanged?.Invoke(currentModel, CurrentModelData);
        }

        private void SetupAnimator(ModelConfigData modelData)
        {
            var animator = currentModel.GetComponent<Animator>();
            if (!animator)
            {
                Debug.LogError($"[ModelSwitcher] No Animator on {modelData.modelName}!");
                return;
            }

            // Apply overrides
            if (modelData.overrideController) animator.runtimeAnimatorController = modelData.overrideController;
            if (modelData.avatar) animator.avatar = modelData.avatar;

            CurrentAnimator = animator;

            // Sync with animation system
            if (animationSync)
            {
                animationSync.SetAnimator(animator);
                Log($"Animator synced: {modelData.modelName}");
            }
            else
            {
                Debug.LogError($"[ModelSwitcher] PlayerAnimationSync missing on {gameObject.name}!");
            }
        }

        private void ReEquipWeapon()
        {
            PlayerEquipment?.ReEquipWeapon();
        }

        #endregion

        #region Public API

        public void SetGameMode(GameMode newMode)
        {
            currentGameMode = newMode;
            RefreshAvailableModels();

            if (IsOwner && availableModels.Count > 0)
            {
                SwitchToModelServerRpc(0);
            }
        }

        public void OnPlayerRoleChanged(Role newRole)
        {
            RefreshAvailableModels();

            if (IsOwner && availableModels.Count > 0)
            {
                SwitchToModelServerRpc(0);
            }
        }

        public PlayerAnimationSync GetAnimationSync() => animationSync;
        public string GetCurrentModelName() => CurrentModelData?.modelName ?? "None";
        public bool CanSwitchModel() => availableModels.Count > 1 && CanSwitch;
        public int GetAvailableModelCount() => availableModels.Count;

        #endregion

        #region Debug Helpers

        private void Log(string message)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[ModelSwitcher] {message}");
            }
        }

        #endregion
    }
}
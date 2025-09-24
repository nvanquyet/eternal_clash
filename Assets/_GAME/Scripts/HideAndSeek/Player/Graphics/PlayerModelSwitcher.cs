// ==================== FIXED PlayerModelSwitcher ====================
using System.Collections;
using System.Collections.Generic;
using _GAME.Scripts.HideAndSeek.Player.Rig;
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
        [SerializeField] private PlayerEquipment playerEquipment;
        [SerializeField] private InputActionReference switchModelAction;
        [SerializeField] private GameMode currentGameMode = GameMode.PersonVsPerson;

        [Header("Performance Settings")] [SerializeField]
        private float switchCooldown = 0.5f;

        [Header("Model Container")] [SerializeField]
        private Transform modelContainer;

        [Header("Debug Settings")] [SerializeField]
        private bool enableDebugLogging = true;

        // Network Variables - Server writes, Everyone reads
        private NetworkVariable<int> currentModelIndex = new NetworkVariable<int>(0, 
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Current State
        private GameObject currentModel;
        private List<ModelConfigData> availableModels = new List<ModelConfigData>();

        // Components
        private PlayerController playerController;

        // Performance tracking
        private float lastSwitchTime;

        // Events
        public System.Action<Animator> OnAnimatorChanged;
        public System.Action<GameObject, ModelConfigData> OnModelChanged;
        private InputAction _switchModelAction;

        // Properties
        public Animator CurrentAnimator { get; private set; }
        public ModelConfigData CurrentModelData { get; private set; }
        public bool CanSwitch => (Time.time - lastSwitchTime) > switchCooldown;

        #region Initialization

        private void Awake()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            playerController = GetComponent<PlayerController>();
            SetupModelContainer();

            if (modelConfig == null)
            {
                Debug.LogError($"ModelConfigSO is not assigned on {gameObject.name}!");
            }
        }

        private void SetupModelContainer()
        {
            if (modelContainer == null)
            {
                GameObject containerObj = new GameObject("ModelContainer");
                containerObj.transform.SetParent(transform);
                containerObj.transform.localPosition = Vector3.zero;
                containerObj.transform.localRotation = Quaternion.identity;
                modelContainer = containerObj.transform;
            }
        }

        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            if (enableDebugLogging)
                Debug.Log($"PlayerModelSwitcher OnNetworkSpawn - IsOwner: {IsOwner}, IsServer: {IsServer}, ClientId: {OwnerClientId}");

            // Initialize available models for everyone
            RefreshAvailableModels();

            // Subscribe to network variable changes for ALL clients (including host/server)
            currentModelIndex.OnValueChanged += OnModelIndexChanged;

            if (IsOwner)
            {
                StartCoroutine(InitializeOwnerModel());
                
                // Setup input action
                if (switchModelAction != null)
                {
                    _switchModelAction = InputActionFactory.CreateUniqueAction(switchModelAction, GetInstanceID());
                    _switchModelAction.Enable();
                    _switchModelAction.performed += OnSwitchModelPerformed;
                }
            }
            
            // Initialize model for current network value (for all clients including server/host)
            StartCoroutine(InitializeModelFromNetwork());
        }

        private IEnumerator InitializeOwnerModel()
        {
            yield return null; // Wait one frame for proper initialization
            
            if (enableDebugLogging)
                Debug.Log($"Owner initializing first model. Available models: {availableModels.Count}");
            
            if (availableModels.Count > 0)
            {
                // Owner requests server to switch model
                SwitchToModelServerRpc(0);
            }
        }

        private IEnumerator InitializeModelFromNetwork()
        {
            yield return null; // Wait for network sync
            
            if (enableDebugLogging)
                Debug.Log($"Initializing model from network value: {currentModelIndex.Value}");
            
            // Update model for current network value (this applies to ALL clients including server/host)
            if (currentModelIndex.Value >= 0 && currentModelIndex.Value < availableModels.Count)
            {
                UpdateModel(currentModelIndex.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (enableDebugLogging)
                Debug.Log($"PlayerModelSwitcher OnNetworkDespawn - ClientId: {OwnerClientId}");

            if (currentModelIndex != null)
            {
                currentModelIndex.OnValueChanged -= OnModelIndexChanged;
            }

            if (IsOwner && _switchModelAction != null)
            {
                _switchModelAction.performed -= OnSwitchModelPerformed;
                _switchModelAction.Disable();
            }
            
            base.OnNetworkDespawn();
        }

        #endregion

        #region Model Management

        private void RefreshAvailableModels()
        {
            if (modelConfig == null) 
            {
                Debug.LogError("ModelConfig is null, cannot refresh available models");
                return;
            }

            Role currentRole = playerController?.CurrentRole ?? Role.None;
            availableModels = modelConfig.GetModelsForGameMode(currentGameMode, currentRole);

            if (enableDebugLogging)
                Debug.Log($"Refreshed available models for {currentGameMode} - {currentRole}: {availableModels.Count} models found");
        }

        private void OnSwitchModelPerformed(InputAction.CallbackContext obj)
        {
            if (enableDebugLogging)
                Debug.Log($"Switch model input performed - CanSwitch: {CanSwitch}, IsOwner: {IsOwner}");
            
            if (!IsOwner || !CanSwitch) return;
            SwitchToNextModel();
        }
        
        private void SwitchToNextModel()
        {
            if (availableModels.Count <= 1) 
            {
                if (enableDebugLogging)
                    Debug.Log($"Cannot switch model - available models: {availableModels.Count}");
                return;
            }

            int nextIndex = (currentModelIndex.Value + 1) % availableModels.Count;
            
            if (enableDebugLogging)
                Debug.Log($"Client requesting switch from model {currentModelIndex.Value} to {nextIndex}");
            
            // Send to server to update NetworkVariable
            SwitchToModelServerRpc(nextIndex);
            lastSwitchTime = Time.time;
        }

        [ServerRpc(RequireOwnership = true)]
        private void SwitchToModelServerRpc(int modelIndex)
        {
            if (enableDebugLogging)
                Debug.Log($"Server: SwitchToModelServerRpc called with index {modelIndex}. Available models: {availableModels.Count}");

            // Ensure server has up-to-date available models
            RefreshAvailableModels();
            
            if (modelIndex >= 0 && modelIndex < availableModels.Count)
            {
                int oldValue = currentModelIndex.Value;
                
                // FIX: Server updates NetworkVariable - this will trigger OnModelIndexChanged for ALL clients
                currentModelIndex.Value = modelIndex;
                
                if (enableDebugLogging)
                    Debug.Log($"Server: Model index updated from {oldValue} to {currentModelIndex.Value} for client {OwnerClientId}");
            }
            else
            {
                Debug.LogError($"Server: Invalid model index {modelIndex}! Available models: {availableModels.Count}");
            }
        }

        private void OnModelIndexChanged(int previousValue, int newValue)
        {
            if (enableDebugLogging)
                Debug.Log($"OnModelIndexChanged: {previousValue} -> {newValue} for client {OwnerClientId}, IsServer: {IsServer}, IsHost: {IsHost}");
            
            // This will be called on ALL clients (including server/host) when NetworkVariable changes
            UpdateModel(newValue);
        }

        private void UpdateModel(int modelIndex)
        {
            if (enableDebugLogging)
                Debug.Log($"UpdateModel called with index {modelIndex}. Available models: {availableModels.Count}, IsServer: {IsServer}, IsHost: {IsHost}");

            // Ensure we have fresh available models
            RefreshAvailableModels();

            if (modelIndex < 0 || modelIndex >= availableModels.Count)
            {
                Debug.LogError($"Invalid model index {modelIndex}! Available models: {availableModels.Count}");
                return;
            }

            ModelConfigData selectedModel = availableModels[modelIndex];
            CurrentModelData = selectedModel;

            if (enableDebugLogging)
                Debug.Log($"Updating to model: {selectedModel.modelName} on {(IsServer ? "Server" : "Client")} for player {OwnerClientId}");

            if(playerEquipment != null) playerEquipment.RefeshEquipableItemsForModel();
            
            // Destroy current model
            DestroyCurrentModel();

            // Create new model
            CreateNewModel(selectedModel);

            if (enableDebugLogging)
                Debug.Log($"Player {OwnerClientId} successfully switched to model: {selectedModel.modelName}");
        }
        
        
        private void DestroyCurrentModel()
        {
            if (currentModel != null)
            {
                if (enableDebugLogging)
                    Debug.Log($"Destroying current model: {currentModel.name}");

                if (currentModel.TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
                {
                    netObj.Despawn(true);
                }
                else
                {
                    Destroy(currentModel.gameObject, 0.1f);
                }
                
                currentModel = null;
                CurrentAnimator = null;
            }
        }

        private void CreateNewModel(ModelConfigData modelData)
        {
            if (modelData.modelPrefab == null)
            {
                Debug.LogError($"Model prefab is null for {modelData.modelName}");
                return;
            }

            if (enableDebugLogging)
                Debug.Log($"Creating new model: {modelData.modelName}");

            // Instantiate new model
            currentModel = Instantiate(modelData.modelPrefab, modelContainer);
            
            // Handle NetworkObject spawning if needed
            if (currentModel.TryGetComponent<NetworkObject>(out var netObj) && !netObj.IsSpawned)
            {
                if (IsServer)
                {
                    netObj.SpawnWithOwnership(OwnerClientId);
                }
            }
            
            // Set transform
            currentModel.transform.SetParent(modelContainer ?? this.transform);
            currentModel.transform.localPosition = Vector3.zero;
            currentModel.transform.localRotation = Quaternion.identity;
            
            // Setup animator
            SetupModelAnimator(modelData);
            
            //Setup player rig
            Invoke(nameof(ReEquip), 0.2f);

            // Disable colliders
            DisableModelColliders();

            // Notify systems
            NotifyModelChanged();
        }

        private void ReEquip()
        {
            playerEquipment.ReEquipWeapon();
        }

        private void SetupModelAnimator(ModelConfigData modelData)
        {
            Animator modelAnimator = currentModel.GetComponent<Animator>();

            if (modelAnimator == null)
            {
                Debug.LogError($"Model {modelData.modelName} doesn't have Animator component!");
                return;
            }

            if (modelData.overrideController != null)
            {
                modelAnimator.runtimeAnimatorController = modelData.overrideController;
            }

            if (modelData.avatar != null)
            {
                modelAnimator.avatar = modelData.avatar;
            }
            
            CurrentAnimator = modelAnimator;
            
            // Update animation sync component
            if (animationSync != null)
            {
                animationSync.SetAnimator(modelAnimator);
                
                if (enableDebugLogging)
                    Debug.Log($"Animator set on AnimationSync for model: {modelData.modelName}");
            }
            else
            {
                Debug.LogError($"PlayerAnimationSync is not assigned on {gameObject.name}!");
            }
        }

        private void DisableModelColliders()
        {
            Collider[] colliders = currentModel.GetComponentsInChildren<Collider>();
            foreach (Collider col in colliders)
            {
                col.enabled = false;
            }

            Rigidbody[] rigidbodies = currentModel.GetComponentsInChildren<Rigidbody>();
            foreach (Rigidbody rb in rigidbodies)
            {
                rb.isKinematic = true;
            }
        }

        private void NotifyModelChanged()
        {
            OnAnimatorChanged?.Invoke(CurrentAnimator);
            OnModelChanged?.Invoke(currentModel, CurrentModelData);
        }

        #endregion

        #region Public API

        public PlayerAnimationSync GetAnimationSync() => animationSync;

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
        
        // Other public methods...
        public string GetCurrentModelName() => CurrentModelData?.modelName ?? "None";
        public bool CanSwitchModel() => availableModels.Count > 1 && CanSwitch;
        public int GetAvailableModelCount() => availableModels.Count;

        #endregion
    }
}

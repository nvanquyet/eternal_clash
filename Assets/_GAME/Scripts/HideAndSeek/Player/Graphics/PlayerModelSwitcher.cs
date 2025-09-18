using System.Collections.Generic;
using _GAME.Scripts.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.HideAndSeek.Player.Graphics
{
    public class PlayerModelSwitcher : NetworkBehaviour
    {
        [Header("Configuration")] [SerializeField]
        private ModelConfigSO modelConfig;
        [SerializeField] private PlayerAnimationSync animationSync;
        
        [SerializeField] private InputActionReference switchModelAction;

        [SerializeField] private GameMode currentGameMode = GameMode.PersonVsPerson;

        [Header("Performance Settings")] [SerializeField]
        private float switchCooldown = 0.5f;

        [Header("Model Container")] [SerializeField]
        private Transform modelContainer;

        // Network Variables
        private NetworkVariable<int> currentModelIndex = new NetworkVariable<int>(0);

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

            // Setup model container
            SetupModelContainer();

            // Validate config
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

        private void Start()
        {
            currentModelIndex.OnValueChanged += OnModelIndexChanged;

            // Initialize available models
            RefreshAvailableModels();

            // Initialize first model
            if (IsOwner)
            {
                SwitchToModelServerRpc(0);
                
                //Setup input action
                if (switchModelAction != null)
                {
                    _switchModelAction = switchModelAction.action;
                    _switchModelAction.Enable();
                    _switchModelAction.performed += OnSwitchModelPerformed;
                }
            }
        }

       

        public override void OnNetworkDespawn()
        {
            if (currentModelIndex != null)
            {
                currentModelIndex.OnValueChanged -= OnModelIndexChanged;
            }

            if (IsOwner)
            {
                //Setup input action
                if (switchModelAction != null)
                {
                    _switchModelAction = switchModelAction.action;
                    _switchModelAction.Enable();
                    _switchModelAction.performed += OnSwitchModelPerformed;
                }
            }
            base.OnNetworkDespawn();
        }

        #endregion

        #region Model Management

        private void RefreshAvailableModels()
        {
            if (modelConfig == null) return;

            Role currentRole = playerController?.CurrentRole ?? Role.None;
            availableModels = modelConfig.GetModelsForGameMode(currentGameMode, currentRole);

            Debug.Log($"Available models for {currentGameMode} - {currentRole}: {availableModels.Count}");
        }

        private void OnSwitchModelPerformed(InputAction.CallbackContext obj)
        {
            if (!IsOwner || !CanSwitch) return;
            SwitchToNextModel();
        }
        
        private void SwitchToNextModel()
        {
            if (availableModels.Count <= 1) return;

            int nextIndex = (currentModelIndex.Value + 1) % availableModels.Count;
            SwitchToModelServerRpc(nextIndex);

            lastSwitchTime = Time.time;
        }

        [ServerRpc]
        private void SwitchToModelServerRpc(int modelIndex)
        {
            if (modelIndex >= 0 && modelIndex < availableModels.Count)
            {
                currentModelIndex.Value = modelIndex;
            }
        }

        private void OnModelIndexChanged(int previousValue, int newValue)
        {
            UpdateModel(newValue);
        }

        private void UpdateModel(int modelIndex)
        {
            if (modelIndex >= availableModels.Count) return;

            ModelConfigData selectedModel = availableModels[modelIndex];
            CurrentModelData = selectedModel;

            // Destroy current model
            DestroyCurrentModel();

            // Create new model
            CreateNewModel(selectedModel);

            Debug.Log($"Player {OwnerClientId} switched to model: {selectedModel.modelName}");
        }

        private void DestroyCurrentModel()
        {
            if (currentModel != null)
            {
                if (Application.isPlaying)
                    Destroy(currentModel);
                else
                    DestroyImmediate(currentModel);
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

            // Instantiate new model
            currentModel = Instantiate(modelData.modelPrefab, modelContainer);
            currentModel.transform.localPosition = Vector3.zero;
            currentModel.transform.localRotation = Quaternion.identity;

            // Setup animator - Model prefab should already have Animator with OverrideController
            SetupModelAnimator(modelData);

            // Disable any colliders on model (Player root should handle collision)
            DisableModelColliders();

            // Notify systems about the change
            NotifyModelChanged();
        }

        private void SetupModelAnimator(ModelConfigData modelData)
        {
            Animator modelAnimator = currentModel.GetComponent<Animator>();

            if (modelAnimator == null)
            {
                Debug.LogError($"Model {modelData.modelName} doesn't have Animator component!");
                return;
            }

            // Model prefab should already have OverrideController assigned
            // But we can override it if specified in config
            if (modelData.overrideController != null)
            {
                modelAnimator.runtimeAnimatorController = modelData.overrideController;
            }

            if (modelData.avatar != null)
            {
                modelAnimator.avatar = modelData.avatar;
            }
            
            CurrentAnimator = modelAnimator;
            animationSync.SetAnimator(modelAnimator);
        }

        private void DisableModelColliders()
        {
            // Disable all colliders on the model - Player root handles collision
            Collider[] colliders = currentModel.GetComponentsInChildren<Collider>();
            foreach (Collider col in colliders)
            {
                col.enabled = false;
            }

            // Disable any Rigidbodies too
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

            if (IsOwner)
            {
                // Reset to first model of new mode
                SwitchToModelServerRpc(0);
            }
        }

        public string GetCurrentModelName()
        {
            return CurrentModelData?.modelName ?? "None";
        }

        public List<string> GetAvailableModelNames()
        {
            List<string> names = new List<string>();
            foreach (var model in availableModels)
            {
                names.Add(model.modelName);
            }

            return names;
        }

        public void SwitchToModel(int modelIndex)
        {
            if (!IsOwner || !CanSwitch) return;
            SwitchToModelServerRpc(modelIndex);
            lastSwitchTime = Time.time;
        }

        public void SwitchToModelByName(string modelName)
        {
            for (int i = 0; i < availableModels.Count; i++)
            {
                if (availableModels[i].modelName == modelName)
                {
                    SwitchToModel(i);
                    return;
                }
            }

            Debug.LogWarning($"Model {modelName} not found in available models");
        }

        public bool CanSwitchModel()
        {
            return availableModels.Count > 1 && CanSwitch;
        }

        public int GetAvailableModelCount()
        {
            return availableModels.Count;
        }

        // Called when player role changes
        public void OnPlayerRoleChanged(Role newRole)
        {
            RefreshAvailableModels();

            if (IsOwner && availableModels.Count > 0)
            {
                // Switch to first available model for new role
                SwitchToModelServerRpc(0);
            }
        }

        #endregion
    }
}
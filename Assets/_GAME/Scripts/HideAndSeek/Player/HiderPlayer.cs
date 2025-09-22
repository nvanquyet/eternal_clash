using System;
using System.Collections.Generic;
using System.Linq;
using _GAME.Scripts.HideAndSeek.SkillSystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.HideAndSeek.Player
{
    /// <summary>
    /// Hider player implementation with stealth and task completion abilities
    /// Network synchronized with proper validation
    /// </summary>
    public class HiderPlayer : RolePlayer
    {
        [Header("Hider Settings")] 
        [SerializeField] private int totalTasks = 5;
        [SerializeField] private Transform ghostCamera; // For soul mode
        [SerializeField] private LayerMask normalLayer = 0;
        [SerializeField] private LayerMask soulLayer = 1;

        [Header("Input References")]
        [SerializeField] private InputActionReference teleportSkillRef;
        [SerializeField] private InputActionReference freezeSkillRef;
        [SerializeField] private InputActionReference toggleSoulModeRef;

        // Input Actions
        private InputAction teleportSkillAction;
        private InputAction freezeSkillAction;
        private InputAction toggleSoulModeAction;

        // Network Variables (Server Authoritative)
        private NetworkVariable<int> networkCompletedTasks = new NetworkVariable<int>(
            0, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );

        private NetworkVariable<bool> networkInSoulMode = new NetworkVariable<bool>(
            false, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );

        private NetworkVariable<float> networkSoulModeEnergy = new NetworkVariable<float>(
            100f, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );

        // Server-side task validation
        private readonly HashSet<int> completedTaskIds = new HashSet<int>();
        private readonly Dictionary<int, float> taskCompletionTimes = new Dictionary<int, float>();

        // Events
        public static event Action<int, int> OnTaskProgressChanged;
        public static event Action<bool> OnSoulModeChanged;
        public static event Action<float> OnSoulEnergyChanged;

        #region IGamePlayer Implementation

        public int CompletedTasks => networkCompletedTasks.Value;
        public int TotalTasks => totalTasks;
        public bool IsInSoulMode => networkInSoulMode.Value;
        public float SoulModeEnergy => networkSoulModeEnergy.Value;
        public override bool HasSkillsAvailable => Skills.Values.Any(s => s.CanUse) && IsAlive;

        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Subscribe to network variable changes
            networkCompletedTasks.OnValueChanged += OnTasksNetworkChanged;
            networkInSoulMode.OnValueChanged += OnSoulModeNetworkChanged;
            networkSoulModeEnergy.OnValueChanged += OnSoulEnergyNetworkChanged;

            // Initialize server-side values
            if (IsServer)
            {
                networkCompletedTasks.Value = 0;
                networkInSoulMode.Value = false;
                networkSoulModeEnergy.Value = 100f;
                totalTasks = GameManager?.Settings?.tasksToComplete ?? 5;
            }

            LogNetworkState("HiderPlayer spawned");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            // Cleanup subscriptions
            if (networkCompletedTasks != null)
                networkCompletedTasks.OnValueChanged -= OnTasksNetworkChanged;
            if (networkInSoulMode != null)
                networkInSoulMode.OnValueChanged -= OnSoulModeNetworkChanged;
            if (networkSoulModeEnergy != null)
                networkSoulModeEnergy.OnValueChanged -= OnSoulEnergyNetworkChanged;

            // Cleanup server-side data
            if (IsServer)
            {
                completedTaskIds.Clear();
                taskCompletionTimes.Clear();
            }

            LogNetworkState("HiderPlayer despawned");
        }

        #endregion

        #region Skill Initialization

        protected override void InitializeSkills()
        {
            if (Role != Role.Hider) return;

            try
            {
                // Initialize hider skills with proper validation
                var freezeSkill = gameObject.GetComponent<FreezeSkill>() ?? gameObject.AddComponent<FreezeSkill>();
                freezeSkill.Initialize(SkillType.FreezeSeeker, GameManager.GetSkillData(SkillType.FreezeSeeker));
                Skills[SkillType.FreezeSeeker] = freezeSkill;

                var teleportSkill = gameObject.GetComponent<TeleportSkill>() ?? gameObject.AddComponent<TeleportSkill>();
                teleportSkill.Initialize(SkillType.Teleport, GameManager.GetSkillData(SkillType.Teleport));
                Skills[SkillType.Teleport] = teleportSkill;
                Debug.Log($"[HiderPlayer] Skills initialized: {Skills.Count}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[HiderPlayer] Failed to initialize skills: {ex.Message}");
            }
        }

        #endregion

        #region Input System

        protected override void HandleRegisterInput()
        {
            if (!IsOwner) return;

            try
            {
                // Teleport skill
                if (teleportSkillRef?.action != null)
                {
                    teleportSkillAction = teleportSkillRef.action;
                    teleportSkillAction.performed += OnTeleportSkillPerformed;
                    teleportSkillAction.Enable();
                }

                // Freeze skill
                if (freezeSkillRef?.action != null)
                {
                    freezeSkillAction = freezeSkillRef.action;
                    freezeSkillAction.performed += OnFreezeSkillPerformed;
                    freezeSkillAction.Enable();
                }

                // Soul mode toggle
                if (toggleSoulModeRef?.action != null)
                {
                    toggleSoulModeAction = toggleSoulModeRef.action;
                    toggleSoulModeAction.performed += OnToggleSoulModePerformed;
                    toggleSoulModeAction.Enable();
                }

                Debug.Log("[HiderPlayer] Input registered successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[HiderPlayer] Failed to register input: {ex.Message}");
            }
        }

        protected override void HandleUnRegisterInput()
        {
            if (!IsOwner) return;

            try
            {
                teleportSkillAction?.Disable();
                freezeSkillAction?.Disable();
                toggleSoulModeAction?.Disable();

                Debug.Log("[HiderPlayer] Input unregistered successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[HiderPlayer] Failed to unregister input: {ex.Message}");
            }
        }

        #region Input Callbacks

        private void OnTeleportSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || !IsAlive) return;
            
            Vector3 destination = GetSafeTeleportDestination();
            UseSkill(SkillType.Teleport, destination);
        }

        private void OnFreezeSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || !IsAlive) return;
            
            UseSkill(SkillType.FreezeSeeker, transform.position);
        }
        

        private void OnToggleSoulModePerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || !IsAlive) return;
            
            ToggleSoulMode();
        }

        private Vector3 GetSafeTeleportDestination()
        {
            // Implement safe teleport location finding
            Vector3 currentPos = transform.position;
            Vector3 randomDirection = UnityEngine.Random.insideUnitSphere;
            randomDirection.y = 0; // Keep on ground level
            
            Vector3 destination = currentPos + randomDirection.normalized * 10f;
            
            // Add ground check and obstacle avoidance logic here
            if (Physics.Raycast(destination + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f))
            {
                destination = hit.point;
            }
            
            return destination;
        }

        #endregion

        #endregion

        #region Task System (Server Authoritative)

        public void CompleteTask(int taskId)
        {
            if (!IsOwner) return;
            
            // Client-side validation
            if (!CanCompleteTaskLocally(taskId))
            {
                ShowTaskErrorMessage("Cannot complete this task");
                return;
            }

            // Send to server for authoritative validation
            CompleteTaskServerRpc(taskId, transform.position);
        }

        [ServerRpc]
        private void CompleteTaskServerRpc(int taskId, Vector3 playerPosition)
        {
            if (!IsServer) return;

            // Server-side validation
            if (!ValidateTaskCompletionServer(taskId, playerPosition))
            {
                NotifyTaskFailedClientRpc(taskId, "Server validation failed");
                return;
            }

            // Mark task as completed
            completedTaskIds.Add(taskId);
            taskCompletionTimes[taskId] = Time.time;
            networkCompletedTasks.Value++;

            // Notify game manager
            GameManager?.OnPlayerTaskCompleted(ClientId, taskId);

            // Notify all clients
            OnTaskCompletedClientRpc(taskId, playerPosition);

            Debug.Log($"[HiderPlayer] Task {taskId} completed by client {ClientId}. Progress: {networkCompletedTasks.Value}/{totalTasks}");
        }

        [ClientRpc]
        private void OnTaskCompletedClientRpc(int taskId, Vector3 completionPosition)
        {
            OnTaskCompletedLocal(taskId, completionPosition);
        }

        [ClientRpc]
        private void NotifyTaskFailedClientRpc(int taskId, string reason)
        {
            if (IsOwner)
            {
                ShowTaskErrorMessage($"Task {taskId} failed: {reason}");
            }
        }

        #region Task Validation

        private bool CanCompleteTaskLocally(int taskId)
        {
            if (!IsAlive || !IsOwner) return false;
            if (completedTaskIds.Contains(taskId)) return false;
            if (networkCompletedTasks.Value >= totalTasks) return false;
            
            return true;
        }

        private bool ValidateTaskCompletionServer(int taskId, Vector3 playerPosition)
        {
            if (!IsServer) return false;
            
            // Check if task already completed
            if (completedTaskIds.Contains(taskId)) return false;
            
            // Check if player is alive
            if (!networkIsAlive.Value) return false;
            
            // Check task limit
            if (networkCompletedTasks.Value >= totalTasks) return false;
            
            // Validate task exists and is accessible at player position
            if (!GameManager.IsTaskValidAtPosition(taskId, playerPosition, ClientId))
            {
                Debug.LogWarning($"[HiderPlayer] Task {taskId} not valid at position {playerPosition} for client {ClientId}");
                return false;
            }
            
            return true;
        }

        private void OnTasksNetworkChanged(int previousValue, int newValue)
        {
            OnTaskProgressChanged?.Invoke(newValue, totalTasks);

            if (IsOwner)
            {
                UpdateTaskUI(newValue, totalTasks);
            }

            // Check win condition
            if (newValue >= totalTasks && IsServer)
            {
                GameManager?.CheckHiderWinCondition(ClientId);
            }
        }

        private void OnTaskCompletedLocal(int taskId, Vector3 position)
        {
            // Local effects
            if (IsOwner)
            {
                ShowTaskCompletedFeedback(taskId);
            }

            // Play completion effect at position for all clients
            PlayTaskCompletionEffect(position);
        }

        #endregion

        #endregion

        #region Soul Mode System (Server Authoritative)

        public void ToggleSoulMode()
        {
            if (!IsOwner) return;
            
            // Client-side validation
            if (!CanToggleSoulModeLocally())
            {
                ShowSoulModeErrorMessage("Cannot toggle soul mode");
                return;
            }

            ToggleSoulModeServerRpc();
        }

        [ServerRpc]
        private void ToggleSoulModeServerRpc()
        {
            if (!IsServer) return;

            // Server-side validation
            if (!ValidateSoulModeToggleServer())
            {
                NotifySoulModeFailedClientRpc("Soul mode toggle failed");
                return;
            }

            bool newSoulMode = !networkInSoulMode.Value;
            networkInSoulMode.Value = newSoulMode;

            Debug.Log($"[HiderPlayer] Soul mode toggled to {newSoulMode} for client {ClientId}");
        }

        [ClientRpc]
        private void NotifySoulModeFailedClientRpc(string reason)
        {
            if (IsOwner)
            {
                ShowSoulModeErrorMessage(reason);
            }
        }

        private void OnSoulModeNetworkChanged(bool previousValue, bool newValue)
        {
            ApplySoulMode(newValue);
            OnSoulModeChanged?.Invoke(newValue);

            if (IsOwner)
            {
                UpdateSoulModeUI(newValue);
            }
        }

        private void OnSoulEnergyNetworkChanged(float previousValue, float newValue)
        {
            OnSoulEnergyChanged?.Invoke(newValue);

            if (IsOwner)
            {
                UpdateSoulEnergyUI(newValue);
            }
        }

        #region Soul Mode Validation

        private bool CanToggleSoulModeLocally()
        {
            if (!IsAlive || !IsOwner) return false;
            if (networkSoulModeEnergy.Value < 10f) return false; // Minimum energy required
            
            return true;
        }

        private bool ValidateSoulModeToggleServer()
        {
            if (!IsServer) return false;
            if (!networkIsAlive.Value) return false;
            if (networkSoulModeEnergy.Value < 10f) return false;
            
            return true;
        }

        private void ApplySoulMode(bool soulMode)
        {
            try
            {
                if (soulMode)
                {
                    EnableSoulMode();
                }
                else
                {
                    DisableSoulMode();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[HiderPlayer] Failed to apply soul mode: {ex.Message}");
            }
        }

        private void EnableSoulMode()
        {
            // Enable ghost camera for owner
            if (IsOwner && ghostCamera != null)
            {
                ghostCamera.gameObject.SetActive(true);
            }

            // Change layer for visibility
            SetLayerRecursively(gameObject, soulLayer);

            // Reduce collision detection
            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }

            // Start energy drain if on server
            if (IsServer)
            {
                StartSoulModeEnergyDrain();
            }

            Debug.Log("[HiderPlayer] Soul mode enabled");
        }

        private void DisableSoulMode()
        {
            // Disable ghost camera for owner
            if (IsOwner && ghostCamera != null)
            {
                ghostCamera.gameObject.SetActive(false);
            }

            // Return to normal layer
            SetLayerRecursively(gameObject, normalLayer);

            // Restore collision detection
            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = false;
            }

            // Stop energy drain if on server
            if (IsServer)
            {
                StopSoulModeEnergyDrain();
            }

            Debug.Log("[HiderPlayer] Soul mode disabled");
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                if (child != null)
                    SetLayerRecursively(child.gameObject, layer);
            }
        }

        #endregion

        #region Soul Mode Energy Management

        private void StartSoulModeEnergyDrain()
        {
            if (!IsServer) return;
            
            InvokeRepeating(nameof(DrainSoulModeEnergy), 1f, 1f);
        }

        private void StopSoulModeEnergyDrain()
        {
            if (!IsServer) return;
            
            CancelInvoke(nameof(DrainSoulModeEnergy));
            InvokeRepeating(nameof(RegenerateSoulModeEnergy), 1f, 1f);
        }

        private void DrainSoulModeEnergy()
        {
            if (!IsServer || !networkInSoulMode.Value) return;

            float newEnergy = Mathf.Max(0f, networkSoulModeEnergy.Value - 2f);
            networkSoulModeEnergy.Value = newEnergy;

            // Auto-disable soul mode when energy depleted
            if (newEnergy <= 0f)
            {
                networkInSoulMode.Value = false;
                CancelInvoke(nameof(DrainSoulModeEnergy));
            }
        }

        private void RegenerateSoulModeEnergy()
        {
            if (!IsServer || networkInSoulMode.Value) return;

            float newEnergy = Mathf.Min(100f, networkSoulModeEnergy.Value + 1f);
            networkSoulModeEnergy.Value = newEnergy;

            // Stop regeneration when full
            if (newEnergy >= 100f)
            {
                CancelInvoke(nameof(RegenerateSoulModeEnergy));
            }
        }

        #endregion

        #endregion

        #region Skill System Override

        protected override void OnSkillExecutedLocal(SkillType skillType, Vector3? target)
        {
            base.OnSkillExecutedLocal(skillType, target);

            if (IsOwner)
            {
                switch (skillType)
                {
                    case SkillType.Teleport:
                        ShowSkillFeedback("Teleported!");
                        PlayTeleportEffect();
                        break;
                    case SkillType.FreezeSeeker:
                        ShowSkillFeedback("Seeker Frozen!");
                        PlayFreezeEffect(target ?? transform.position);
                        break;
                }
            }
        }

        protected override void OnSkillUsageFailedLocal(SkillType skillType, string reason)
        {
            base.OnSkillUsageFailedLocal(skillType, reason);
            
            if (IsOwner)
            {
                ShowSkillErrorMessage($"{skillType}: {reason}");
            }
        }

        protected override void UpdateSkillCooldownUI()
        {
            if (!IsOwner) return;

            foreach (var skill in Skills)
            {
                UpdateSkillCooldownDisplay(skill.Key, skill.Value.GetCooldownTime());
            }
        }

        #endregion

        #region UI Updates

        protected virtual void UpdateTaskUI(int completedTasks, int totalTasks)
        {
            if (!IsOwner) return;
            
            // Update task progress UI
            Debug.Log($"[HiderPlayer] Task Progress: {completedTasks}/{totalTasks}");
            // HiderUI.Instance?.UpdateTaskProgress(completedTasks, totalTasks);
        }

        protected virtual void UpdateSoulModeUI(bool soulMode)
        {
            if (!IsOwner) return;
            
            Debug.Log($"[HiderPlayer] Soul Mode: {(soulMode ? "ON" : "OFF")}");
            // HiderUI.Instance?.UpdateSoulModeIndicator(soulMode);
        }

        protected virtual void UpdateSoulEnergyUI(float energy)
        {
            if (!IsOwner) return;
            
            Debug.Log($"[HiderPlayer] Soul Energy: {energy:F1}%");
            // HiderUI.Instance?.UpdateSoulEnergyBar(energy / 100f);
        }

        protected override void UpdateHealthUI(float currentHealth, float maxHealth)
        {
            if (!IsOwner) return;
            
            // Hiders may have reduced health UI
            Debug.Log($"[HiderPlayer] Health: {currentHealth}/{maxHealth}");
            // HiderUI.Instance?.UpdateHealthBar(currentHealth / maxHealth);
        }

        protected override void UpdateAliveStateUI(bool isAlive)
        {
            if (!IsOwner) return;
            
            if (!isAlive)
            {
                ShowDeathUI();
            }
        }

        private void UpdateSkillCooldownDisplay(SkillType skillType, float remainingCooldown)
        {
            // Update individual skill cooldown in UI
            Debug.Log($"[HiderPlayer] {skillType} cooldown: {remainingCooldown:F1}s");
            // HiderUI.Instance?.UpdateSkillCooldown(skillType, remainingCooldown);
        }

        #endregion

        #region UI Feedback Methods

        private void ShowTaskCompletedFeedback(int taskId)
        {
            Debug.Log($"[HiderPlayer] Task {taskId} completed!");
            // HiderUI.Instance?.ShowTaskCompletedFeedback(taskId);
        }

        private void ShowTaskErrorMessage(string message)
        {
            Debug.LogWarning($"[HiderPlayer] Task Error: {message}");
            // HiderUI.Instance?.ShowErrorMessage(message);
        }

        private void ShowSoulModeErrorMessage(string message)
        {
            Debug.LogWarning($"[HiderPlayer] Soul Mode Error: {message}");
            // HiderUI.Instance?.ShowErrorMessage(message);
        }

        private void ShowSkillFeedback(string message)
        {
            Debug.Log($"[HiderPlayer] Skill: {message}");
            // HiderUI.Instance?.ShowSkillFeedback(message);
        }

        private void ShowSkillErrorMessage(string message)
        {
            Debug.LogWarning($"[HiderPlayer] Skill Error: {message}");
            // HiderUI.Instance?.ShowErrorMessage(message);
        }

        private void ShowDeathUI()
        {
            Debug.Log("[HiderPlayer] Player died - showing death UI");
            // HiderUI.Instance?.ShowDeathScreen();
        }

        private void ShowSkillHints()
        {
            Debug.Log("[HiderPlayer] Skills: Q-Teleport, R-Freeze, T-Shapeshift, F-SoulMode");
            // HiderUI.Instance?.ShowSkillHints("Q - Teleport | R - Freeze Seeker | T - Shape Shift | F - Soul Mode");
        }

        private void ShowEndGameResults(Role winner)
        {
            string resultMessage = winner == Role.Hider ? "HIDERS WIN!" : "SEEKERS WIN!";
            Debug.Log($"[HiderPlayer] {resultMessage}");
            // HiderUI.Instance?.ShowGameResult(resultMessage, winner == Role.Hider);
        }

        #endregion

        #region Visual Effects

        private void PlayTaskCompletionEffect(Vector3 position)
        {
            // Play task completion particle effect
            Debug.Log($"[HiderPlayer] Playing task completion effect at {position}");
            // EffectManager.Instance?.PlayTaskCompletionEffect(position);
        }

        private void PlayTeleportEffect()
        {
            Debug.Log("[HiderPlayer] Playing teleport effect");
            // EffectManager.Instance?.PlayTeleportEffect(transform.position);
        }

        private void PlayFreezeEffect(Vector3 position)
        {
            Debug.Log($"[HiderPlayer] Playing freeze effect at {position}");
            // EffectManager.Instance?.PlayFreezeEffect(position);
        }

        private void PlayShapeshiftEffect()
        {
            Debug.Log("[HiderPlayer] Playing shapeshift effect");
            // EffectManager.Instance?.PlayShapeshiftEffect(transform.position);
        }

        #endregion

        #region Game Mode Initialization

        private void InitializePersonVsPerson()
        {
            totalTasks = GameManager?.Settings?.tasksToComplete ?? 5;

            if (IsOwner)
            {
                Debug.Log($"[HiderPlayer] Person vs Person mode - Need to complete {totalTasks} tasks");
                // HiderUI.Instance?.ShowTaskProgress(true);
                // HiderUI.Instance?.UpdateTaskProgress(0, totalTasks);
            }
        }

        private void InitializePersonVsObject()
        {
            if (IsOwner)
            {
                Debug.Log("[HiderPlayer] Person vs Object mode - Find disguises to hide");
                // HiderUI.Instance?.ShowDisguiseIndicator(true);
            }
        }

        #endregion

        #region Win Condition Checks

        private bool HasCompletedAllTasks()
        {
            return networkCompletedTasks.Value >= totalTasks;
        }

        public bool CanWin()
        {
            if (!IsAlive) return false;
            
            return GameManager?.CurrentMode switch
            {
                GameMode.PersonVsPerson => HasCompletedAllTasks(),
                GameMode.PersonVsObject => true, // Survive until timer runs out
                _ => false
            };
        }

        #endregion

        #region Game Events Override

        public override void OnGameStart()
        {
            Debug.Log($"[HiderPlayer] Game started for {PlayerName}");

            if (IsOwner)
            {
                // Initialize based on game mode
                switch (GameManager?.CurrentMode)
                {
                    case GameMode.PersonVsPerson:
                        InitializePersonVsPerson();
                        break;
                    case GameMode.PersonVsObject:
                        InitializePersonVsObject();
                        break;
                }

                // Enable hider-specific UI
                // HiderUI.Instance?.ShowHiderUI(true);
                // HiderUI.Instance?.UpdateSkillCooldowns(Skills);

                // Setup input prompts
                ShowSkillHints();
            }

            // Server initialization
            if (IsServer)
            {
                // Reset all network variables
                networkCompletedTasks.Value = 0;
                networkInSoulMode.Value = false;
                networkSoulModeEnergy.Value = 100f;
                
                // Clear server-side tracking
                completedTaskIds.Clear();
                taskCompletionTimes.Clear();
            }
        }

        public override void OnGameEnd(Role winnerRole)
        {
            Debug.Log($"[HiderPlayer] Game ended, winner: {winnerRole}");

            if (IsOwner)
            {
                // Handle game end cleanup
                if (IsInSoulMode)
                {
                    ApplySoulMode(false);
                }

                // Disable UI
                // HiderUI.Instance?.ShowHiderUI(false);

                // Disable controls
                HandleUnRegisterInput();

                // Show end game UI
                ShowEndGameResults(winnerRole);
            }

            // Server cleanup
            if (IsServer)
            {
                // Cancel all recurring invokes
                CancelInvoke();
                
                // Reset states
                networkInSoulMode.Value = false;
                networkSoulModeEnergy.Value = 100f;
            }
        }

        #endregion

        #region Update Loop for Energy Management

        private void Update()
        {
            // Only run energy management on server
            if (!IsServer) return;

            // Handle soul mode energy drain/regen
            HandleSoulModeEnergyUpdate();
        }

        private void HandleSoulModeEnergyUpdate()
        {
            if (!IsServer) return;

            // This provides a backup to the InvokeRepeating system
            // Could be used for more precise energy management if needed
        }

        #endregion

        #region Debug and Logging

        public override string ToString()
        {
            return $"HiderPlayer[{ClientId}] - Role:{Role}, Alive:{IsAlive}, Tasks:{CompletedTasks}/{TotalTasks}, SoulMode:{IsInSoulMode}, Energy:{SoulModeEnergy:F1}";
        }

        private void LogTaskCompletion(int taskId)
        {
            LogNetworkState($"Task {taskId} completed. Progress: {CompletedTasks}/{TotalTasks}");
        }

        private void LogSoulModeChange(bool enabled)
        {
            LogNetworkState($"Soul mode {(enabled ? "enabled" : "disabled")}. Energy: {SoulModeEnergy:F1}");
        }

        #endregion
    }
}
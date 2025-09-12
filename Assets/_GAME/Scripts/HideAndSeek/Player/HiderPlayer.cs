using System;
using System.Linq;
using _GAME.Scripts.HideAndSeek.SkillSystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.HideAndSeek.Player
{
    /// <summary>
    /// Hider player implementation with stealth and task completion abilities
    /// </summary>
    public class HiderPlayer : RolePlayer
    {
        [Header("Hider Settings")] [SerializeField]
        private int totalTasks = 5;

        [SerializeField] private Transform ghostCamera; // For soul mode
        [SerializeField] private InputActionReference teleportSkillRef;
        [SerializeField] private InputActionReference freezeSkillRef;
        [SerializeField] private InputActionReference shapeshiftSkillRef;
        [SerializeField] private InputActionReference toggleSoulModeRef;

        private InputAction teleportSkillAction;
        private InputAction freezeSkillAction;
        private InputAction shapeshiftSkillAction;
        private InputAction toggleSoulModeAction;

        // Network variables
        private NetworkVariable<int> networkCompletedTasks = new NetworkVariable<int>(0);
        private NetworkVariable<bool> networkInSoulMode = new NetworkVariable<bool>(false);

        // IHider implementation
        public int CompletedTasks => networkCompletedTasks.Value;
        public int TotalTasks => totalTasks;
        public bool IsInSoulMode => networkInSoulMode.Value;
        public override bool HasSkillsAvailable => Skills.Values.Any(s => s.CanUse);

        public static event Action<int, int> OnTaskProgressChanged;
        public static event Action<bool> OnSoulModeChanged;

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            networkCompletedTasks.OnValueChanged += OnTasksNetworkChanged;
            networkInSoulMode.OnValueChanged += OnSoulModeNetworkChanged;

            if (IsServer)
            {
                networkCompletedTasks.Value = 0;
                networkInSoulMode.Value = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            if (networkCompletedTasks != null)
                networkCompletedTasks.OnValueChanged -= OnTasksNetworkChanged;
            if (networkInSoulMode != null)
                networkInSoulMode.OnValueChanged -= OnSoulModeNetworkChanged;
        }

        #endregion

        #region Skill Initialization

        protected override void InitializeSkills()
        {
            if (Role != Role.Hider) return;

            // Add hider skills
            var freezeSkill = gameObject.AddComponent<FreezeSkill>();
            freezeSkill.Initialize(SkillType.FreezeSeeker, GameManager.GetSkillData(SkillType.FreezeSeeker));
            Skills[SkillType.FreezeSeeker] = freezeSkill;

            var teleportSkill = gameObject.AddComponent<TeleportSkill>();
            teleportSkill.Initialize(SkillType.Teleport, GameManager.GetSkillData(SkillType.Teleport));
            Skills[SkillType.Teleport] = teleportSkill;

            var shapeshiftSkill = gameObject.AddComponent<ShapeShiftSkill>();
            shapeshiftSkill.Initialize(SkillType.ShapeShift, GameManager.GetSkillData(SkillType.ShapeShift));
            Skills[SkillType.ShapeShift] = shapeshiftSkill;

            Debug.Log($"[HiderPlayer] Skills initialized: {Skills.Count}");
        }

        #endregion

        #region Input System

        protected override void HandleRegisterInput()
        {
            if (!IsOwner) return;

            // Skill inputs
            if (teleportSkillRef != null)
            {
                teleportSkillAction = teleportSkillRef.action;
                teleportSkillAction.performed += OnTeleportSkillPerformed;
                teleportSkillAction.Enable();
            }

            if (freezeSkillRef != null)
            {
                freezeSkillAction = freezeSkillRef.action;
                freezeSkillAction.performed += OnFreezeSkillPerformed;
                freezeSkillAction.Enable();
            }

            if (shapeshiftSkillRef != null)
            {
                shapeshiftSkillAction = shapeshiftSkillRef.action;
                shapeshiftSkillAction.performed += OnShapeshiftSkillPerformed;
                shapeshiftSkillAction.Enable();
            }

            if (toggleSoulModeRef != null)
            {
                toggleSoulModeAction = toggleSoulModeRef.action;
                toggleSoulModeAction.performed += OnToggleSoulModePerformed;
                toggleSoulModeAction.Enable();
            }
        }

        protected override void HandleUnRegisterInput()
        {
            if (!IsOwner) return;

            teleportSkillAction?.Disable();
            freezeSkillAction?.Disable();
            shapeshiftSkillAction?.Disable();
            toggleSoulModeAction?.Disable();
        }

        private void OnTeleportSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed) return;

            // Teleport to a safe location or specific point
            UseSkill(SkillType.Teleport, GetTeleportDestination());
        }

        private void OnFreezeSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed) return;

            UseSkill(SkillType.FreezeSeeker, transform.position);
        }

        private void OnShapeshiftSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed) return;

            UseSkill(SkillType.ShapeShift);
        }

        private void OnToggleSoulModePerformed(InputAction.CallbackContext context)
        {
            if (!context.performed) return;

            ToggleSoulMode();
        }

        private Vector3 GetTeleportDestination()
        {
            // Find a safe teleport location
            // This could be random, or based on specific logic
            return transform.position + transform.forward * 10f;
        }

        #endregion

        #region Task System

        public void CompleteTask(int taskId)
        {
            if (IsOwner)
            {
                CompleteTaskServerRpc(taskId);
            }
        }

        [ServerRpc]
        private void CompleteTaskServerRpc(int taskId)
        {
            networkCompletedTasks.Value++;
            GameManager.PlayerTaskCompletedServerRpc(ClientId, taskId);

            OnTaskCompletedClientRpc(taskId);
        }

        [ClientRpc]
        private void OnTaskCompletedClientRpc(int taskId)
        {
            OnTaskCompletedLocal(taskId);
        }

        private void OnTaskCompletedLocal(int taskId)
        {
            Debug.Log($"Task {taskId} completed by {playerName}");

            if (IsOwner)
            {
                // Update task UI
                // Play completion sound
                // Show feedback
            }
        }

        private void OnTasksNetworkChanged(int previousValue, int newValue)
        {
            OnTaskProgressChanged?.Invoke(newValue, totalTasks);

            if (IsOwner)
            {
                UpdateTaskUI(newValue, totalTasks);
            }
        }

        #endregion

        #region Soul Mode System

        public void ToggleSoulMode()
        {
            if (IsOwner)
            {
                ToggleSoulModeServerRpc();
            }
        }

        [ServerRpc]
        private void ToggleSoulModeServerRpc()
        {
            networkInSoulMode.Value = !networkInSoulMode.Value;
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

        private void ApplySoulMode(bool soulMode)
        {
            if (soulMode)
            {
                // Enable ghost camera for owner
                if (IsOwner && ghostCamera != null)
                {
                    ghostCamera.gameObject.SetActive(true);
                }

                // Make player invisible to seekers but visible to other hiders
                SetLayerRecursively(gameObject, LayerMask.NameToLayer("HiderSoul"));

                // Reduce collision detection
                var collider = GetComponent<Collider>();
                if (collider != null)
                {
                    collider.isTrigger = true;
                }
            }
            else
            {
                // Disable ghost camera for owner
                if (IsOwner && ghostCamera != null)
                {
                    ghostCamera.gameObject.SetActive(false);
                }

                // Return to normal layer
                SetLayerRecursively(gameObject, LayerMask.NameToLayer("Player"));

                // Restore collision detection
                var collider = GetComponent<Collider>();
                if (collider != null)
                {
                    collider.isTrigger = false;
                }
            }
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        #endregion


        #region UI Updates

        protected virtual void UpdateTaskUI(int completedTasks, int totalTasks)
        {
            // Update task progress UI cho owner only
            if (IsOwner)
            {
                // HiderUI.Instance.UpdateTaskProgress(completedTasks, totalTasks);
                Debug.Log($"[HiderPlayer] Task Progress: {completedTasks}/{totalTasks}");
            }
        }

        protected virtual void UpdateSoulModeUI(bool soulMode)
        {
            // Update soul mode indicator cho owner only
            if (IsOwner)
            {
                // HiderUI.Instance.UpdateSoulModeIndicator(soulMode);
                Debug.Log($"[HiderPlayer] Soul Mode: {(soulMode ? "ON" : "OFF")}");
            }
        }

        protected override void UpdateHealthUI(float currentHealth, float maxHealth)
        {
            // Hiders thường không có health bar, hoặc có thể ẩn
            if (IsOwner)
            {
                // HiderUI.Instance.UpdateHealthBar(currentHealth / maxHealth);
            }
        }

        #endregion

        #region UI Helper Methods

        private void ShowInteractionPrompt(string message)
        {
            // HiderUI.Instance.ShowInteractionPrompt(message);
            Debug.Log($"[HiderPlayer] Interaction: {message}");
        }

        private void HideInteractionPrompt()
        {
            // HiderUI.Instance.HideInteractionPrompt();
        }

        private void ShowTaskErrorMessage(string message)
        {
            // HiderUI.Instance.ShowErrorMessage(message);
            Debug.LogWarning($"[HiderPlayer] Task Error: {message}");
        }

        private void ShowDisguiseErrorMessage(string message)
        {
            // HiderUI.Instance.ShowErrorMessage(message);
            Debug.LogWarning($"[HiderPlayer] Disguise Error: {message}");
        }

        #endregion

        #region Skill System Override

        protected override void OnSkillUsedLocal(SkillType skillType)
        {
            base.OnSkillUsedLocal(skillType);

            if (IsOwner)
            {
                switch (skillType)
                {
                    case SkillType.Teleport:
                        ShowSkillFeedback("Teleported!");
                        break;
                    case SkillType.FreezeSeeker:
                        ShowSkillFeedback("Seeker Frozen!");
                        break;
                    case SkillType.ShapeShift:
                        ShowSkillFeedback("Shape Shifted!");
                        break;
                }
            }
        }

        private void ShowSkillFeedback(string message)
        {
            // HiderUI.Instance.ShowSkillFeedback(message);
            Debug.Log($"[HiderPlayer] Skill: {message}");
        }

        #endregion

        #region Game Mode Specific Logic

        private void InitializePersonVsPerson()
        {
            totalTasks = GameManager.Settings.tasksToComplete;

            if (IsOwner)
            {
                // Enable task UI
                // HiderUI.Instance.ShowTaskProgress(true);
                // HiderUI.Instance.UpdateTaskProgress(0, totalTasks);

                Debug.Log($"[HiderPlayer] Person vs Person mode - Need to complete {totalTasks} tasks");
            }
        }

        private void InitializePersonVsObject()
        {
            if (IsOwner)
            {
                // Enable disguise UI
                // HiderUI.Instance.ShowDisguiseIndicator(true);

                Debug.Log("[HiderPlayer] Person vs Object mode - Find disguises to hide");
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
            return GameManager.CurrentMode switch
            {
                GameMode.PersonVsPerson => HasCompletedAllTasks(),
                GameMode.PersonVsObject => true // Survive until timer runs out
                ,
                _ => false
            };
        }

        #endregion

        #region Override Game Events with Complete Implementation

        public override void OnGameStart()
        {
            Debug.Log($"[HiderPlayer] Game started for {PlayerName}");

            if (IsOwner)
            {
                // Initialize based on game mode
                switch (GameManager.CurrentMode)
                {
                    case GameMode.PersonVsPerson:
                        InitializePersonVsPerson();
                        break;
                    case GameMode.PersonVsObject:
                        InitializePersonVsObject();
                        break;
                }

                // Enable hider-specific UI
                // HiderUI.Instance.ShowHiderUI(true);
                // HiderUI.Instance.UpdateSkillCooldowns(Skills);

                // Setup input prompts
                ShowSkillHints();
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
                // HiderUI.Instance.ShowHiderUI(false);

                // Disable controls
                HandleUnRegisterInput();

                // Show end game UI
                ShowEndGameResults(winnerRole);
            }
        }

        private void ShowSkillHints()
        {
            if (IsOwner)
            {
                // HiderUI.Instance.ShowSkillHints(
                //     "Q - Teleport | R - Freeze Seeker | T - Shape Shift | F - Soul Mode"
                // );
                Debug.Log("[HiderPlayer] Skills: Q-Teleport, R-Freeze, T-Shapeshift, F-SoulMode");
            }
        }

        private void ShowEndGameResults(Role winner)
        {
            string resultMessage = winner == Role.Hider ? "HIDERS WIN!" : "SEEKERS WIN!";

            // HiderUI.Instance.ShowGameResult(resultMessage, winner == PlayerRole.Hider);
            Debug.Log($"[HiderPlayer] {resultMessage}");
        }

        #endregion
    }
}
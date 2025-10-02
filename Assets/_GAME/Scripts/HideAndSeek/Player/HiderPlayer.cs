using System;
using System.Collections.Generic;
using System.Linq;
using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.Object;
using _GAME.Scripts.HideAndSeek.SkillSystem;
using _GAME.Scripts.Utils;
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
        [Header("Hider Settings")] [SerializeField]
        private SoulModeController soulModeCtrl;

        [Header("Input References")] [SerializeField]
        private InputActionReference toggleSoulModeRef;

        // Input Actions
        private InputAction toggleSoulModeAction;

        #region IGamePlayer Implementation

        public override bool HasSkillsAvailable => Skills.Values.Any(s => s.CanUse) && IsAlive;

        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            LogNetworkState("HiderPlayer spawned");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            LogNetworkState("HiderPlayer despawned");
        }

        #endregion

        #region Skill Initialization

        protected override void InitializeSkills()
        {
            if (Role != Role.Hider) return;

            // try
            // {
            //     // Initialize hider skills with proper validation
            //     var freezeSkill = gameObject.GetComponent<FreezeSkill>() ?? gameObject.AddComponent<FreezeSkill>();
            //     freezeSkill.Initialize(SkillType.FreezeSeeker, GameManager.GetSkillData(SkillType.FreezeSeeker));
            //     Skills[SkillType.FreezeSeeker] = freezeSkill;
            //
            //     var teleportSkill = gameObject.GetComponent<TeleportSkill>() ?? gameObject.AddComponent<TeleportSkill>();
            //     teleportSkill.Initialize(SkillType.Teleport, GameManager.GetSkillData(SkillType.Teleport));
            //     Skills[SkillType.Teleport] = teleportSkill;
            //     Debug.Log($"[HiderPlayer] Skills initialized: {Skills.Count}");
            // }
            // catch (System.Exception ex)
            // {
            //     Debug.LogError($"[HiderPlayer] Failed to initialize skills: {ex.Message}");
            // }
        }

        public override void UseSkill(SkillType skillType, Vector3? targetPosition = null)
        {
            //Todo: Implement skill use with validation
        }

        public override void ApplyPenaltyForKillingBot() { }

    #endregion

        #region Input System

        protected override void HandleRegisterInput()
        {
            if (!IsOwner) return;

            try
            {
                // Soul mode toggle
                if (toggleSoulModeRef?.action != null)
                {
                    toggleSoulModeAction = InputActionFactory.CreateUniqueAction(toggleSoulModeRef, GetInstanceID());
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
                //Clear soul mode input
                if (toggleSoulModeAction != null)
                {
                    toggleSoulModeAction.performed -= OnToggleSoulModePerformed;
                    toggleSoulModeAction.Disable();
                    toggleSoulModeAction = null;
                }

                Debug.Log("[HiderPlayer] Input unregistered successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[HiderPlayer] Failed to unregister input: {ex.Message}");
            }
        }

        #region Input Callbacks

        private void OnToggleSoulModePerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || !IsAlive) return;
            ToggleSoulMode();
        }

        #endregion

        #endregion

        #region Task System (Server Authoritative)

        public void CompleteTask(int taskId)
        {
            if (!IsOwner) return;

            GameEvent.OnTaskCompletion(this.OwnerClientId, taskId);
        }

        #endregion

        #region Soul Mode System (Server Authoritative)

        private void ToggleSoulMode()
        {
            if (!IsOwner) return;
            
            // Client-side validation
            if (!soulModeCtrl.CanToggleSoulMode())
            {
                ShowSoulModeErrorMessage("Cannot toggle soul mode");
                return;
            }
            soulModeCtrl.ToggleSoulMode();
        }
        
        #endregion


        #region Implemented Methods

        public override void OnDeath(IAttackable killer)
        {
            //ActiveCage(true);
            base.OnDeath(killer);
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

        protected override void UpdateHealthUI(float newValue, float maxValue)
        {
            if (!IsOwner) return;
            
            // Hiders may have reduced health UI
            Debug.Log($"[HiderPlayer] Health: {newValue}/{maxValue}");
            // HiderUI.Instance?.UpdateHealthBar(currentHealth / maxHealth);
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
        
        #endregion

        #region Game Mode Initialization

        private void InitializePersonVsPerson()
        {
           
        }

        private void InitializePersonVsObject()
        {
            
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
                if (soulModeCtrl.IsInSoulMode)
                {
                    soulModeCtrl.Clear();
                }

                // Disable UI
                // HiderUI.Instance?.ShowHiderUI(false);

                // Disable controls
                HandleUnRegisterInput();

                // Show end game UI
                ShowEndGameResults(winnerRole);
                CancelInvoke();
            }
            
        }

        #endregion


        #region Cage

        [SerializeField] private Cage cage;
        
        private void ActiveCage(bool active = true)
        {
            cage.SetActive(active);
        }

        #endregion
        
        
    }
}
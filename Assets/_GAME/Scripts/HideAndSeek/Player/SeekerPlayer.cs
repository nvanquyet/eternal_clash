using System;
using System.Collections.Generic;
using System.Linq;
using _GAME.Scripts.HideAndSeek.SkillSystem;
using _GAME.Scripts.Utils;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.HideAndSeek.Player
{
    /// <summary>
    /// Seeker player implementation with detection and pursuit abilities
    /// Network synchronized with proper validation
    /// </summary>
    public class SeekerPlayer : RolePlayer
    {
        [SerializeField] private int maxMissfireAllowed = 3;
        [Header("Input References")]
        [SerializeField] private InputActionReference detectSkillRef;
        [SerializeField] private InputActionReference freezeSkillRef;
        [SerializeField] private InputActionReference rushSkillRef;

        // Input Actions
        private InputAction detectSkillAction;
        private InputAction freezeSkillAction;
        private InputAction rushSkillAction;
        
        #region IGamePlayer Implementation
        public override bool HasSkillsAvailable => Skills.Values.Any(s => s.CanUse) && IsAlive;
        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Subscribe to network variable changes
          

            // Initialize server-side values
            if (IsServer)
            {
                // Initialize seeker health from game settings
                var seekerHealth = GameManager?.Settings?.seekerHealth ?? 100f;
                networkCurrentHealth.Value = seekerHealth;
            }

            LogNetworkState("SeekerPlayer spawned");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            LogNetworkState("SeekerPlayer despawned");
        }

        #endregion

        #region Skill Initialization

        protected override void InitializeSkills()
        {
            if (Role != Role.Seeker) return;

            // try
            // {
            //     // Initialize seeker skills with proper validation
            //     var detectSkill = gameObject.GetComponent<DetectSkill>() ?? gameObject.AddComponent<DetectSkill>();
            //     detectSkill.Initialize(SkillType.Detect, GameManager.GetSkillData(SkillType.Detect));
            //     Skills[SkillType.Detect] = detectSkill;
            //
            //     var freezeSkill = gameObject.GetComponent<FreezeSkill>() ?? gameObject.AddComponent<FreezeSkill>();
            //     freezeSkill.Initialize(SkillType.FreezeHider, GameManager.GetSkillData(SkillType.FreezeHider));
            //     Skills[SkillType.FreezeHider] = freezeSkill;
            //
            //     // Optional rush skill
            //     var skill = GameManager.GetSkillData(SkillType.Rush);
            //     if (skill.type != SkillType.None) // giả sử bạn có enum None = 0
            //     {
            //         var rushSkill = gameObject.GetComponent<RushSkill>() ?? gameObject.AddComponent<RushSkill>();
            //         rushSkill.Initialize(SkillType.Rush, GameManager.GetSkillData(SkillType.Rush));
            //         Skills[SkillType.Rush] = rushSkill;
            //     }
            //
            //     Debug.Log($"[SeekerPlayer] Skills initialized: {Skills.Count}");
            // }
            // catch (System.Exception ex)
            // {
            //     Debug.LogError($"[SeekerPlayer] Failed to initialize skills: {ex.Message}");
            // }
        }

        public override void UseSkill(SkillType skillType, Vector3? targetPosition = null)
        {
            //Todo: Call skill use with validation
        }

        public override void ApplyPenaltyForKillingBot()
        {
            if (IsAlive)
            {
                TakeDamage(null, MaxHealth * 1.0f / maxMissfireAllowed);
            }
        }

        #endregion

        #region Input System

        protected override void HandleRegisterInput()
        {
            if (!IsOwner) return;

            try
            {
                // Detect skill
                if (detectSkillRef?.action != null)
                {
                    detectSkillAction = detectSkillRef.action;
                    detectSkillAction.performed += OnDetectSkillPerformed;
                    detectSkillAction.Enable();
                }

                // Freeze skill
                if (freezeSkillRef?.action != null)
                {
                    freezeSkillAction = freezeSkillRef.action;
                    freezeSkillAction.performed += OnFreezeSkillPerformed;
                    freezeSkillAction.Enable();
                }

                // Rush skill
                if (rushSkillRef?.action != null)
                {
                    rushSkillAction = rushSkillRef.action;
                    rushSkillAction.performed += OnRushSkillPerformed;
                    rushSkillAction.Enable();
                }

                Debug.Log("[SeekerPlayer] Input registered successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SeekerPlayer] Failed to register input: {ex.Message}");
            }
        }

        protected override void HandleUnRegisterInput()
        {
            if (!IsOwner) return;

            try
            {
                detectSkillAction?.Disable();
                freezeSkillAction?.Disable();
                rushSkillAction?.Disable();

                Debug.Log("[SeekerPlayer] Input unregistered successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SeekerPlayer] Failed to unregister input: {ex.Message}");
            }
        }

        #region Input Callbacks

        private void OnDetectSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || !IsAlive) return;
            UseSkill(SkillType.Detect, transform.position);
        }

        private void OnFreezeSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || !IsAlive) return;
            UseSkill(SkillType.FreezeHider, transform.position);
        }

        private void OnRushSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || !IsAlive) return;
            
            if (Skills.ContainsKey(SkillType.Rush))
            {
                UseSkill(SkillType.Rush, transform.position);
            }
        }
        #endregion

        #endregion
        

        #region UI Updates

       
        protected override void UpdateHealthUI(float newValue, float maxValue)
        {
            if (!IsOwner) return;
            
            Debug.Log($"[SeekerPlayer] Health: {newValue}/{maxValue}");
        }
        
        #endregion

        #region UI Feedback Methods

        private void ShowSkillHints()
        {
            Debug.Log("[SeekerPlayer] Skills: Q-Detect, R-Freeze, E-Rush");
            // SeekerUI.Instance?.ShowSkillHints("Q - Detection | R - Freeze Hider | E - Rush");
        }

        private void ShowEndGameResults(Role winner)
        {
            string resultMessage = winner == Role.Seeker ? "SEEKERS WIN!" : "HIDERS WIN!";
            Debug.Log($"[SeekerPlayer] {resultMessage}");
            // SeekerUI.Instance?.ShowGameResult(resultMessage, winner == Role.Seeker);
        }

        #endregion

        #region Visual Effects

        private void PlayCatchEffect(Vector3 position)
        {
            Debug.Log($"[SeekerPlayer] Playing catch effect at {position}");
            // EffectManager.Instance?.PlayCatchEffect(position);
        }

        private void PlayFreezeEffect(Vector3 position)
        {
            Debug.Log($"[SeekerPlayer] Playing freeze effect at {position}");
            // EffectManager.Instance?.PlayFreezeEffect(position);
        }

        private void ApplyRushVisualEffects(bool isRushing)
        {
            if (isRushing)
            {
                Debug.Log("[SeekerPlayer] Applying rush visual effects");
                // EffectManager.Instance?.ApplyRushEffect(gameObject);
            }
            else
            {
                Debug.Log("[SeekerPlayer] Removing rush visual effects");
                // EffectManager.Instance?.RemoveRushEffect(gameObject);
            }
        }

        #endregion

        #region Game Events Override

        public override void OnGameStart()
        {
            Debug.Log($"[SeekerPlayer] Game started for {PlayerName}");

            if (IsOwner)
            {
                // Enable seeker-specific UI
                // SeekerUI.Instance?.ShowSeekerUI(true);
                // SeekerUI.Instance?.UpdateSkillCooldowns(Skills);

                // Show skill hints
                ShowSkillHints();
            }
        }

        public override void OnGameEnd(Role winnerRole)
        {
            Debug.Log($"[SeekerPlayer] Game ended, winner: {winnerRole}");

            if (IsOwner)
            {
                // Disable UI
                // SeekerUI.Instance?.ShowSeekerUI(false);

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
            }
        }

        #endregion
    }
}
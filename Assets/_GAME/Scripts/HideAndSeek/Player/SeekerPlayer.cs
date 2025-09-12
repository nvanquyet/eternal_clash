using System;
using System.Linq;
using _GAME.Scripts.HideAndSeek.SkillSystem;
using _GAME.Scripts.Utils;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.HideAndSeek.Player
{
    public class SeekerPlayer : RolePlayer
    {
        [SerializeField] private InputActionReference detectSkillRef;
        [SerializeField] private InputActionReference freezeSkillRef;

        private InputAction detectSkillAction;
        private InputAction freezeSkillAction;

        public override bool HasSkillsAvailable => Skills.Values.Any(s => s.CanUse);


        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                // Initialize seeker health
                networkCurrentHealth.Value = GameManager.Instance.Settings.seekerHealth;
            }
        }

        #endregion


        #region Skill Initialization

        protected override void InitializeSkills()
        {
            if (Role != Role.Seeker) return;

            // Add seeker skills
            var detectSkill = gameObject.AddComponent<DetectSkill>();
            detectSkill.Initialize(SkillType.Detect, GameManager.GetSkillData(SkillType.Detect));
            Skills[SkillType.Detect] = detectSkill;

            var freezeSkill = gameObject.AddComponent<FreezeSkill>();
            freezeSkill.Initialize(SkillType.FreezeHider, GameManager.GetSkillData(SkillType.FreezeHider));
            Skills[SkillType.FreezeHider] = freezeSkill;

            Debug.Log($"[SeekerPlayer] Skills initialized: {Skills.Count}");
        }

        #endregion

        

        #region Input System

        protected override void HandleRegisterInput()
        {
            if (!IsOwner) return;

            // Skill inputs
            if (detectSkillRef != null)
            {
                detectSkillAction = detectSkillRef.action;
                detectSkillAction.performed += OnDetectSkillPerformed;
                detectSkillAction.Enable();
            }

            if (freezeSkillRef != null)
            {
                freezeSkillAction = freezeSkillRef.action;
                freezeSkillAction.performed += OnFreezeSkillPerformed;
                freezeSkillAction.Enable();
            }
        }

        protected override void HandleUnRegisterInput()
        {
            if (!IsOwner) return;

            detectSkillAction?.Disable();
            freezeSkillAction?.Disable();
        }


        private void OnDetectSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed) return;

            UseSkill(SkillType.Detect);
        }

        private void OnFreezeSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed) return;

            // Use at current position or target position
            UseSkill(SkillType.FreezeHider, transform.position);
        }

        #endregion

        #region Game Events

        public override void OnGameStart()
        {
            Debug.Log($"[SeekerPlayer] Game started for {PlayerName}");

            if (IsOwner)
            {
                // Enable seeker-specific UI
                // Initialize targeting systems
            }
        }

        public override void OnGameEnd(Role winnerRole)
        {
            Debug.Log($"[SeekerPlayer] Game ended, winner: {winnerRole}");

            if (IsOwner)
            {
                // Handle game end UI
                // Disable controls
            }
        }

        #endregion

        #region UI Updates

        protected override void UpdateHealthUI(float currentHealth, float maxHealth)
        {
            // Update seeker health bar
            // SeekerUI.UpdateHealthBar(currentHealth / maxHealth);
        }

        #endregion
    }
}
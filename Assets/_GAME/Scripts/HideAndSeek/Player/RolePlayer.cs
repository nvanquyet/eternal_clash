using System;
using System.Collections.Generic;
using _GAME.Scripts.DesignPattern.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player
{
    public abstract class RolePlayer : ACombatEntity, IGamePlayer
    {
        [Header("Player Settings")] [SerializeField]
        protected string playerName = "Player";

        [SerializeField] protected PlayerRole role; // ✅ This is now LOCAL only

        [Header("Network")] [SerializeField]
        protected NetworkVariable<bool> networkIsAlive = new NetworkVariable<bool>(true);

        protected GameManager GameManager => GameManager.Instance;
        protected readonly Dictionary<SkillType, ISkill> Skills = new Dictionary<SkillType, ISkill>();

        // IGamePlayer implementation
        public ulong ClientId => NetworkObject.OwnerClientId;
        public PlayerRole Role => role; // ✅ Returns local role value
        public string PlayerName => playerName;
        public override bool IsAlive => networkIsAlive.Value;

        public static event Action<ulong, PlayerRole> OnPlayerRoleChanged;

        #region Abstract Methods

        protected abstract void HandleInput();
        protected abstract void InitializeSkills();
        public abstract void OnGameStart();
        public abstract void OnGameEnd(PlayerRole winnerRole);

        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            networkIsAlive.OnValueChanged += OnAliveStatusChanged;

            if (IsOwner)
            {
                GameManager?.RegisterPlayer(this);
            }

            // ✅ REMOVED: Don't initialize skills here anymore
            // Skills will be initialized when role is actually set
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            networkIsAlive.OnValueChanged -= OnAliveStatusChanged;
        }

        #endregion

        #region Role Management - FIXED

        /// <summary>
        /// Set role locally - called by PlayerController
        /// This should ONLY be called by PlayerController.ApplyRoleLocally()
        /// </summary>
        public virtual void SetRole(PlayerRole newRole)
        {
            var previousRole = role;
            role = newRole;

            Debug.Log($"[RolePlayer] {gameObject.name} role set: {previousRole} -> {newRole}");

            // Trigger event
            OnPlayerRoleChanged?.Invoke(ClientId, newRole);
        }

        /// <summary>
        /// Called when role is first assigned to this player
        /// </summary>
        public virtual void OnRoleAssigned()
        {
            Debug.Log($"[RolePlayer] {gameObject.name} role assigned: {role}");

            // Initialize skills for this role
            InitializeSkills();

            // Additional role-specific initialization
            OnRoleInitialized();
        }

        /// <summary>
        /// Called when role is removed from this player
        /// </summary>
        public virtual void OnRoleRemoved()
        {
            Debug.Log($"[RolePlayer] {gameObject.name} role removed: {role}");

            // Cleanup skills
            CleanupSkills();

            // Role-specific cleanup
            OnRoleCleanup();
        }

        /// <summary>
        /// Override this for role-specific initialization
        /// </summary>
        protected virtual void OnRoleInitialized()
        {
        }

        /// <summary>
        /// Override this for role-specific cleanup
        /// </summary>
        protected virtual void OnRoleCleanup()
        {
        }

        /// <summary>
        /// Cleanup all skills when role changes
        /// </summary>
        private void CleanupSkills()
        {
            foreach (var skill in Skills.Values)
            {
                if (skill is Component skillComponent)
                {
                    Destroy(skillComponent);
                }
            }

            Skills.Clear();
        }

        #endregion

        #region Network RPCs

        [ServerRpc]
        protected void SetAliveStatusServerRpc(bool isAlive)
        {
            networkIsAlive.Value = isAlive;
        }

        #endregion

        #region Event Handlers

        private void OnAliveStatusChanged(bool previousValue, bool newValue)
        {
            if (!newValue)
            {
                OnDeath();
            }
        }

        #endregion

        #region Interaction System Integration

        public override bool Interact(IInteractable target)
        {
            return true;
        }

        public override void OnInteracted(IInteractable initiator)
        {
            // Handle being interacted with
        }

        protected override void OnStateChanged(InteractionState previousState, InteractionState newState)
        {
            if (newState == InteractionState.Dead && IsAlive)
            {
                SetAliveStatusServerRpc(false);
            }
        }

        #endregion
    }
}
using System;
using System.Collections.Generic;
using _GAME.Scripts.HideAndSeek.Player.HitBox;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player
{
    /// <summary>
    /// Base class for all player roles with proper network synchronization
    /// </summary>
    public abstract class RolePlayer : ModularRootHitBox, IGamePlayer
    {
        [Header("Player Settings")] [SerializeField]
        protected string playerName = "Player";

        // Network Variables - Synchronized across all clients
        private NetworkVariable<Role> networkRole = new NetworkVariable<Role>(HideAndSeek.Role.None);

        // Local only - Role assignment happens through PlayerController
        private Role localRole = HideAndSeek.Role.None;

        protected readonly Dictionary<SkillType, ISkill> Skills = new Dictionary<SkillType, ISkill>();
        protected GameManager GameManager => GameManager.Instance;
        
        public Action OnNetworkSpawned;

        #region IGamePlayer Implementation

        public ulong ClientId => NetworkObject.OwnerClientId;
        public Role Role => networkRole.Value; 
        public string PlayerName => playerName;
        public abstract bool HasSkillsAvailable { get; }

        #endregion
        

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Subscribe to network variable changes
            networkRole.OnValueChanged += OnRoleNetworkChanged;

            if (OnNetworkSpawned != null)
            {
                OnNetworkSpawned?.Invoke();
                OnNetworkSpawned = null; // Clear after invoking
            }
            
            // Register input handling for owner
            if (IsOwner)
            {
                HandleRegisterInput();
                GameManager?.RegisterPlayer(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            // Cleanup subscriptions
            if (networkRole != null)
                networkRole.OnValueChanged -= OnRoleNetworkChanged;

            // Unregister input
            if (IsOwner)
            {
                HandleUnRegisterInput();
            }

            // Cleanup skills
            CleanupSkills();
        }

        #endregion

        #region Role Management

        public void SetRole(Role role)
        {
            AssignRoleServerRpc(role);
        }
        
        /// <summary>
        /// Server-side method to assign role to player
        /// Called by GameManager or PlayerController
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void AssignRoleServerRpc(Role newRole)
        {
            if (networkRole.Value == newRole) return;

            var previousRole = networkRole.Value;
            networkRole.Value = newRole;

            Debug.Log($"[RolePlayer] {gameObject.name} role assigned: {previousRole} -> {newRole}");
        }
        

        private void OnRoleNetworkChanged(Role previousRole, Role newRole)
        {
            // Initialize new role
            if (newRole == Role.None) return;
            localRole = newRole;
            OnRoleAssigned(newRole);
        }
        

        protected virtual void OnRoleAssigned(Role role)
        {
            Debug.Log($"[RolePlayer] {gameObject.name} role assigned: {role}");

            // Initialize skills for this role
            InitializeSkills();

            // Role-specific initialization
            OnRoleInitialized();
        }

        #endregion

        #region Skill System

        public virtual void UseSkill(SkillType skillType, Vector3? targetPosition = null)
        {
            if (!IsOwner) return;

            if (!Skills.ContainsKey(skillType) || !Skills[skillType].CanUse)
            {
                Debug.LogWarning($"Skill {skillType} not available or on cooldown");
                return;
            }

            UseSkillServerRpc(skillType, targetPosition ?? Vector3.zero, targetPosition.HasValue);
        }

        [ServerRpc]
        protected virtual void UseSkillServerRpc(SkillType skillType, Vector3 targetPosition, bool hasTarget)
        {
            if (!Skills.ContainsKey(skillType) || !Skills[skillType].CanUse) return;

            Vector3? target = hasTarget ? targetPosition : null;
            Skills[skillType].UseSkill(this, target);

            // Notify clients about skill usage
            OnSkillUsedClientRpc(skillType);
        }

        [ClientRpc]
        protected virtual void OnSkillUsedClientRpc(SkillType skillType)
        {
            OnSkillUsedLocal(skillType);
        }

        protected virtual void OnSkillUsedLocal(SkillType skillType)
        {
            // Override for local effects (UI, sounds, etc.)
        }

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

        #region Input System

        protected abstract void HandleRegisterInput();
        protected abstract void HandleUnRegisterInput();
        
        #endregion

        #region Game Events

        public abstract void OnGameStart();
        public abstract void OnGameEnd(Role winnerRole);

        #endregion

        #region Abstract Methods

        protected abstract void InitializeSkills();

        protected virtual void OnRoleInitialized()
        {
            
        }

        #endregion

        #region Health System Override

        protected override void OnHealthChangedLocal(float previousHealth, float newHealth)
        {
            if (IsOwner)
            {
                // Update health UI only for owner
                UpdateHealthUI(newHealth, MaxHealth);
            }
        }

        protected virtual void UpdateHealthUI(float currentHealth, float maxHealth)
        {
            // Override in specific player types
        }
        #endregion
    }
}
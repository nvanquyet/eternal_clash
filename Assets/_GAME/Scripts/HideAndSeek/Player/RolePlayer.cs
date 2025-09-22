using System;
using System.Collections.Generic;
using _GAME.Scripts.DesignPattern.Interaction;
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
        [Header("Player Settings")] 
        [SerializeField] protected string playerName = "Player";

        // Network Variables - Synchronized across all clients
        protected NetworkVariable<Role> networkRole = new NetworkVariable<Role>(
            Role.None, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );

        protected NetworkVariable<bool> networkIsAlive = new NetworkVariable<bool>(
            true, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );

        // Skill cooldown tracking (server authoritative)
        protected NetworkVariable<float> networkLastSkillTime = new NetworkVariable<float>(
            0f, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );

        // Local skill management
        protected readonly Dictionary<SkillType, ISkill> Skills = new Dictionary<SkillType, ISkill>();
        protected readonly Dictionary<SkillType, float> skillCooldowns = new Dictionary<SkillType, float>();

        protected GameManager GameManager => GameManager.Instance;
        
        public Action OnNetworkSpawned;
        public Action<Role> OnRoleChanged;

        #region IGamePlayer Implementation

        public ulong ClientId => NetworkObject.OwnerClientId;
        public Role Role => networkRole.Value; 
        public string PlayerName => playerName;
        public bool IsAlive => networkIsAlive.Value;
        public abstract bool HasSkillsAvailable { get; }

        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Subscribe to network variable changes
            networkRole.OnValueChanged += OnRoleNetworkChanged;
            networkIsAlive.OnValueChanged += OnAliveStateNetworkChanged;
            networkLastSkillTime.OnValueChanged += OnSkillTimeNetworkChanged;

            // Initialize server-side values
            if (IsServer)
            {
                networkIsAlive.Value = true;
            }

            // Invoke spawn callback
            OnNetworkSpawned?.Invoke();
            OnNetworkSpawned = null; // Clear after invoking
            
            // Register input and player for owner only
            if (IsOwner)
            {
                HandleRegisterInput();
                GameManager?.RegisterPlayer(this);
            }

            Debug.Log($"[RolePlayer] {gameObject.name} spawned. Owner: {IsOwner}, Server: {IsServer}");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            // Cleanup subscriptions
            if (networkRole != null)
                networkRole.OnValueChanged -= OnRoleNetworkChanged;
            if (networkIsAlive != null)
                networkIsAlive.OnValueChanged -= OnAliveStateNetworkChanged;
            if (networkLastSkillTime != null)
                networkLastSkillTime.OnValueChanged -= OnSkillTimeNetworkChanged;

            // Unregister input for owner
            if (IsOwner)
            {
                HandleUnRegisterInput();
                GameManager?.RemovePlayer(this);
            }

            // Cleanup skills
            CleanupSkills();

            Debug.Log($"[RolePlayer] {gameObject.name} despawned");
        }

        #endregion

        #region Role Management (Server Authoritative)

        /// <summary>
        /// Client requests role assignment (called by GameManager)
        /// </summary>
        public void SetRole(Role role)
        {
            if (IsServer)
            {
                AssignRoleServer(role);
            }
            else
            {
                RequestRoleAssignmentServerRpc(role);
            }
        }
        
        [ServerRpc(RequireOwnership = false)]
        private void RequestRoleAssignmentServerRpc(Role newRole)
        {
            AssignRoleServer(newRole);
        }

        /// <summary>
        /// Server-side role assignment with validation
        /// </summary>
        private void AssignRoleServer(Role newRole)
        {
            if (!IsServer) return;

            // Validate role assignment
            if (!GameManager.CanAssignRole(ClientId, newRole))
            {
                Debug.LogWarning($"[RolePlayer] Cannot assign role {newRole} to client {ClientId}");
                return;
            }

            if (networkRole.Value == newRole) return;

            var previousRole = networkRole.Value;
            networkRole.Value = newRole;

            Debug.Log($"[RolePlayer] {gameObject.name} role assigned: {previousRole} -> {newRole} (Server)");
        }

        private void OnRoleNetworkChanged(Role previousRole, Role newRole)
        {
            Debug.Log($"[RolePlayer] {gameObject.name} role changed: {previousRole} -> {newRole} (Client)");

            // Role-specific initialization
            if (newRole != Role.None)
            {
                OnRoleAssigned(newRole);
            }

            // Notify listeners
            OnRoleChanged?.Invoke(newRole);
        }

        protected virtual void OnRoleAssigned(Role role)
        {
            // Initialize skills for this role
            //InitializeSkills();

            // Role-specific initialization
            OnRoleInitialized();
        }

        #endregion

        #region Skill System (Server Authoritative)

        public virtual void UseSkill(SkillType skillType, Vector3? targetPosition = null)
        {
            if (!IsOwner) return;

            // Client-side validation before sending to server
            if (!CanUseSkillLocally(skillType))
            {
                OnSkillUsageFailedLocal(skillType, "Skill not available or on cooldown");
                return;
            }

            // Send to server for authoritative execution
            UseSkillServerRpc(skillType, targetPosition ?? Vector3.zero, targetPosition.HasValue);
        }

        [ServerRpc]
        protected virtual void UseSkillServerRpc(SkillType skillType, Vector3 targetPosition, bool hasTarget)
        {
            if (!IsServer) return;

            // Server-side validation
            if (!CanUseSkillServer(skillType))
            {
                NotifySkillFailedClientRpc(skillType, "Server validation failed");
                return;
            }

            // Execute skill on server
            Vector3? target = hasTarget ? targetPosition : null;
            bool success = ExecuteSkillServer(skillType, target);

            if (success)
            {
                // Update cooldown tracking
                networkLastSkillTime.Value = Time.time;
                UpdateSkillCooldownServer(skillType);

                // Notify all clients about successful skill usage
                OnSkillUsedClientRpc(skillType, targetPosition, hasTarget);
            }
            else
            {
                NotifySkillFailedClientRpc(skillType, "Skill execution failed");
            }
        }

        [ClientRpc]
        protected virtual void OnSkillUsedClientRpc(SkillType skillType, Vector3 targetPosition, bool hasTarget)
        {
            Vector3? target = hasTarget ? targetPosition : null;
            OnSkillExecutedLocal(skillType, target);
        }

        [ClientRpc]
        protected virtual void NotifySkillFailedClientRpc(SkillType skillType, string reason)
        {
            if (IsOwner)
            {
                OnSkillUsageFailedLocal(skillType, reason);
            }
        }

        #region Skill Helper Methods

        private bool CanUseSkillLocally(SkillType skillType)
        {
            if (!Skills.ContainsKey(skillType)) return false;
            if (!Skills[skillType].CanUse) return false;
            if (!networkIsAlive.Value) return false;
            
            return true;
        }

        private bool CanUseSkillServer(SkillType skillType)
        {
            if (!IsServer) return false;
            if (!Skills.ContainsKey(skillType)) return false;
            if (!Skills[skillType].CanUse) return false;
            if (!networkIsAlive.Value) return false;

            // Server-side cooldown check
            if (skillCooldowns.ContainsKey(skillType))
            {
                float timeSinceLastUse = Time.time - skillCooldowns[skillType];
                float requiredCooldown = Skills[skillType].GetCooldownTime();
                if (timeSinceLastUse < requiredCooldown) return false;
            }

            return true;
        }

        private bool ExecuteSkillServer(SkillType skillType, Vector3? target)
        {
            if (!Skills.ContainsKey(skillType)) return false;

            try
            {
                Skills[skillType].UseSkill(this, target);
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RolePlayer] Skill execution error: {ex.Message}");
                return false;
            }
        }

        private void UpdateSkillCooldownServer(SkillType skillType)
        {
            if (!IsServer) return;
            skillCooldowns[skillType] = Time.time;
        }

        protected virtual void OnSkillExecutedLocal(SkillType skillType, Vector3? target)
        {
            // Override in derived classes for local effects (UI, sounds, etc.)
            Debug.Log($"[RolePlayer] Skill executed locally: {skillType}");
        }

        protected virtual void OnSkillUsageFailedLocal(SkillType skillType, string reason)
        {
            Debug.LogWarning($"[RolePlayer] Skill usage failed: {skillType} - {reason}");
            // Show error message to owner
        }

        private void OnSkillTimeNetworkChanged(float previousTime, float newTime)
        {
            // Update UI cooldowns for owner
            if (IsOwner)
            {
                UpdateSkillCooldownUI();
            }
        }

        protected virtual void UpdateSkillCooldownUI()
        {
            // Override in derived classes
        }

        #endregion

        private void CleanupSkills()
        {
            foreach (var skill in Skills.Values)
            {
                if (skill is Component skillComponent)
                {
                    if (skillComponent != null)
                        Destroy(skillComponent);
                }
            }

            Skills.Clear();
            skillCooldowns.Clear();
        }

        #endregion

        #region Player State Management (Server Authoritative)

        public void SetAliveState(bool isAlive)
        {
            if (IsServer)
            {
                SetAliveStateServer(isAlive);
            }
            else
            {
                SetAliveStateServerRpc(isAlive);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetAliveStateServerRpc(bool isAlive)
        {
            SetAliveStateServer(isAlive);
        }

        private void SetAliveStateServer(bool isAlive)
        {
            if (!IsServer) return;
            if (networkIsAlive.Value == isAlive) return;

            networkIsAlive.Value = isAlive;
            Debug.Log($"[RolePlayer] {gameObject.name} alive state: {isAlive}");
        }

        private void OnAliveStateNetworkChanged(bool previousState, bool newState)
        {
            OnAliveStateChanged(newState);
        }

        protected virtual void OnAliveStateChanged(bool isAlive)
        {
            if (IsOwner)
            {
                UpdateAliveStateUI(isAlive);
            }
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
            // Override in derived classes
        }

        public override void OnDeath(IAttackable killer)
        {
            if (IsServer)
            {
                //Check if bullet get bullet's owner and get killer from owner id and notify to clients
            }
            base.OnDeath(killer);
        }

        #endregion

        #region Health System Override

        protected override void OnHealthChangedLocal(float previousHealth, float newHealth)
        {
            // Sync health changes to server if owner
            if (IsOwner && !Mathf.Approximately(previousHealth, newHealth))
            {
                SyncHealthToServerRpc(newHealth);
            }

            if (IsOwner)
            {
                UpdateHealthUI(newHealth, MaxHealth);
            }

            // Check for death
            if (newHealth <= 0 && networkIsAlive.Value)
            {
                SetAliveState(false);
            }
        }

        [ServerRpc]
        private void SyncHealthToServerRpc(float newHealth)
        {
            if (IsServer)
            {
                // Server validates and updates
                var clampedHealth = Mathf.Clamp(newHealth, 0f, MaxHealth);
                if (Math.Abs(CurrentHealth - clampedHealth) > 0.01f)
                {
                    // Update health on server and sync to all clients
                    SyncHealthToClientsClientRpc(clampedHealth);
                }
            }
        }

        [ClientRpc]
        private void SyncHealthToClientsClientRpc(float newHealth)
        {
            if (!IsOwner) // Don't update owner's health as it's the source
            {
                //networkCurrentHealth.Value = newHealth;
            }
        }
        

        protected virtual void UpdateHealthUI(float currentHealth, float maxHealth)
        {
            // Override in derived classes
        }

        protected virtual void UpdateAliveStateUI(bool isAlive)
        {
            // Override in derived classes
        }

        #endregion

        #region Utility Methods

        protected void LogNetworkState(string message)
        {
            Debug.Log($"[RolePlayer] {gameObject.name} - Owner:{IsOwner} Server:{IsServer} - {message}");
        }

        #endregion
    }
}
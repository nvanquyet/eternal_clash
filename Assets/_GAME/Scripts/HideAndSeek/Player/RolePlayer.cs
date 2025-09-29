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

        // Network Variables - Synchronized across all clients (server-write)
        protected NetworkVariable<Role> networkRole = new NetworkVariable<Role>(
            Role.None,
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
        public bool IsAlive => CurrentHealth > 0;
        public abstract bool HasSkillsAvailable { get; }

        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Subscribe to network variable changes
            networkRole.OnValueChanged += OnRoleNetworkChanged;

            // Invoke spawn callback
            OnNetworkSpawned?.Invoke();
            OnNetworkSpawned = null; // Clear after invoking
            
            // ✅ Đăng ký player với GameManager TRÊN SERVER
            if (IsServer)
            {
                GameManager?.RegisterPlayer(this);
            }

            // Đăng ký input CHỈ phía owner
            if (IsOwner)
            {
                HandleRegisterInput();
            }

            Debug.Log($"[RolePlayer] {gameObject.name} spawned. Owner: {IsOwner}, Server: {IsServer}");
        }

        public override void OnNetworkDespawn()
        {
            // Cleanup subscriptions
            if (networkRole != null)
                networkRole.OnValueChanged -= OnRoleNetworkChanged;

            // Hủy đăng ký input phía owner
            if (IsOwner)
            {
                HandleUnRegisterInput();
            }

            // Cleanup skills
            CleanupSkills();

            Debug.Log($"[RolePlayer] {gameObject.name} despawned");
            base.OnNetworkDespawn();
        }

        #endregion

        #region Role Management (Server Authoritative)

        /// <summary>
        /// Public API: Yêu cầu set role (GM gọi trực tiếp trên server; client gọi sẽ đi qua RPC)
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
        private void RequestRoleAssignmentServerRpc(Role newRole, ServerRpcParams rpc = default)
        {
            // Anti-spoof (nếu cần): client tự xin đổi role → check điều kiện cho client này
            if (!IsServer) return;

            // Gọi AssignRoleServer luôn, nhưng dùng GameManager.CanAssignRole để lọc
            AssignRoleServer(newRole);
        }

        /// <summary>
        /// Server-side role assignment with validation
        /// </summary>
        private void AssignRoleServer(Role newRole)
        {
            if (!IsServer) return;

            // Validate role assignment qua GameManager
            if (GameManager != null && !GameManager.CanAssignRole(ClientId, newRole))
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
            // Nếu cần, bật lại InitializeSkills()
            // InitializeSkills();

            // Role-specific initialization
            OnRoleInitialized();
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
        public abstract void UseSkill(SkillType skillType, Vector3? targetPosition = null);

        protected virtual void OnRoleInitialized()
        {
            // Override in derived classes
        }

        /// <summary>
        /// Server nhận thông tin killer từ hệ thống combat (Projectile/Melee),
        /// phát sự kiện để GameManager xử lý (đồng bộ với OnPlayerDeathRequested đã subscribe).
        /// </summary>
        public override void OnDeath(IAttackable killer)
        {
            if (IsServer)
            {
                ulong killerId = ResolveKillerClientId(killer);
                // Thông báo cho GameManager qua event chung
                GameEvent.OnPlayerKilled?.Invoke(killerId, ClientId);
            }
            //Call base to handle local death logic (e.g., UI update)
            base.OnDeath(killer);
        }

        private ulong ResolveKillerClientId(IAttackable killer)
        {
            if (killer == null) return 0UL;

            // Ưu tiên projectile có trường Owner
            if (killer is MonoBehaviour mb)
            {
                // Nếu là projectile custom có property Owner (vd AProjectile)
                // dùng reflection nhẹ để không bị phụ thuộc kiểu:
                try
                {
                    var t = killer.GetType();
                    var ownerProp = t.GetProperty("Owner");
                    if (ownerProp != null && ownerProp.PropertyType == typeof(ulong))
                    {
                        var val = ownerProp.GetValue(killer);
                        if (val is ulong ownerId) return ownerId;
                    }
                }
                catch { /* ignore */ }

                // fallback: lấy OwnerClientId của NetworkObject
                if (mb.TryGetComponent<NetworkObject>(out var aNob))
                {
                    return aNob.OwnerClientId;
                }
            }
            return 0UL;
        }

        #endregion

        #region Health System Override

        protected override void OnHealthChangedLocal(float previousHealth, float newHealth)
        {
            // ⚠️ Không gọi OnDeath từ client – server sẽ RPC OnDeathClientRpc rồi
            if (IsOwner)
            {
                UpdateHealthUI(newHealth, MaxHealth);
            }
        }

        protected virtual void UpdateHealthUI(float newValue, float maxValue)
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

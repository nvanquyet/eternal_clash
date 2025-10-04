using _GAME.Scripts.Core.Components;
using _GAME.Scripts.DesignPattern.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Core.Combat
{
   
    /// <summary>
    /// Component that enables player to deal damage
    /// Replaces AAttackable inheritance
    /// </summary>
    public class CombatComponent : NetworkBehaviour, IPlayerComponent
    {
        [Header("Combat Settings")]
        [SerializeField] private float baseDamage = 10f;
        [SerializeField] private float attackCooldown = 1f;
        [SerializeField] private DamageType damageType = DamageType.Physical;

        private IPlayer _owner;
        private NetworkVariable<double> _nextAttackTime = new(0d);
        private CombatSystem _combatSystem;

        public bool IsActive => enabled;
        public bool CanAttack => NetworkManager.Singleton.ServerTime.Time >= _nextAttackTime.Value;

        public void Initialize(IPlayer owner)
        {
            _owner = owner;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _combatSystem = FindObjectOfType<CombatSystem>();
        }

        public void OnNetworkDespawn()
        {
        }

        /// <summary>
        /// Client-initiated attack request
        /// </summary>
        public void Attack(ulong targetId)
        {
            if (!_owner.NetObject.IsOwner) return;
            if (!CanAttack) return;

            RequestAttackServerRpc(targetId);
        }

        [ServerRpc]
        private void RequestAttackServerRpc(ulong targetId, ServerRpcParams rpc = default)
        {
            if (!CanAttack) return;

            // Validate and apply damage
            if (_combatSystem.ApplyDamage(_owner.ClientId, targetId, baseDamage, damageType))
            {
                _nextAttackTime.Value = NetworkManager.Singleton.ServerTime.Time + attackCooldown;
                OnAttackSuccessClientRpc(targetId, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { rpc.Receive.SenderClientId } }
                });
            }
        }

        [ClientRpc]
        private void OnAttackSuccessClientRpc(ulong targetId, ClientRpcParams rpc = default)
        {
            // Play attack animation/effects
            Debug.Log($"[CombatComponent] Attack success on {targetId}");
        }
    }
}
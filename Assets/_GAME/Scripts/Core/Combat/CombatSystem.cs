using System;
using System.Collections.Generic;
using _GAME.Scripts.Core.Player;
using _GAME.Scripts.Core.Services;
using _GAME.Scripts.DesignPattern.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Core.Combat
{
    /// <summary>
    /// Centralized combat system using strategy pattern
    /// Replaces scattered damage calculations
    /// </summary>
    public class CombatSystem : NetworkBehaviour
    {
        private readonly Dictionary<DamageType, IDamageStrategy> _damageStrategies = new();
        private IPlayerRegistry _playerRegistry;

        private void Awake()
        {
            InitializeStrategies();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _playerRegistry = GameServices.Get<IPlayerRegistry>();
        }

        private void InitializeStrategies()
        {
            _damageStrategies[DamageType.Physical] = new PhysicalDamageStrategy();
            _damageStrategies[DamageType.True] = new TrueDamageStrategy();
            _damageStrategies[DamageType.Magical] = new MagicDamageStrategy();
        }

        /// <summary>
        /// Server-only: Apply damage from attacker to target
        /// </summary>
        public bool ApplyDamage(ulong attackerId, ulong targetId, float baseDamage, DamageType damageType)
        {
            if (!IsServer) return false;

            var target = _playerRegistry?.GetPlayer(targetId);
            if (target == null || !target.IsAlive()) return false;

            // Calculate final damage
            var strategy = _damageStrategies[damageType];
            var defense = 0f;
            var finalDamage = strategy.Calculate(baseDamage, defense);

            // Apply damage
            var actualDamage = target.Health.TakeDamage(finalDamage);

            // Broadcast damage event
            BroadcastDamageClientRpc(attackerId, targetId, actualDamage, damageType);

            // Check if target died
            if (!target.IsAlive())
            {
                HandleDeath(attackerId, targetId);
            }

            return actualDamage > 0f;
        }

        private void HandleDeath(ulong killerId, ulong victimId)
        {
            if (!IsServer) return;

            Debug.Log($"[CombatSystem] Player {victimId} killed by {killerId}");

            GameEventBus.Publish(new PlayerKilledEvent
            {
                KillerId = killerId,
                VictimId = victimId,
                Timestamp = Time.time
            });
        }

        [ClientRpc]
        private void BroadcastDamageClientRpc(ulong attackerId, ulong targetId, float damage, DamageType type)
        {
            // Client-side visual feedback
            Debug.Log($"[CombatSystem] {attackerId} dealt {damage} {type} damage to {targetId}");
        }

        /// <summary>
        /// Calculate damage without applying it (for UI predictions)
        /// </summary>
        public float PredictDamage(float baseDamage, DamageType damageType, float defense)
        {
            if (!_damageStrategies.TryGetValue(damageType, out var strategy))
                return 0f;

            return strategy.Calculate(baseDamage, defense);
        }
    }

}
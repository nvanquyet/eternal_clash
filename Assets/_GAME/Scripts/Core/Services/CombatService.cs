using _GAME.Scripts.Core.Player;
using _GAME.Scripts.DesignPattern.Interaction;
using UnityEngine;

namespace _GAME.Scripts.Core.Services
{
    public class CombatService : ICombatService
    {
        private readonly IPlayerRegistry _playerRegistry;

        public CombatService(IPlayerRegistry playerRegistry)
        {
            _playerRegistry = playerRegistry;
        }

        public float CalculateDamage(float baseDamage, DamageType damageType, float defense)
        {
            if (damageType == DamageType.True)
                return baseDamage;

            return Mathf.Max(1f, baseDamage - defense);
        }

        public void ApplyDamage(ulong attackerId, ulong targetId, float damage)
        {
            var target = _playerRegistry.GetPlayer(targetId);
            if (target == null || !target.IsAlive()) return;

            float actualDamage = target.Health.TakeDamage(damage);

            if (!target.IsAlive())
            {
                RegisterKill(attackerId, targetId);
            }
        }

        public void RegisterKill(ulong killerId, ulong victimId)
        {
            GameEventBus.Publish(new PlayerKilledEvent
            {
                KillerId = killerId,
                VictimId = victimId,
                Timestamp = Time.time
            });
        }
    }
}
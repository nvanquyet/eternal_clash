using System;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Core.Components
{
    [Serializable]
    public struct HealthState : INetworkSerializable
    {
        public float current;
        public float max;
        public bool isDead;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref current);
            serializer.SerializeValue(ref max);
            serializer.SerializeValue(ref isDead);
        }
    }

    public class HealthComponent : NetworkBehaviour, IPlayerComponent
    {
        private IPlayer _owner;
        private NetworkVariable<HealthState> _health;

        public float CurrentHealth => _health.Value.current;
        public float MaxHealth => _health.Value.max;
        public bool IsAlive => !_health.Value.isDead;
        public bool IsActive => enabled;

        public event Action<float, float> OnHealthChanged;
        public event Action OnDeath;

        public void Initialize(IPlayer owner)
        {
            _owner = owner;
            _health = new NetworkVariable<HealthState>(
                new HealthState { current = 100f, max = 100f, isDead = false },
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _health.OnValueChanged += HandleHealthChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (_health != null)
                _health.OnValueChanged -= HandleHealthChanged;
            base.OnNetworkDespawn();
        }

        public void SetMaxHealth(float max)
        {
            if (!IsServer) return;
            var state = _health.Value;
            state.max = Mathf.Max(1f, max);
            state.current = Mathf.Min(state.current, state.max);
            _health.Value = state;
        }

        public float TakeDamage(float amount)
        {
            if (!IsServer || _health.Value.isDead) return 0f;

            var state = _health.Value;
            var actualDamage = Mathf.Min(amount, state.current);
            state.current = Mathf.Max(0f, state.current - actualDamage);

            if (state.current <= 0f)
            {
                state.isDead = true;
                _health.Value = state;
                OnDeath?.Invoke();
            }
            else
            {
                _health.Value = state;
            }

            return actualDamage;
        }

        public float Heal(float amount)
        {
            if (!IsServer || _health.Value.isDead) return 0f;

            var state = _health.Value;
            var actualHeal = Mathf.Min(amount, state.max - state.current);
            state.current = Mathf.Min(state.max, state.current + actualHeal);
            _health.Value = state;

            return actualHeal;
        }

        public void Revive(float health = -1f)
        {
            if (!IsServer) return;

            var state = _health.Value;
            state.isDead = false;
            state.current = health > 0 ? Mathf.Min(health, state.max) : state.max;
            _health.Value = state;
        }

        private void HandleHealthChanged(HealthState oldState, HealthState newState)
        {
            OnHealthChanged?.Invoke(newState.current, newState.max);
        }
    }

}
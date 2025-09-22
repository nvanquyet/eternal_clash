using System;
using System.Linq;
using _GAME.Scripts.HideAndSeek.Config;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.SkillSystem
{
     public abstract class BaseSkill : NetworkBehaviour, ISkill
    {
        [Header("Skill Base Settings")]
        [SerializeField] protected SkillType skillType;
        [SerializeField] protected float cooldown;
        [SerializeField] protected int usesPerGame;
        [SerializeField] protected float duration;
        [SerializeField] protected float range;
        
        protected int remainingUses;
        protected float nextUseTime;
        protected bool isActive;
        
        // Network variables
        protected NetworkVariable<int> networkRemainingUses = new NetworkVariable<int>();
        protected NetworkVariable<float> networkNextUseTime = new NetworkVariable<float>();
        
        // ISkill implementation
        public SkillType Type => skillType;
        public float Cooldown => cooldown;
        public int UsesPerGame => usesPerGame;
        public int RemainingUses => networkRemainingUses.Value;
        public bool CanUse => networkRemainingUses.Value > 0 && Time.time >= networkNextUseTime.Value && !isActive;
        
        public static event Action<SkillType, IGamePlayer, bool> OnSkillUsed;
        public static event Action<SkillType, float> OnSkillCooldownStarted;
        
        public virtual void Initialize(SkillType type, SkillData data)
        {
            skillType = type;
            cooldown = data.cooldown;
            usesPerGame = data.usesPerGame;
            duration = data.duration;
            range = data.range;
            
            remainingUses = usesPerGame;
            
            if (IsServer)
            {
                networkRemainingUses.Value = usesPerGame;
                networkNextUseTime.Value = 0f;
            }
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            networkRemainingUses.OnValueChanged += OnRemainingUsesChanged;
            networkNextUseTime.OnValueChanged += OnNextUseTimeChanged;
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            networkRemainingUses.OnValueChanged -= OnRemainingUsesChanged;
            networkNextUseTime.OnValueChanged -= OnNextUseTimeChanged;
        }
        
        public virtual void UseSkill(IGamePlayer caster, Vector3? targetPosition = null)
        {
            if (!CanUse) return;
            
            if (IsServer)
            {
                // Consume use
                networkRemainingUses.Value--;
                StartCooldown();
                
                // Execute skill effect
                ExecuteSkillEffect(caster, targetPosition);
                
                // Notify clients
                OnSkillUsedClientRpc(caster.ClientId, targetPosition ?? Vector3.zero, targetPosition.HasValue);
            }
        }
        
        public virtual void StartCooldown()
        {
            if (IsServer)
            {
                networkNextUseTime.Value = Time.time + cooldown;
                OnSkillCooldownStarted?.Invoke(skillType, cooldown);
            }
        }

        public virtual float GetCooldownTime()
        {
            if (IsServer)
            {
                return Mathf.Max(0f, networkNextUseTime.Value - Time.time);
            }
            else
            {
                return Mathf.Max(0f, networkNextUseTime.Value - Time.time);
            }
        }

        public virtual float GetEffectDuration()
        {
            return duration;
        }

        protected abstract void ExecuteSkillEffect(IGamePlayer caster, Vector3? targetPosition);
        
        [ClientRpc]
        protected virtual void OnSkillUsedClientRpc(ulong casterId, Vector3 targetPosition, bool hasTarget)
        {
            var caster = FindObjectsOfType<MonoBehaviour>().OfType<IGamePlayer>().FirstOrDefault(p => p.ClientId == casterId);
            if (caster != null)
            {
                OnSkillUsed?.Invoke(skillType, caster, true);
                Vector3? target = hasTarget ? targetPosition : null;
                ExecuteSkillVFX(caster, target);
            }
        }
        
        protected virtual void ExecuteSkillVFX(IGamePlayer caster, Vector3? targetPosition)
        {
            // Override in derived classes for visual/audio effects
        }
        
        private void OnRemainingUsesChanged(int previousValue, int newValue)
        {
            remainingUses = newValue;
        }
        
        private void OnNextUseTimeChanged(float previousValue, float newValue)
        {
            nextUseTime = newValue;
        }
    }
}
using System;
using UnityEngine;

namespace _GAME.Scripts.DesignPattern.Interaction
{
    /// <summary>
    /// Base interface for all interactive entities in the game
    /// </summary>
    public interface IInteractable
    {
        string EntityId { get; }
        bool CanInteract { get; }
        bool IsActive { get; set; }
        Vector3 Position { get; }
        
        bool Interact(IInteractable target);
        void OnInteracted(IInteractable initiator);
    }

    /// <summary>
    /// Interface for entities that can take damage and defend
    /// </summary>
    public interface IDefendable : IInteractable
    {
        float CurrentHealth { get; }
        float MaxHealth { get; }
        float DefenseValue { get; }
        bool IsAlive { get; }
        bool IsInvulnerable { get; }
        
        float TakeDamage(IAttackable attacker, float damage, DamageType damageType = DamageType.Physical);
        float Heal(float amount);
        void OnDeath(IAttackable killer = null);
        
        event Action<float, float> OnHealthChanged; // (currentHealth, maxHealth)
        event Action<IDefendable, IAttackable> OnDied; // (defender, killer)
    }
    
    /// <summary>
    /// Interface for entities that can attack others
    /// </summary>
    public interface IAttackable : IInteractable
    {
        float BaseDamage { get; }
        float AttackRange { get; }
        float AttackCooldown { get; }
        bool CanAttack { get; }
        float NextAttackTime { get; }
        DamageType PrimaryDamageType { get; }
        
        bool Attack(IDefendable target);
        bool IsInAttackRange(IDefendable target);
        float CalculateDamage(IDefendable target);
        
        event Action<IAttackable, IDefendable, float> OnAttackPerformed; // (attacker, target, damage)
    }


}
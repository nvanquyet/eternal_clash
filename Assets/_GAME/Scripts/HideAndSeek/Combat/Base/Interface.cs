using System;
using System.Collections.Generic;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Combat.Base
{
    [System.Serializable]
    public class WeaponStats
    {
        public float damage = 10f;
        public float attackCooldown = 1f;
        public float range = 100f;
        public int criticalChance = 0; // 0-100%
        public float criticalMultiplier = 1.5f;
    }
    public interface IWeaponComponent
    {
        bool IsInitialized { get; }
        void Initialize();
        void Cleanup();
    }

    public interface IAmmoSystem : IWeaponComponent
    {
        int CurrentAmmo { get; }
        int MaxAmmo { get; }
        bool HasAmmo { get; }
        bool IsEmpty { get; }
        bool IsFull { get; }
        bool IsUnlimited { get; }
        
        bool TryConsumeAmmo();
        void RestoreAmmo(int amount);
        void RefillAmmo();
        void SetMaxAmmo(int maxAmmo);
        void SetUnlimitedAmmo(bool unlimited);
        
        event Action<int, int> OnAmmoChanged;
        event Action OnAmmoEmpty;
    }

    public interface IReloadSystem : IWeaponComponent
    {
        bool IsReloading { get; }
        float ReloadProgress { get; }
        float ReloadTime { get; }
        
        bool CanReload();
        void StartReload();
        void InterruptReload();
        
        event Action<bool> OnReloadStateChanged;
        event Action OnReloadStarted;
        event Action OnReloadCompleted;
    }

    public interface IAttackSystem : IWeaponComponent
    {
        float AttackRate { get; }
        float BaseDamage { get; }
        
        bool CanAttack { get; }
        void Attack(Vector3 direction, Vector3 origin);
        void SetAttackRate(float value);
        
        void SetDamage(float value);
    }

    public interface IWeaponEffects : IWeaponComponent
    {
        void PlayAttackEffects();
        void PlayEmptySound();
        void PlayAttackSound();
        void StopAllEffects();
    }

    public interface IWeaponInput : IWeaponComponent
    {
        void RegisterInput();
        void UnregisterInput();
        
        void EnableInput();
        void DisableInput();
        
        event Action OnAttackPerformed;
    }
    
}
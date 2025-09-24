using System;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Combat.Base
{
    public abstract class AttackComponent :  NetworkBehaviour, IAttackSystem
    {
        [Header("Attack Configuration")]
        [SerializeField] private float attackRate = 1f; // Attacks per second
        [SerializeField] private float damage; 
        public bool IsInitialized { get; private set; }
        public float AttackRate => attackRate;
        public float BaseDamage => damage;

        public Action OnPreFire = null;
        protected Action<Vector3, Vector3> OnWeaponAttacked = null;
        // Server writes; everyone reads
        protected readonly NetworkVariable<double> LastFireServerTime = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
       
        public virtual bool CanAttack => LocalCooldownReady; // note: this is server time replicated → an toàn cho client check “gần đúng”

        protected bool LocalCooldownReady =>
            NetworkManager != null &&
            NetworkManager.ServerTime.Time >= LastFireServerTime.Value + TimeBetweenShots;
        protected double TimeBetweenShots => 1.0 / attackRate; // in seconds (serverTime)
        public virtual void Initialize()
        {
            IsInitialized = true;
        }

        #region Unity Lifecycle

        protected void Awake()
        {
            Initialize();
        }

        #endregion
        
        
        public virtual void Cleanup() { IsInitialized = false; }

        public void SetAttackRate(float value) => this.attackRate = Mathf.Max(0,value);

        public void SetDamage(float value) =>  this.damage = Mathf.Max(1,value);

        public abstract void Attack(Vector3 direction, Vector3 origin);
    }
}
using System;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Combat.Base
{
    public abstract class AttackComponent : NetworkBehaviour, IAttackSystem
    {
        [Header("Attack Configuration")]
        [SerializeField] private float attackRate = 1f;
        [SerializeField] private float damage;

        // ✅ SYNC DAMAGE QUA NETWORK
        private readonly NetworkVariable<float> _networkDamage = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        protected readonly NetworkVariable<double> LastFireServerTime = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
        );

        private float? _pendingDamage = null;
        private bool _damageInitialized = false;

        public bool IsInitialized { get; private set; }
        public float AttackRate => attackRate;
    
        // ✅ ĐỌC TỪ NETWORK VARIABLE
        public float BaseDamage => _networkDamage.Value;

        public Action<bool> OnPreFire = null;   //Has enable aim camera
        public Action OnFire = null; 

        public virtual bool CanAttack => LocalCooldownReady;

        protected bool LocalCooldownReady =>
            NetworkManager != null &&
            NetworkManager.ServerTime.Time >= LastFireServerTime.Value + TimeBetweenShots;

        protected double TimeBetweenShots => 1.0 / attackRate;

        private void Awake()
        {
            Initialize();
        }

        public override void OnDestroy()
        {
            Cleanup();
            base.OnDestroy();
        }


        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
        
            // ✅ INIT DAMAGE ON SERVER
            if (IsServer && !_damageInitialized)
            {
                _networkDamage.Value = _pendingDamage ?? damage;
                _damageInitialized = true;
                _pendingDamage = null;
            }
        }

        public virtual void Initialize()
        {
            IsInitialized = true;
        }

        public virtual void Cleanup()
        {
            IsInitialized = false;
        }

        public void SetAttackRate(float value) => attackRate = Mathf.Max(0, value);

        // ✅ SET DAMAGE TRƯỚC SPAWN
        public void SetDamage(float value)
        {
            if (IsSpawned)
            {
                Debug.LogError("Cannot SetDamage after spawn! Use UpdateDamageServerRpc()");
                return;
            }
            _pendingDamage = value;
        }

        // ✅ UPDATE SAU SPAWN (SERVER RPC)
        [ServerRpc(RequireOwnership = false)]
        public void UpdateDamageServerRpc(float value)
        {
            _networkDamage.Value = Mathf.Max(1f, value);
        }

        public abstract void Attack(Vector3 direction, Vector3 origin);
    }
}
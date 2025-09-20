using System.Linq;
using _GAME.Scripts.DesignPattern.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Combat.Base
{
    public abstract class AProjectile : AAttackable
    {
        [Header("Projectile Settings")]
        [SerializeField] protected float speed = 20f;
        [SerializeField] protected float lifetime = 5f;
        [SerializeField] protected bool pierceTargets = false;
        [SerializeField] protected int maxPierceCount = 3;
        
        [Header("Physics")]
        [SerializeField] protected bool useGravity = false;
        [SerializeField] protected Rigidbody rb;

        protected Vector3 direction;
        protected NetworkVariable<double> networkSpawnTime = new NetworkVariable<double>(
            writePerm: NetworkVariableWritePermission.Server);
        protected int currentPierceCount;
        protected IAttackable owner;
        
        // Properties
        public float Speed => speed;
        public float Lifetime => lifetime;
        public Vector3 Direction => direction;
        public IAttackable Owner => owner;
        
        // Override CanAttack from new base system
        public override bool CanAttack => 
            base.CanAttack && 
            CurrentState != InteractionState.Disabled &&
            !hasCollisionAttacked;
        
        protected override void Awake()
        {
            base.Awake();
            
            if (rb == null)
                rb = GetComponent<Rigidbody>();
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            if (IsServer)
            {
                networkSpawnTime.Value = NetworkManager.Singleton.ServerTime.Time;
                currentPierceCount = 0;
                hasCollisionAttacked = false;
                SetState(InteractionState.Enable); // Changed from Attacking
            }
            
            // Setup physics
            if (rb != null)
            {
                rb.useGravity = useGravity;
                if (!useGravity)
                {
                    rb.linearVelocity = direction.normalized * speed;
                }
            }
            
            OnProjectileSpawned();
        }

        protected virtual void Update()
        {
            // Only server handles movement and lifetime
            if (!IsServer || !IsSpawned) return;
            
            // Move projectile (if not using physics)
            if (rb == null || rb.isKinematic)
            {
                transform.position += direction.normalized * speed * Time.deltaTime;
            }
            
            // Check lifetime using server time
            if (NetworkManager.Singleton.ServerTime.Time - networkSpawnTime.Value > lifetime)
            {
                OnLifetimeExpired();
            }
        }
        
        public virtual void Initialize(IAttackable shooter, Vector3 startPosition, Vector3 shootDirection)
        {
            owner = shooter;
            direction = shootDirection.normalized;
            transform.position = startPosition;
            transform.rotation = Quaternion.LookRotation(direction);
            
            // Copy properties from shooter
            if (shooter != null)
            {
                //baseDamage = shooter.BaseDamage;
                primaryDamageType = shooter.PrimaryDamageType;
            }
        }
        
        #region New Base System Overrides
        
        protected override void OnSuccessfulAttack(IDefendable target, float actualDamage)
        {
            OnHitTarget(target, actualDamage);
            
            if (!pierceTargets || currentPierceCount >= maxPierceCount)
            {
                DestroyProjectile();
            }
            else
            {
                currentPierceCount++;
                hasCollisionAttacked = false; // Allow next hit
            }
        }
        
        protected override void OnHitInvalidTarget(Collider other)
        {
            Debug.Log($"[AProjectile]: Hit invalid target {other.name}");
            OnHitObstacle(other);
            DestroyProjectile();
        }
        
        protected override void OnHitNonDefendableTarget(Collider other)
        {
            if (other) OnHitObstacle(other);
            DestroyProjectile();
        }
        
        protected override void HandleDestruction()
        {
            DestroyProjectile();
        }
        
        #endregion
        
        #region Collision and Interaction
        
        public override bool Interact(IInteractable target)
        {
            // Projectiles don't actively interact - they use collision system
            return false;
        }
        
        public override void OnInteracted(IInteractable initiator)
        {
            // Projectiles don't get interacted with
        }
        
        #endregion
        
        #region Lifecycle Management
        
        protected virtual void OnLifetimeExpired()
        {
            OnProjectileExpired();
            DestroyProjectile();
        }
        
        protected virtual void DestroyProjectile()
        {
            if (!IsServer) return;
            
            SetState(InteractionState.Disabled);
            OnProjectileDestroyed();
            
            // Despawn with destroy = true
            if (NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
        }
        
        #endregion
        
        #region Abstract Methods - Override in specific projectiles
        
        /// <summary>
        /// Called when projectile is spawned on network
        /// </summary>
        protected abstract void OnProjectileSpawned();
        
        /// <summary>
        /// Called when projectile hits a valid target
        /// </summary>
        protected abstract void OnHitTarget(IDefendable target, float damage);
        
        /// <summary>
        /// Called when projectile hits an obstacle or invalid target
        /// </summary>
        protected abstract void OnHitObstacle(Collider obstacle);
        
        /// <summary>
        /// Called when projectile lifetime expires
        /// </summary>
        protected abstract void OnProjectileExpired();
        
        /// <summary>
        /// Called before projectile is destroyed
        /// </summary>
        protected abstract void OnProjectileDestroyed();
        
        #endregion
    }
}
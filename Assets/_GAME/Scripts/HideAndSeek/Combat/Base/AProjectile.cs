using System;
using _GAME.Scripts.DesignPattern.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Combat.Base
{
    /// <summary>
    /// Projectile dựa trên AAttackable của base:
    /// - Server-authoritative: di chuyển & lifetime ở server
    /// - Collision gây damage qua AAttackable.Server_ProcessCollision (đã có sẵn)
    /// - Lắng nghe OnAttackPerformed để xử lý pierce/hit hooks
    /// </summary>
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

        // Server-only write
        protected NetworkVariable<double> networkSpawnTime =
            new NetworkVariable<double>(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        protected int currentPierceCount;

        // Expose
        public float Speed => speed;
        public float Lifetime => lifetime;
        public Vector3 Direction => direction;

        /// <summary>
        /// Projectile có thể gây sát thương nhiều lần (pierce),
        /// nên bỏ ràng buộc cooldown của base. Dùng state + hasCollisionAttacked để throttle frame hiện tại.
        /// </summary>
        public override bool CanAttack =>
            CanInteract &&
            CurrentState != InteractionState.Disabled &&
            !hasCollisionAttacked;

        protected override void Awake()
        {
            base.Awake();

            // Bật collision attack trong base; không tự destroy để tự xử lý pierce/obstacle
            enableCollisionAttack = true;
            destroyAfterCollisionAttack = false;

            if (rb == null) rb = GetComponent<Rigidbody>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Subscribe nhận callback khi HIT HỢP LỆ (do base gọi OnAttackPerformed sau khi TakeDamage thành công)
            OnAttackPerformed += HandleProjectileAttackPerformed;

            if (IsServer)
            {
                networkSpawnTime.Value = NetworkManager.Singleton.ServerTime.Time;
                currentPierceCount = 0;
                hasCollisionAttacked = false;
                SetState(InteractionState.Enable);
            }

            // Thiết lập physics ở cả client & server cho mượt
            if (rb != null)
            {
                rb.useGravity = useGravity;
                rb.linearVelocity = direction.normalized * speed;
                // rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                // rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            OnProjectileSpawned();
        }

        public override void OnNetworkDespawn()
        {
            OnAttackPerformed -= HandleProjectileAttackPerformed;
            base.OnNetworkDespawn();
        }

        protected virtual void Update()
        {
            // Server điều khiển lifetime & (nếu không dùng physics) thì di chuyển
            if (!IsServer || !IsSpawned) return;

            if (rb == null || rb.isKinematic)
            {
                transform.position += direction.normalized * speed * Time.deltaTime;
            }

            // Hết lifetime?
            if (NetworkManager.Singleton.ServerTime.Time - networkSpawnTime.Value > lifetime)
            {
                OnLifetimeExpired();
            }
        }

        /// <summary>
        /// Gọi TRÊN SERVER ngay khi spawn projectile.
        /// Nên SpawnWithOwnership(shooterId) để feedback chỉ về attacker đúng client.
        /// </summary>
        public virtual void Initialize(Vector3 startPosition, Vector3 shootDirection)
        { 
            direction = shootDirection.normalized;
            transform.SetPositionAndRotation(startPosition, Quaternion.LookRotation(direction));

            if (rb != null)
            {
                rb.useGravity = useGravity;
                rb.linearVelocity = direction * speed;
            }
        }

        #region Base overrides / Filters

        protected override void Server_ProcessCollision(Collider other)
        {
            // Ignore nếu va chạm với chính owner (shooter)
            if(IsHitOwner(other)) return;
            base.Server_ProcessCollision(other);
        }

        private bool IsHitOwner(Collider other)
        {
            if(other == null) return false;
            var netOtherObj = other.GetComponent<NetworkObject>();
            if (netOtherObj == null)
            {
                //Try get parent
                netOtherObj = other.GetComponentInParent<NetworkObject>();
                if (netOtherObj == null) return false;
            }
            if (netOtherObj.OwnerClientId == this.OwnerClientId)
            {
                return true;
            }

            return false;
        }
        
        
        // Khi base gặp vật cản/đối tượng không hợp lệ:
        protected override void OnHitInvalidTarget(Collider other)
        {
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
            // Base sẽ gọi khi destroyAfterCollisionAttack = true,
            // nhưng mình đã set false; giữ để an toàn nếu có chỗ khác gọi.
            DestroyProjectile();
        }

        #endregion

        #region OnAttackPerformed hook (valid hit)

        private void HandleProjectileAttackPerformed(IAttackable attacker, IDefendable target, float appliedDamage)
        {
            // Chỉ quan tâm event của chính projectile này
            if (!ReferenceEquals(attacker, this)) return;
            if (!IsServer) return;

            OnHitTarget(target, appliedDamage);

            if (!pierceTargets || currentPierceCount >= Mathf.Max(0, maxPierceCount))
            {
                DestroyProjectile();
            }
            else
            {
                currentPierceCount++;
                // Cho phép va chạm tiếp theo (base đã set hasCollisionAttacked = true trong frame hit)
                hasCollisionAttacked = false;
            }
        }

        #endregion

        #region Lifecycle

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

            if (NetworkObject && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
            else
                Destroy(gameObject);
        }

        #endregion

        #region Abstract hooks (triển khai ở projectile cụ thể)

        /// <summary> Gọi khi projectile spawn xong trên network (mọi phía). </summary>
        protected abstract void OnProjectileSpawned();

        /// <summary> Gọi khi trúng mục tiêu hợp lệ (sau khi server áp damage thành công). </summary>
        protected abstract void OnHitTarget(IDefendable target, float damage);

        /// <summary> Gọi khi trúng vật cản/đối tượng không hợp lệ. </summary>
        protected abstract void OnHitObstacle(Collider obstacle);

        /// <summary> Gọi khi hết lifetime. </summary>
        protected abstract void OnProjectileExpired();

        /// <summary> Gọi ngay trước khi bị hủy/Despawn. </summary>
        protected abstract void OnProjectileDestroyed();

        #endregion
    }
}

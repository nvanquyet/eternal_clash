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
        [SerializeField] private float timePlayEffectsOnDestroy = 0.3f;
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
            if (!IsServer)
            {
                Debug.LogWarning("[AProjectile] Initialize should only be called on server!");
                return;
            }
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
            Debug.Log($"[Projectile-Server_ProcessCollision] START - Collided with: {other.name}, Layer: {LayerMask.LayerToName(other.gameObject.layer)}, Tag: {other.tag}");
            // Ignore nếu va chạm với chính owner (shooter)
            if(IsHitOwner(other)) return;
            base.Server_ProcessCollision(other);
        }

        private bool IsHitOwner(Collider other)
        {
            if (other == null) return false;
    
            // ✅ Ưu tiên Rigidbody root
            GameObject go = other.attachedRigidbody 
                ? other.attachedRigidbody.gameObject 
                : other.gameObject;
            
            if (go == null) return false;
            var netOtherObj = go.GetComponent<NetworkObject>() 
                              ?? go.GetComponentInParent<NetworkObject>();
    
            if (netOtherObj == null) return false;
            
            if(netOtherObj.CompareTag("Bot")) return false;
            
            return netOtherObj.OwnerClientId == this.OwnerClientId;
        }


        protected override bool IsValidTargetGO(GameObject target)
        {
            if (target == null) return false;

            // Layer check
            int targetLayerMask = 1 << target.layer;
            if ((targetLayerMask & attackableLayers) == 0) return false;

            // Tag check
            if (attackableTags != null && attackableTags.Length > 0)
            {
                bool tagMatched = false;
                for (int i = 0; i < attackableTags.Length; i++)
                {
                    if (target.CompareTag(attackableTags[i]))
                    {
                        tagMatched = true;
                        break;
                    }
                }

                if (!tagMatched) return false;
            }

            // ✅ Self-attack prevention
            if (target.TryGetComponent<NetworkObject>(out var nob))
            {
                Debug.Log($"[AProjectile] Target NO found. Tag: {nob.tag} Compare bot {nob.CompareTag("Bot")}, OwnerClientId: {nob.OwnerClientId}, Self OwnerClientId: {OwnerClientId}");
                if(!nob.CompareTag("Bot") && nob.OwnerClientId == OwnerClientId) return false;
            }
            
            if (target.transform.root.TryGetComponent<IGamePlayer>(out var targetPlayer))
            {
                var owner = GameManager.Instance.GetPlayerRoleWithId(this.OwnerClientId);
                // Same role = friendly fire
                if (targetPlayer.Role == owner && 
                    targetPlayer.Role != Role.None)
                {
                    return false; // Block damage giữa cùng team
                }
            }
            return true;
        
        }

        // Khi base gặp vật cản/đối tượng không hợp lệ:
        // protected override void OnHitInvalidTarget(Collider other)
        // {
        //     OnHitObstacle(other);
        //     DestroyProjectile();
        // }

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
                SetState(InteractionState.Enable);
            }
        }

        #endregion

        #region Lifecycle

        protected virtual void OnLifetimeExpired()
        {
            OnProjectileExpired();
            DestroyProjectileImmediate();
        }

        protected virtual void DestroyProjectile()
        {
            Invoke(nameof(DestroyProjectileImmediate), timePlayEffectsOnDestroy);
        }
        
        protected virtual void DestroyProjectileImmediate()
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

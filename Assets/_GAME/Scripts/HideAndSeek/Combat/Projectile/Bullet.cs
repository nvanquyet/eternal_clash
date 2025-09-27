using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Combat.Projectile
{
    public class Bullet : AProjectile
    {
        [Header("Bullet Visual Effects")]
        [SerializeField] private ParticleSystem hitEffect;
        [SerializeField] private ParticleSystem trailEffect;
        [SerializeField] private GameObject impactDecal;

        [Header("Bullet Audio")]
        [SerializeField] private AudioClip hitSound;
        [SerializeField] private AudioClip flybySound;
        [SerializeField] private AudioSource audioSource;

        protected override void Awake()
        {
            base.Awake();
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
        }

        public override bool Interact(IInteractable target) => false;

        public override void OnInteracted(IInteractable initiator) { }
        protected override void OnProjectileSpawned()
        {
            // VFX bay
            if (trailEffect != null) trailEffect.Play();

            // SFX bay
            if (audioSource != null && flybySound != null)
            {
                audioSource.PlayOneShot(flybySound);
            }
        }

        protected override void OnHitTarget(IDefendable target, float damage)
        {
            // Log nhẹ (server)
            if (target is MonoBehaviour mb)
            {
                string nameOrId = (target is InteractableBase ib) ? ib.EntityId : mb.name;
                Debug.Log($"[Bullet] Hit target {nameOrId} for {damage} dmg");
            }

            // Gửi hiệu ứng va chạm cho tất cả client (host cũng nhận)
            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
            {
                // Dùng vị trí/rotation hiện tại của viên đạn (server là nguồn thật)
                OnHitTargetClientRpc(transform.position, transform.rotation);
            }
        }

        protected override void OnHitObstacle(Collider obstacle)
        {
            if (!obstacle) return;

            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
            {
                OnHitObstacleClientRpc(transform.position, transform.rotation, obstacle.name);
            }
        }

        protected override void OnProjectileExpired()
        {
            Debug.Log("[Bullet] Lifetime expired");
        }

        protected override void OnProjectileDestroyed()
        {
            // Ngừng trail. Nếu muốn để lại “smoke tail” thì có thể tách instance thay vì stop trực tiếp
            if (trailEffect != null) trailEffect.Stop();
        }

        #region Client RPC Effects

        [ClientRpc]
        private void OnHitTargetClientRpc(Vector3 hitPosition, Quaternion hitRotation)
        {
            PlayHitEffects(hitPosition, hitRotation, true);
        }

        [ClientRpc]
        private void OnHitObstacleClientRpc(Vector3 hitPosition, Quaternion hitRotation, string obstacleName)
        {
            PlayHitEffects(hitPosition, hitRotation, false);
        }

        private void PlayHitEffects(Vector3 position, Quaternion rotation, bool isTarget)
        {
            // Particle va chạm
            if (hitEffect != null)
            {
                var fx = Instantiate(hitEffect, position, rotation);
                fx.Play();
                Destroy(fx.gameObject, 3f);
            }

            // Decal chỉ với obstacle (không phải entity)
            if (!isTarget && impactDecal != null)
            {
                var decal = Instantiate(impactDecal, position, rotation);
                Destroy(decal, 10f);
            }

            // Âm thanh va chạm
            if (hitSound != null) PlayHitAudio(position);
        }

        private void PlayHitAudio(Vector3 position)
        {
            var go = new GameObject("BulletHitAudio");
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();

            // Copy cấu hình cơ bản
            if (audioSource != null)
            {
                src.volume = audioSource.volume;
                src.pitch = audioSource.pitch;
                src.spatialBlend = audioSource.spatialBlend;
                src.rolloffMode = audioSource.rolloffMode;
                src.maxDistance = audioSource.maxDistance;
                src.minDistance = audioSource.minDistance;
            }

            src.clip = hitSound;
            src.Play();

            float cleanup = hitSound ? Mathf.Max(0.1f, hitSound.length + 0.5f) : 2f;
            Destroy(go, cleanup);
        }

        #endregion
    }
}

using System;
using _GAME.Scripts.Controller;
using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Combat.Projectile
{
    public class Bullet : AProjectile
    {
        [Header("Bullet Visual Effects")]
        [SerializeField] private ParticleSystem mainEffect;
        [SerializeField] private ParticleSystem hitEffect;
        [SerializeField] private ParticleSystem trailEffect;

        [Header("Bullet Audio")]
        [SerializeField] private AudioClip hitSound;
        [SerializeField] private AudioClip flybySound;
        [SerializeField] private AudioSource audioSource;

#if UNITY_EDITOR
        private void OnValidate()
        {
            audioSource = GetComponent<AudioSource>();
        }
#endif
        
        
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
            if(mainEffect) mainEffect.gameObject.SetActive(true);
            // SFX bay
            PlayAudio(audioSource, flybySound);
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
            PlayHitEffects();
        }

        [ClientRpc]
        private void OnHitObstacleClientRpc(Vector3 hitPosition, Quaternion hitRotation, string obstacleName)
        {
            PlayHitEffects();
        }

        private void PlayHitEffects()
        {
            if(mainEffect) mainEffect.gameObject.SetActive(false);
            // Particle va chạm
            if (hitEffect != null)
            {
                if(hitEffect.gameObject.activeSelf) hitEffect.Play();
                else hitEffect.gameObject.SetActive(true);
            }

            // Âm thanh va chạm
            PlayHitAudio();
        }

        private void PlayHitAudio()
        {
            PlayAudio(audioSource, hitSound);
        }

        private void PlayAudio(AudioSource source, AudioClip clip)
        {
            //Call audio manager
            AudioManager.Instance.PlaySfx(source, clip);
        }
        #endregion
    }
}

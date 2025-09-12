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
            
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
        }
        
        protected override void OnProjectileSpawned()
        {
            // Start visual effects
            if (trailEffect != null)
            {
                trailEffect.Play();
            }
            
            // Play flyby sound
            if (audioSource != null && flybySound != null)
            {
                audioSource.PlayOneShot(flybySound);
            }
        }
        
        protected override void OnHitTarget(IDefendable target, float damage)
        {
            // FIX: Safe target name extraction
            string targetName = "Unknown";
            if (target is MonoBehaviour mb)
            {
                targetName = mb.name;
                // If target is InteractableBase, we can get EntityId
                if (target is InteractableBase interactable)
                {
                    targetName = interactable.EntityId;
                }
            }
            
            Debug.Log($"[Bullet] Hit target {targetName} for {damage} damage");
            
            // FIX: Proper server and spawn checks
            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
            {
                OnHitTargetClientRpc(transform.position, transform.rotation);
            }
        }
        
        protected override void OnHitObstacle(Collider obstacle)
        {
            if (obstacle == null) return;
            
            // FIX: Proper server and spawn checks
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
            // Stop any ongoing effects
            if (trailEffect != null)
            {
                trailEffect.Stop();
            }
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
            // Spawn hit particle effect
            if (hitEffect != null)
            {
                var effect = Instantiate(hitEffect, position, rotation);
                Destroy(effect.gameObject, 3f);
            }
            
            // Spawn impact decal for obstacles
            if (!isTarget && impactDecal != null)
            {
                var decal = Instantiate(impactDecal, position, rotation);
                Destroy(decal, 10f); // Clean up after 10 seconds
            }
            
            // FIX: Better audio handling
            if (hitSound != null)
            {
                PlayHitAudio(position);
            }
        }
        
        // FIX: Separate method for audio handling
        private void PlayHitAudio(Vector3 position)
        {
            // Create temporary audio source for hit sound
            GameObject tempAudio = new GameObject("BulletHitAudio");
            tempAudio.transform.position = position;
            AudioSource tempSource = tempAudio.AddComponent<AudioSource>();
            
            // Copy settings from original audio source
            if (audioSource != null)
            {
                tempSource.volume = audioSource.volume;
                tempSource.pitch = audioSource.pitch;
                tempSource.spatialBlend = audioSource.spatialBlend;
                tempSource.rolloffMode = audioSource.rolloffMode;
                tempSource.maxDistance = audioSource.maxDistance;
            }
            
            tempSource.clip = hitSound;
            tempSource.Play();
            
            // FIX: Safe cleanup time
            float cleanupTime = hitSound != null ? Mathf.Max(hitSound.length, 0.1f) + 0.5f : 2f;
            Destroy(tempAudio, cleanupTime);
        }
        
        #endregion
    }
}
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace _GAME.Scripts.HideAndSeek.Combat.Base
{
    public class EffectsComponent : NetworkBehaviour, IWeaponEffects
    {
        [Header("Main Effect")]
        [SerializeField] private ParticleSystem attackEffect;
   
        [Header("Fire Audio")] [SerializeField]
        private AudioSource attackAudioSource;

        [SerializeField] private AudioClip[] attackSounds;
        [SerializeField] private AudioClip emptySound;
        [SerializeField] private float attackVolume = 1f;
        
        public bool IsInitialized { get; private set; }

        public void Initialize()
        {
            IsInitialized = true;
        }

        public void Cleanup()
        {
            IsInitialized = false;
        }

        public void PlayAttackEffects()
        {   
            if (attackEffect != null)
            {
                attackEffect.Play();
            }
        }

        public void PlayEmptySound()
        {
            if (attackAudioSource != null && emptySound != null)
            {
                attackAudioSource.PlayOneShot(emptySound, attackVolume);
            }
        }

        public void PlayAttackSound()
        {
            if (attackAudioSource != null && attackSounds.Length > 0)
            {
                var clip = attackSounds[Random.Range(0, attackSounds.Length)];
                attackAudioSource.PlayOneShot(clip, attackVolume);
            }
        }

        public void StopAllEffects()
        {
            if (attackEffect != null)
            {
                attackEffect.Stop();
            }
        }
    }
}
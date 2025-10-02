using _GAME.Scripts.Controller;
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
        [SerializeField] private AudioClip reloadSounds;
        [SerializeField] private AudioClip emptySound;
        
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
                if(attackEffect.gameObject.activeSelf) attackEffect.Play();
                else attackEffect.gameObject.SetActive(true);
            }
        }

        public void PlayEmptySound()
        {
            if (attackAudioSource != null && emptySound != null)
            {
                AudioManager.Instance.PlaySfx(attackAudioSource, emptySound);
            }
        }

        public void PlayAttackSound()
        {
            if (attackAudioSource != null && attackSounds.Length > 0)
            {
                var clip = attackSounds[Random.Range(0, attackSounds.Length)];
                AudioManager.Instance.PlaySfx(attackAudioSource, clip);
            }
        }
        
        public void PlayReloadSound()
        {
            if (attackAudioSource != null && reloadSounds != null)
            {
                AudioManager.Instance.PlaySfx(attackAudioSource, reloadSounds);
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
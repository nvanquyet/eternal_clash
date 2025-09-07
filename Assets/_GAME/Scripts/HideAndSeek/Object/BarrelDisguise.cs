using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Object
{
    public class BarrelDisguise : BaseDisguiseObject
    {
        [Header("Barrel Specific")]
        [SerializeField] private ParticleSystem steamEffect;
        [SerializeField] private float steamIntensity = 10f;
        
        protected override void Awake()
        {
            objectType = ObjectType.Barrel;
            base.Awake();
        }
        
        protected override void CreateOccupationEffect()
        {
            base.CreateOccupationEffect();
            
            // Add steam effect
            if (steamEffect != null)
            {
                var emission = steamEffect.emission;
                emission.rateOverTime = steamIntensity;
                steamEffect.Play();
            }
        }
        
        protected override void RemoveOccupationEffect()
        {
            base.RemoveOccupationEffect();
            
            if (steamEffect != null)
            {
                steamEffect.Stop();
            }
        }
    }

}
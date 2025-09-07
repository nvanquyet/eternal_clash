using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Object
{
   
    public class ContainerDisguise : BaseDisguiseObject
    {
        [Header("Container Specific")]
        [SerializeField] private Animator lidAnimator;
        [SerializeField] private AudioSource containerSounds;
        
        protected override void Awake()
        {
            objectType = ObjectType.Container;
            base.Awake();
        }
        
        protected override void CreateOccupationEffect()
        {
            base.CreateOccupationEffect();
            
            // Close lid
            if (lidAnimator != null)
            {
                lidAnimator.SetBool("IsOccupied", true);
            }
            
            // Play closing sound
            if (containerSounds != null)
            {
                containerSounds.Play();
            }
        }
        
        protected override void RemoveOccupationEffect()
        {
            base.RemoveOccupationEffect();
            
            // Open lid
            if (lidAnimator != null)
            {
                lidAnimator.SetBool("IsOccupied", false);
            }
        }
    }

}
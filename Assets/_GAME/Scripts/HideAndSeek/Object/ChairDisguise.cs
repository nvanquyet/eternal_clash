using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Object
{
     
    public class ChairDisguise : BaseDisguiseObject
    {
        [Header("Chair Specific")]
        [SerializeField] private Transform seatPosition;
        [SerializeField] private float rockingAngle = 5f;
        
        protected override void Awake()
        {
            objectType = ObjectType.Chair;
            base.Awake();
        }
        
        protected override void CreateOccupationEffect()
        {
            base.CreateOccupationEffect();
            
            // Add slight rocking motion
            StartCoroutine(RockingMotion());
        }
        
        private System.Collections.IEnumerator RockingMotion()
        {
            Vector3 originalRotation = transform.eulerAngles;
            
            while (IsOccupied)
            {
                float rock = Mathf.Sin(Time.time * 0.5f) * rockingAngle;
                transform.rotation = Quaternion.Euler(originalRotation.x, originalRotation.y, rock);
                yield return null;
            }
            
            transform.rotation = Quaternion.Euler(originalRotation);
        }
    }

}
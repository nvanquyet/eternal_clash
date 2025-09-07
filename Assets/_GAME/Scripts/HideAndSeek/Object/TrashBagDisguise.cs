using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Object
{
    
    public class TrashBagDisguise : BaseDisguiseObject
    {
        [Header("Trash Bag Specific")]
        [SerializeField] private float wobbleIntensity = 0.1f;
        [SerializeField] private float wobbleSpeed = 2f;
        
        private Vector3 originalPosition;
        private bool isWobbling = false;
        
        protected override void Awake()
        {
            objectType = ObjectType.TrashBag;
            base.Awake();
            originalPosition = transform.position;
        }
        
        private void Update()
        {
            if (isWobbling && IsOccupied)
            {
                // Add subtle wobble effect
                Vector3 wobble = new Vector3(
                    Mathf.Sin(Time.time * wobbleSpeed) * wobbleIntensity,
                    0,
                    Mathf.Cos(Time.time * wobbleSpeed * 0.8f) * wobbleIntensity
                );
                transform.position = originalPosition + wobble;
            }
        }
        
        protected override void CreateOccupationEffect()
        {
            base.CreateOccupationEffect();
            isWobbling = true;
        }
        
        protected override void RemoveOccupationEffect()
        {
            base.RemoveOccupationEffect();
            isWobbling = false;
            transform.position = originalPosition;
        }
    }

}
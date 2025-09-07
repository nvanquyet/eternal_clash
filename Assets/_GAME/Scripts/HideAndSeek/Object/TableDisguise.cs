using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Object
{
   
    public class TableDisguise : BaseDisguiseObject
    {
        [Header("Table Specific")]
        [SerializeField] private Transform[] hidingSpots;
        [SerializeField] private GameObject tableclothEffect;
        
        protected override void Awake()
        {
            objectType = ObjectType.Table;
            base.Awake();
        }
        
        protected override void CreateOccupationEffect()
        {
            base.CreateOccupationEffect();
            
            // Add tablecloth movement effect
            if (tableclothEffect != null)
            {
                tableclothEffect.SetActive(true);
            }
        }
        
        protected override void RemoveOccupationEffect()
        {
            base.RemoveOccupationEffect();
            
            if (tableclothEffect != null)
            {
                tableclothEffect.SetActive(false);
            }
        }
    }

}
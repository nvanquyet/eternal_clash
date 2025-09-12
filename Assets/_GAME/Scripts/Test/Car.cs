using _GAME.Scripts.DesignPattern.Interaction;
using UnityEngine;

namespace _GAME.Scripts.Test
{
    public class Car : ADefendable
    {
        public override bool Interact(IInteractable target) => false;

        public override void OnInteracted(IInteractable initiator)
        {
            
        }

        protected override void OnStateChanged(InteractionState previousState, InteractionState newState)
        {
            
        }

        public override void OnDeath(IAttackable killer = null)
        {
            base.OnDeath(killer);
            //Hide this object
            Debug.Log("Car Defeated by " + killer);
            //Despawn this object
            gameObject.SetActive(false);
        }
    }
}

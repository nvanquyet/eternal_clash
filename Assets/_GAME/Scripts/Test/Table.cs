using System;
using _GAME.Scripts.DesignPattern.Interaction;
using UnityEngine;

namespace _GAME.Scripts.Test
{
    public class Table : APassiveInteractable
    {
        public override bool Interact(IInteractable target) => false;
        
        protected override void OnStateChanged(InteractionState previousState, InteractionState newState)
        {
        }

        protected override void PerformInteractionLogic(IInteractable initiator)
        {
            //Hide this object 
            Debug.Log("Table Interacted with by " + initiator);
            gameObject.SetActive(false);
        }
    }
}
using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using _GAME.Scripts.HideAndSeek.Player;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Combat.Gun
{
    public class GunPickup : APassiveInteractable
    {
        [SerializeField] private AGun gunReference;
        
        public override bool Interact(IInteractable target) => false;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if(gunReference) gunReference.OnDropWeapon += OnDropWeapon;
        }
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if(gunReference) gunReference.OnDropWeapon -= OnDropWeapon;
        }

        private void OnDropWeapon()
        {
            SetState(InteractionState.Enable);
        }

        protected override void OnStateChanged(InteractionState previousState, InteractionState newState) { }

        protected override void PerformInteractionLogic(IInteractable initiator)
        {
            Debug.Log($"[GunPickup] Interaction initiated by {initiator}");
            //Check and set gun to player
            if (initiator is PlayerInteraction player && gunReference != null)
            {
                //Equip gun to player
                player.OnInteracted(gunReference);
                //Hide this object 
                uiIndicator?.gameObject.SetActive(false);
                SetState(InteractionState.Disabled);
                //Set State 
                Debug.Log("Gun Picked up by " + initiator);
            }
            else
            {
                Debug.Log($"[GunPickup] Interaction ignored. Initiator is not a PlayerInteraction or gunReference is null.");
            }
        }
    }
}

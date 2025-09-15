using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using _GAME.Scripts.Test;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace _GAME.Scripts.HideAndSeek.Player
{
    public class PlayerInteraction : AActiveInteractable
    {
        [SerializeField] private InputActionReference inputInteractionRef;
        
        [SerializeField] protected PlayerEquipment playerEquipment;
        
        private InputAction inputInteraction;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            HandleRegisterInput();
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            HandleUnRegisterInput();
        }

        private void HandleRegisterInput()
        {
            if (IsOwner && inputInteractionRef != null)
            {
                inputInteraction = inputInteractionRef.action;
                inputInteraction.Enable();
                inputInteraction.performed += OnInputInteractionPerformed;
            }
        }

        private void OnInputInteractionPerformed(InputAction.CallbackContext obj)
        {
            Debug.Log($"[PlayerInteraction] Interaction input performed by {OwnerClientId}");
            //Call method OnInteract 
            OnInteractInput();
        }


        private void HandleUnRegisterInput()
        {
            if (IsOwner && inputInteraction != null)
            {
                inputInteraction.performed -= OnInputInteractionPerformed;
                inputInteraction.Disable();
            }
        }

        protected override void OnStateChanged(InteractionState previousState, InteractionState newState)
        {
        }

        protected override void OnNearInteractable(APassiveInteractable interactable)
        {
        }

        protected override void OnLeftInteractable(APassiveInteractable interactable)
        {
        }
    }
}
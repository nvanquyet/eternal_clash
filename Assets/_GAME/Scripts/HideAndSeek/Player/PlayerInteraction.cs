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
        
        private InputAction _inputInteraction;

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
                _inputInteraction = inputInteractionRef.action;
                _inputInteraction.Enable();
                _inputInteraction.performed += OnInputInteractionPerformed;
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
            if (IsOwner && _inputInteraction != null)
            {
                _inputInteraction.performed -= OnInputInteractionPerformed;
                _inputInteraction.Disable();
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
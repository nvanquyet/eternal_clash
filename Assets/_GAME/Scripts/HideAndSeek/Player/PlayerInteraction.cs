using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using _GAME.Scripts.HideAndSeek.Player.Rig;
using _GAME.Scripts.Test;
using _GAME.Scripts.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.HideAndSeek.Player
{
    public class PlayerInteraction : AActiveInteractable
    {
        [SerializeField] private InputActionReference inputInteractionRef;
        [SerializeField] protected PlayerEquipment playerEquipment;
        public PlayerEquipment PlayerEquipment => playerEquipment;
        
        private InputAction _inputInteraction;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsOwner)
            {
                HandleRegisterInput();
            }
            else
            {
                // Ngăn non-owner va chạm chủ động
                if (InteractionCollider) InteractionCollider.enabled = false;
            }
        }
         
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (IsOwner)
            {
                HandleUnRegisterInput();
            }
        }

        private void HandleRegisterInput()
        {
            if (IsOwner && inputInteractionRef != null)
            {
                _inputInteraction = InputActionFactory.CreateUniqueAction(inputInteractionRef, GetInstanceID());
                _inputInteraction.Enable();
                _inputInteraction.performed += OnInputInteractionPerformed;
                Debug.Log($"[PlayerInteraction] Interaction input registered for {OwnerClientId}");
            }
        }

        private void OnInputInteractionPerformed(InputAction.CallbackContext obj)
        {
            Debug.Log($"[PlayerInteraction] Interaction input performed by {OwnerClientId}");
            OnInteractInput();
        }

        protected override void OnInteractionPerformed(APassiveInteractable interactable)
        {
            // Logic pick/drop nằm trong WeaponInteraction/PlayerEquipment
        }
        
        private void HandleUnRegisterInput()
        {
            if (IsOwner && _inputInteraction != null)
            {
                _inputInteraction.performed -= OnInputInteractionPerformed;
                _inputInteraction.Disable();
            }
        }

        protected override void OnStateChanged(InteractionState previousState, InteractionState newState) {}
        protected override void OnNearInteractable(APassiveInteractable interactable) {}
        protected override void OnLeftInteractable(APassiveInteractable interactable) {}
    }
}

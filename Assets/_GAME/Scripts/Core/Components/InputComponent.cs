using System;
using UnityEngine;

namespace _GAME.Scripts.Core.Components
{
    public class InputComponent : MonoBehaviour, IPlayerComponent
    {
        private IPlayer _owner;
        public bool IsActive => enabled;

        public event Action<Vector2> OnMoveInput;
        public event Action OnJumpInput;
        public event Action OnInteractInput;
        public event Action OnSkill1Input;
        public event Action OnSkill2Input;
        public event Action OnSkill3Input;

        public void Initialize(IPlayer owner)
        {
            _owner = owner;
        }

        public void OnNetworkSpawn()
        {
            if (_owner.NetObject.IsOwner)
                EnableInput();
        }

        public void OnNetworkDespawn()
        {
            DisableInput();
        }

        private void EnableInput()
        {
            // Register input actions here
        }

        private void DisableInput()
        {
            // Unregister input actions here
        }

        public void TriggerMove(Vector2 direction) => OnMoveInput?.Invoke(direction);
        public void TriggerJump() => OnJumpInput?.Invoke();
        public void TriggerInteract() => OnInteractInput?.Invoke();
        public void TriggerSkill1() => OnSkill1Input?.Invoke();
        public void TriggerSkill2() => OnSkill2Input?.Invoke();
        public void TriggerSkill3() => OnSkill3Input?.Invoke();
    }
}
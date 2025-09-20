using System;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using _GAME.Scripts.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.HideAndSeek.Combat.Gun
{
    public class GunInputComponent : InputComponent
    {
        [SerializeField] private InputActionReference reloadActionRef;
        private InputAction _reloadAction;

        public override void RegisterInput()
        {
            base.RegisterInput();
            if (reloadActionRef != null)
            {
                _reloadAction = InputActionFactory.CreateUniqueAction(reloadActionRef, GetInstanceID());
                _reloadAction.performed += OnReloadPress;
                _reloadAction.Enable();
            }
        }

        public override void UnregisterInput()
        {
            base.UnregisterInput();
            if (_reloadAction != null)
            {
                _reloadAction.performed -= OnReloadPress;
                _reloadAction.Disable();
                _reloadAction = null;
            }
        }

        private void OnReloadPress(InputAction.CallbackContext context)
        {
            Debug.Log($"[GunInputComponent] Reload Pressed");
            OnReloadPerformed?.Invoke();
        }

        public override void EnableInput()
        {
            base.EnableInput();
            if (_reloadAction != null && !_reloadAction.enabled) _reloadAction.Enable();
        }
        public override void DisableInput()
        {
            base.DisableInput();
            if (_reloadAction != null && _reloadAction.enabled) _reloadAction.Disable();
        }

        public event Action OnReloadPerformed;
    }
}
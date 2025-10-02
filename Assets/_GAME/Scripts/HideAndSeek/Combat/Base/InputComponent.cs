using System;
using _GAME.Scripts.Networking;
using _GAME.Scripts.Utils;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace _GAME.Scripts.HideAndSeek.Combat.Base
{
    public class InputComponent : NetworkBehaviour, IWeaponInput
    {
        [Header("Input Actions")]
        [SerializeField] private InputActionReference attackActionRef;
        
        private InputAction _attackAction;

        #region Unity Lifecycle

        private void Awake()
        {
            Initialize();
        }
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            UnregisterInput();
            Cleanup();
        }

        #endregion
        
        public override void OnLostOwnership()
        {
            base.OnLostOwnership();
            DisableInput(); 
        }
        #region  Implementation 

        private bool _isInitialized = false;
        public bool IsInitialized => _isInitialized;
        public virtual void Initialize()
        {
            _isInitialized = true;
        }

        public void Cleanup()
        {
            UnregisterInput();
        }

        public virtual void RegisterInput()
        {
            if (attackActionRef != null && _attackAction == null)
            {
                _attackAction = InputActionFactory.CreateUniqueAction(attackActionRef, GetInstanceID());
                _attackAction.performed += OnAttackPress;
            }
        }

        public virtual void UnregisterInput()
        {
            if (_attackAction != null)
            {
                _attackAction.performed -= OnAttackPress;
                _attackAction.Disable();
                _attackAction.Dispose();
                _attackAction = null;
            }
        }

        public virtual void EnableInput()
        {
            Debug.Log($"[Weapon] Enable Input");
            RegisterInput();
            //Enable input actions => disable interaction when attacking
            if (_attackAction is { enabled: false })
            {
                _attackAction.Enable();
            }
        }
        
        public virtual void DisableInput()
        {
            //Disable all input actions
            UnregisterInput();
            if (_attackAction is { enabled: true })
            {
                _attackAction.Disable();
            }
        }

        public event Action OnAttackPerformed;
        #endregion

        private void OnAttackPress(InputAction.CallbackContext context)
        {
            Debug.Log($"[InputComponent] Attack input received: {context.phase}");
            OnAttackPerformed?.Invoke();
        }
        
    }
}
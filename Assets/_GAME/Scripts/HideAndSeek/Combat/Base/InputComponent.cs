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
        
        [Header("Hold Detection")]
        [Tooltip("Thời gian phân biệt Tap vs Hold (giây)")]
        [SerializeField] private float holdThreshold = 0.2f;
        
        [Header("Debug")]
        [SerializeField] private bool debugLog = false;
        
        private InputAction _attackAction;
        private float _pressStartTime = 0f;
        private bool _wasHolding = false;

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
        
        #region Implementation
        
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
                
                // FIXED: Chỉ đăng ký 1 lần cho mỗi phase
                _attackAction.started += OnAttackStarted;
                _attackAction.canceled += OnAttackCanceled;
                
                if (debugLog) Debug.Log("[InputComponent] Input registered");
            }
        }

        public virtual void UnregisterInput()
        {
            if (_attackAction != null)
            {
                _attackAction.started -= OnAttackStarted;
                _attackAction.canceled -= OnAttackCanceled;
                _attackAction.Disable();
                _attackAction.Dispose();
                _attackAction = null;
                
                if (debugLog) Debug.Log("[InputComponent] Input unregistered");
            }
        }

        public virtual void EnableInput()
        {
            if (debugLog) Debug.Log("[InputComponent] Enable Input");
            RegisterInput();
            
            if (_attackAction is { enabled: false })
            {
                _attackAction.Enable();
            }
        }
        
        public virtual void DisableInput()
        {
            if (debugLog) Debug.Log("[InputComponent] Disable Input");
            UnregisterInput();
            
            if (_attackAction is { enabled: true })
            {
                _attackAction.Disable();
            }
        }

        public event Action OnAttackPerformed;      // Bắn (tap nhanh - KHÔNG zoom)
        public event Action OnActionAttackStarted;  // Bắt đầu aim (giữ lâu - CÓ zoom)
        public event Action OnHoldFirePerformed;    // Bắn sau khi aim (CÓ zoom)
        public event Action OnActionAttackCanceled; // Hủy aim (nếu cần)
        
        #endregion

        /// <summary>
        /// Khi bắt đầu nhấn nút (started phase)
        /// </summary>
        private void OnAttackStarted(InputAction.CallbackContext context)
        {
            _pressStartTime = Time.time;
            _wasHolding = false;
            
            if (debugLog) Debug.Log($"[InputComponent] Attack STARTED at {_pressStartTime}");
        }

        /// <summary>
        /// Khi thả nút (canceled phase) - Phân biệt Tap vs Hold
        /// </summary>
        private void OnAttackCanceled(InputAction.CallbackContext context)
        {
            float holdDuration = Time.time - _pressStartTime;
            
            if (debugLog) Debug.Log($"[InputComponent] Attack CANCELED - Hold: {holdDuration:F3}s, Threshold: {holdThreshold}s, WasHolding: {_wasHolding}");
            
            // CASE 1: BẤM NHANH (Tap) - Bắn ngay KHÔNG ZOOM
            if (holdDuration < holdThreshold)
            {
                if (debugLog) Debug.Log($"[InputComponent] TAP - Quick fire!");
                OnAttackPerformed?.Invoke(); // ← Event cho TAP (không zoom)
            }
            // CASE 2: GIỮ LÂU (Hold to Aim) - Thả để bắn SAU KHI ZOOM
            else
            {
                if (debugLog) Debug.Log($"[InputComponent] HOLD RELEASE - Fire after aim!");
                OnHoldFirePerformed?.Invoke(); // ← Event cho HOLD (có zoom)
            }
            
            _wasHolding = false;
        }
        
        /// <summary>
        /// Update để detect khi đang giữ đủ lâu → trigger aim
        /// </summary>
        private void Update()
        {
            if (!IsOwner) return;
            
            // Nếu đang giữ nút và chưa trigger aim
            if (_attackAction != null && _attackAction.IsPressed() && !_wasHolding)
            {
                float holdDuration = Time.time - _pressStartTime;
                
                // Đã giữ đủ lâu → bật aim mode (SỬ DỤNG holdThreshold)
                if (holdDuration >= holdThreshold)
                {
                    _wasHolding = true;
                    if (debugLog) Debug.Log($"[InputComponent] HOLD DETECTED ({holdDuration:F2}s >= {holdThreshold}s) - Start aiming!");
                    OnActionAttackStarted?.Invoke();
                }
            }
        }
    }
}
using UnityEngine;
using UnityEngine.InputSystem;
using _GAME.Scripts.Utils;

namespace _GAME.Scripts.Player
{
    public class MobileInputBridge : MonoBehaviour
    {
        [SerializeField] private bool usingJoystick = false;
        [SerializeField] private Joystick joystick;
        [SerializeField] private GameObject gamePadMovement;

        [Header("Input Actions (References from asset)")]
        [SerializeField] private InputActionReference moveActionRef;
        [SerializeField] private InputActionReference lookActionRef;
        [SerializeField] private InputActionReference jumpActionRef;
        [SerializeField] private InputActionReference runActionRef;
        [SerializeField] private InputActionReference dashActionRef;

        // Cloned per-instance actions
        private InputAction _move, _look, _jump, _run, _dash;

        // One-shot & holds
        private bool _jumpOneShot;
        private bool _dashOneShot;
        private bool _runHeld;

        // Quyết định có đọc input không
        private bool _acceptInput;
        
        // Flag để đảm bảo chỉ owner mới process input actions
        private bool _isOwner = false;
        
        

        // ======== Lifecycle ========
        private void Awake()
        {
            // UI mặc định
            if (joystick) joystick.gameObject.SetActive(usingJoystick);
            if (gamePadMovement) gamePadMovement.SetActive(!usingJoystick);

            // Tạo unique actions cho instance này - sử dụng factory
            CreateUniqueActions();

            // Mặc định: không đọc input
            _acceptInput = false;
            _isOwner = false;
            DisableActions();
            gameObject.SetActive(false);
        }

        private void CreateUniqueActions()
        {
            int instanceId = GetInstanceID();

            // Tạo actions sử dụng factory
            _move = InputActionFactory.CreateUniqueAction(moveActionRef, instanceId);
            _look = InputActionFactory.CreateUniqueAction(lookActionRef, instanceId);
            _jump = InputActionFactory.CreateUniqueAction(jumpActionRef, instanceId);
            _run = InputActionFactory.CreateUniqueAction(runActionRef, instanceId);
            _dash = InputActionFactory.CreateUniqueAction(dashActionRef, instanceId);

            // Subscribe events
            if (_jump != null) _jump.performed += OnJumpPerformed;
            if (_run != null) 
            {
                _run.performed += OnRunPerformed;
                _run.canceled += OnRunCanceled;
            }
            if (_dash != null) _dash.performed += OnDashPerformed;
        }

        private void OnDestroy()
        {
            // Unsubscribe events
            if (_jump != null) _jump.performed -= OnJumpPerformed;
            if (_dash != null) _dash.performed -= OnDashPerformed;
            if (_run != null)
            {
                _run.performed -= OnRunPerformed;
                _run.canceled -= OnRunCanceled;
            }

            // Dispose actions
            DisableActions();
            _move?.Dispose(); _move = null;
            _look?.Dispose(); _look = null;
            _jump?.Dispose(); _jump = null;
            _run?.Dispose(); _run = null;
            _dash?.Dispose(); _dash = null;
        }

        // ======== Public API ========
        public void SetOwner()
        {
            _acceptInput = true;
            _isOwner = true;
            EnableActions();
            gameObject.SetActive(true);
            
            Debug.Log($"[MobileInputBridge] Instance {GetInstanceID()} set as OWNER");
        }

        public void SetNonOwner()
        {
            _acceptInput = false;
            _isOwner = false;
            DisableActions();
            gameObject.SetActive(false);
            
            // Clear states
            _jumpOneShot = _dashOneShot = false;
            _runHeld = false;
            
            Debug.Log($"[MobileInputBridge] Instance {GetInstanceID()} set as NON-OWNER");
        }

        public PlayerInputData GetPlayerInput()
        {
            if (!_acceptInput || !_isOwner) 
                return PlayerInputData.Empty;

         
            // Lấy one-shot values và clear ngay
            bool jump = _jumpOneShot;
            bool dash = _dashOneShot;
            _jumpOneShot = false;
            _dashOneShot = false;

            return new PlayerInputData
            {
                moveInput =  GetDirectionInput(),
                jumpPressed = jump,
                sprintHeld = _runHeld,
                dashPressed = dash
            };
        }


        private Vector2 GetDirectionInput()
        {
            if (!_acceptInput || !_isOwner) 
                return Vector2.zero;

            // Ưu tiên joystick nếu đang sử dụng
            if (usingJoystick && joystick != null)
            {
                return new Vector2(joystick.Horizontal, joystick.Vertical);
            }
            // Nếu không dùng joystick thì đọc từ Input Action
            else if (_move != null && _move.enabled)
            {
                return _move.ReadValue<Vector2>();
            }

            return Vector2.zero;
        }
        
        
        public void UseJoystick(bool use)
        {
            usingJoystick = use;
            if (joystick) joystick.gameObject.SetActive(use);
            if (gamePadMovement) gamePadMovement.SetActive(!use);
        }

        // ======== Helpers ========
        private void EnableActions() 
        {
            if (!_isOwner) return;
            
            _move?.Enable();
            _look?.Enable();
            _jump?.Enable();
            _run?.Enable();
            _dash?.Enable();
        }

        private void DisableActions()
        {
            _move?.Disable();
            _look?.Disable();
            _jump?.Disable();
            _run?.Disable();
            _dash?.Disable();
        }

        // ======== Callbacks - chỉ xử lý khi là owner ========
        private void OnJumpPerformed(InputAction.CallbackContext context)
        {
            if (_isOwner && _acceptInput)
                _jumpOneShot = true;
        }

        private void OnDashPerformed(InputAction.CallbackContext context)
        {
            if (_isOwner && _acceptInput)
                _dashOneShot = true;
        }

        private void OnRunPerformed(InputAction.CallbackContext context)
        {
            if (_isOwner && _acceptInput)
                _runHeld = true;
        }

        private void OnRunCanceled(InputAction.CallbackContext context)
        {
            if (_isOwner && _acceptInput)
                _runHeld = false;
        }
    }
}
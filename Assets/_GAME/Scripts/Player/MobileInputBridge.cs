using UnityEngine;
using UnityEngine.InputSystem;

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

            // Clone actions - tạo instance riêng cho mỗi player
            CreateUniqueActions();

            // Mặc định: không đọc input
            _acceptInput = false;
            _isOwner = false;
            DisableActions();
            gameObject.SetActive(false);
        }

        private void CreateUniqueActions()
        {
            // Tạo actions hoàn toàn mới thay vì clone
            // Điều này tránh conflict giữa các instances
            
            if (moveActionRef != null)
            {
                _move = new InputAction(name: $"Move_{GetInstanceID()}", type: InputActionType.Value);
                CopyBindings(moveActionRef.action, _move);
            }

            if (jumpActionRef != null)
            {
                _jump = new InputAction(name: $"Jump_{GetInstanceID()}", type: InputActionType.Button);
                CopyBindings(jumpActionRef.action, _jump);
                _jump.performed += OnJumpPerformed;
            }

            if (runActionRef != null)
            {
                _run = new InputAction(name: $"Run_{GetInstanceID()}", type: InputActionType.Button);
                CopyBindings(runActionRef.action, _run);
                _run.performed += OnRunPerformed;
                _run.canceled += OnRunCanceled;
            }

            if (dashActionRef != null)
            {
                _dash = new InputAction(name: $"Dash_{GetInstanceID()}", type: InputActionType.Button);
                CopyBindings(dashActionRef.action, _dash);
                _dash.performed += OnDashPerformed;
            }

            if (lookActionRef != null)
            {
                _look = new InputAction(name: $"Look_{GetInstanceID()}", type: InputActionType.Value);
                CopyBindings(lookActionRef.action, _look);
            }
        }

        private void CopyBindings(InputAction source, InputAction target)
        {
            if (source == null || target == null) return;

            // Copy tất cả bindings từ source action
            for (int i = 0; i < source.bindings.Count; i++)
            {
                target.AddBinding(source.bindings[i]);
            }
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

            Vector2 move = Vector2.zero;

            // Ưu tiên joystick nếu đang sử dụng
            if (usingJoystick && joystick != null)
            {
                move = new Vector2(joystick.Horizontal, joystick.Vertical);
            }
            // Nếu không dùng joystick thì đọc từ Input Action
            else if (_move != null && _move.enabled)
            {
                move = _move.ReadValue<Vector2>();
            }

            // Lấy one-shot values và clear ngay
            bool jump = _jumpOneShot;
            bool dash = _dashOneShot;
            _jumpOneShot = false;
            _dashOneShot = false;

            return new PlayerInputData
            {
                moveInput = move,
                jumpPressed = jump,
                sprintHeld = _runHeld,
                dashPressed = dash
            };
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
            if (!_isOwner) return; // Chỉ owner mới enable được
            
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
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.Player
{
    // Đổi tên để tránh đè với component UnityEngine.InputSystem.PlayerInput
    public class MobileInputBridge : MonoBehaviour
    {
        [SerializeField] private bool usingJoystick = false;
        [SerializeField] private Joystick joystick;   // GamePlay/Move   (Vector2)
        [SerializeField] private GameObject gamePadMovement;   // GamePlay/Move   (Vector2)
        [Header("Input Actions")]
        [SerializeField] private InputActionReference moveAction;   // GamePlay/Move   (Vector2)
        [SerializeField] private InputActionReference lookAction;   // GamePlay/Look   (Vector2) - optional
        [SerializeField] private InputActionReference jumpAction;   // GamePlay/Jump   (Button)
        [SerializeField] private InputActionReference runAction;    // GamePlay/Run    (Button - hold)
        [SerializeField] private InputActionReference dashAction;   // GamePlay/Dash   (Button)

        // Trạng thái
        private bool jumpPressedOneShot;
        private bool dashPressedOneShot;
        private bool runHeld;

        private void Start()
        {
            if(joystick) joystick.gameObject.SetActive(usingJoystick);
            if(gamePadMovement) gamePadMovement.SetActive(!usingJoystick);
        }

        private void OnEnable()
        {
            moveAction?.action.Enable();
            lookAction?.action.Enable();
            jumpAction?.action.Enable();
            runAction?.action.Enable();
            dashAction?.action.Enable();

            // Button callbacks
            if (jumpAction) jumpAction.action.performed  += OnJumpPerformed;
            if (dashAction) dashAction.action.performed  += OnDashPerformed;
            if (runAction)
            {
                runAction.action.performed += OnRunPerformed;   // giữ
                runAction.action.canceled  += OnRunCanceled;    // nhả
            }
        }

        private void OnDisable()
        {
            if (jumpAction) jumpAction.action.performed  -= OnJumpPerformed;
            if (dashAction) dashAction.action.performed  -= OnDashPerformed;
            if (runAction)
            {
                runAction.action.performed -= OnRunPerformed;
                runAction.action.canceled  -= OnRunCanceled;
            }

            moveAction?.action.Disable();
            lookAction?.action.Disable();
            jumpAction?.action.Disable();
            runAction?.action.Disable();
            dashAction?.action.Disable();
        }

        // ======== API đọc input cho gameplay ========
        public PlayerInputData GetPlayerInput()
        {
            var move = usingJoystick ? (joystick ? new Vector2(joystick.Horizontal, joystick.Vertical) : Vector2.zero) : (moveAction ? moveAction.action.ReadValue<Vector2>() : Vector2.zero);

            // One-shot: tiêu thụ sau khi đọc
            bool jump = jumpPressedOneShot;
            bool dash = dashPressedOneShot;
            jumpPressedOneShot = false;
            dashPressedOneShot = false;

            return new PlayerInputData
            {
                moveInput   = move,
                jumpPressed = jump,
                sprintHeld  = runHeld,
                dashPressed = dash
            };
        }

        // ======== Callbacks ========
        private void OnJumpPerformed(InputAction.CallbackContext _) => jumpPressedOneShot = true;
        private void OnDashPerformed (InputAction.CallbackContext _) => dashPressedOneShot = true;
        private void OnRunPerformed  (InputAction.CallbackContext _) => runHeld = true;
        private void OnRunCanceled   (InputAction.CallbackContext _) => runHeld = false;
    }
}

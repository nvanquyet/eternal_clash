using _GAME.Scripts.UI;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.Player
{
    public class PlayerInput : MonoBehaviour
    {
        [Header("Mobile Controls")]
        [SerializeField] private Joystick joystick;
        [SerializeField] private Button jumpButton;
        [SerializeField] private ButtonEvents runButtonEvents;
        [SerializeField] private Button dashButton;
        
        // Input states for mobile controls
        private bool jumpPressed = false;
        private bool runHeld = false;
        private bool dashPressed = false;
        
        private void Start()
        {
            // Setup button listeners
            if (jumpButton != null)
                jumpButton.onClick.AddListener(OnJumpButtonClicked);
            if (runButtonEvents != null)
            {
                runButtonEvents.OnPointerDownEvent += OnRunButtonPressed;
                runButtonEvents.OnPointerUpEvent += OnRunButtonReleased;
            }
            if (dashButton != null)
                dashButton.onClick.AddListener(OnDashButtonClicked);
        }

        private void OnDestroy()
        {
            // Clean up listeners
            if (jumpButton != null)
                jumpButton.onClick.RemoveListener(OnJumpButtonClicked);
            if (runButtonEvents != null)
            {
                runButtonEvents.OnPointerDownEvent -= OnRunButtonPressed;
                runButtonEvents.OnPointerUpEvent -= OnRunButtonReleased;
            }
            if (dashButton != null)
                dashButton.onClick.RemoveListener(OnDashButtonClicked);
        }

        public PlayerInputData GetPlayerInput()
        {
            Vector2 moveInput = GetMovementInput();
            
            // Combine keyboard and mobile input
            bool jumpInput = Input.GetKeyDown(KeyCode.Space) || jumpPressed;
            bool sprintInput = Input.GetKey(KeyCode.LeftShift) || runHeld;
            bool dashInput = Input.GetKeyDown(KeyCode.LeftControl) || dashPressed;
            
            // Reset one-time inputs after reading
            if (jumpPressed) jumpPressed = false;
            if (dashPressed) dashPressed = false;
            
            return new PlayerInputData
            {
                moveInput = moveInput,
                jumpPressed = jumpInput,
                sprintHeld = sprintInput,
                dashPressed = dashInput,
            };
        }

        private Vector2 GetMovementInput()
        {
            Vector2 input = Vector2.zero;
            
            // Get keyboard input
            Vector2 keyboardInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            
            // Get joystick input
            Vector2 joystickInput = Vector2.zero;
            if (joystick != null)
            {
                joystickInput = new Vector2(joystick.Horizontal, joystick.Vertical);
            }
            
            // Combine inputs (prioritize the one with larger magnitude)
            if (keyboardInput.magnitude > joystickInput.magnitude)
                input = keyboardInput;
            else
                input = joystickInput;
                
            return input;
        }

        // Mobile button event handlers
        private void OnJumpButtonClicked()
        {
            Debug.Log("Jump button clicked");
            jumpPressed = true;
        }

        private void OnRunButtonPressed()
        {
            Debug.Log("Run button pressed");
            runHeld = true;
        }
        
        private void OnRunButtonReleased()
        {
            Debug.Log("Run button released");
            runHeld = false;
        }

        private void OnDashButtonClicked()
        {
            Debug.Log("Dash button clicked");
            dashPressed = true;
        }
    }
}
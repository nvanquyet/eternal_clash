using _GAME.Scripts.Player.Config;
using _GAME.Scripts.Player.Locomotion;
using _GAME.Scripts.Player.Locomotion.States;
using UnityEngine;

namespace _GAME.Scripts.Player
{
    public class PlayerLocomotion
    {
        // Configuration for movement settings
        private readonly Transform _playerTransform;
        private readonly PlayerLocomotionConfig _playerLocomotionConfig;
        private readonly CharacterController _characterController;
        private readonly Animator _animator;

        public PlayerLocomotion(PlayerLocomotionConfig playerLocomotionConfig, CharacterController characterController, Animator animator)
        {
            this._playerTransform = characterController ? characterController.transform : null;
            this._playerLocomotionConfig = playerLocomotionConfig;
            this._characterController = characterController;
            this._animator = animator;
            
            InitializeLocomotion();
        }
        
        // Internal state and velocity tracking
        private Vector3 _velocity;
        private Vector3 _inputDirection;
        private ALocomotionState _currentState;
        private PlayerLocomotionAnimator _playerLocomotionAnimator;

        #region Properties
        // Expose key properties for external access
        public CharacterController CharacterController => _characterController;
        public PlayerLocomotionConfig Config => _playerLocomotionConfig;
        public ALocomotionState CurrentState => _currentState;
        public Vector3 Velocity => _velocity;
        public Vector3 InputDirection => _inputDirection;
        #endregion

        #region Unity Methods
        /// <summary>
        /// Updates the current locomotion state every frame.
        /// </summary>
        public void OnUpdate()
        {
            _currentState?.OnUpdate(this);
        }

        /// <summary>
        /// Handles physics-based updates and applies gravity.
        /// </summary>
        public void OnFixedUpdate()
        {
            _currentState?.OnFixedUpdate(this);
            ApplyGravity();
                   
            // Apply movement
            _characterController.Move(_velocity * Time.fixedDeltaTime);
        }

        /// <summary>
        /// Updates the animator with the latest locomotion data.
        /// </summary>
        public void OnLateUpdate()
        {
            _playerLocomotionAnimator?.OnLateUpdate(this);
        }

        #endregion

        #region State Management

        /// <summary>
        /// Switches to a new locomotion state if it's different from the current one.
        /// </summary>
        /// <param name="newState">The new locomotion state to switch to.</param>
        public void SetLocomotionState(ALocomotionState newState)
        {
            if (newState == null || IsSameState(newState)) return;

            _currentState?.OnExit(this); // Exit the current state
            _currentState = newState;
            _currentState.OnEnter(this); // Enter the new state
        }

        /// <summary>
        /// Checks if the new state is the same as the current state.
        /// </summary>
        /// <param name="newState">The new state to compare.</param>
        /// <returns>True if the states are the same, false otherwise.</returns>
        private bool IsSameState(ALocomotionState newState)
        {
            return _currentState != null && newState != null && newState.GetType() == _currentState.GetType();
        }

        #endregion

        #region Movement Logic

        /// <summary>
        /// Updates the vertical velocity (used for jumping and gravity).
        /// </summary>
        /// <param name="yVelocity">The new vertical velocity value.</param>
        public void ApplyVerticalVelocity(float yVelocity)
        {
            _velocity.y = yVelocity;
        }
        
       
        /// <summary>
        /// Updates the horizontal velocity (used for movement along the X and Z axes).
        /// </summary>
        /// <param name="x">The new velocity along the X-axis.</param>
        /// <param name="z">The new velocity along the Z-axis.</param>
        private void ApplyHorizontalVelocity(float x, float z)
        {
            _velocity.x = x;
            _velocity.z = z;
        }
        
        /// <summary>
        /// Calculates the movement direction based on camera orientation and input values, 
        /// updates the horizontal velocity, and adjusts the player's rotation accordingly.
        /// </summary>
        /// <param name="horizontal">The horizontal input value (X-axis).</param>
        /// <param name="vertical">The vertical input value (Z-axis).</param>
        /// <param name="speed">The movement speed multiplier.</param>
        public void ApplyInputDirection(float horizontal, float vertical, float speed)
        {
            // Update input direction
            _inputDirection = new Vector3(horizontal, 0, vertical) * speed;
            
            if (Camera.main != null)
            {
                var cam = Camera.main.transform;
                Vector3 moveDir = Vector3.Normalize(cam.forward * vertical + cam.right * horizontal);
                moveDir.y = 0;
                // Update horizontal velocity
                ApplyHorizontalVelocity(
                    moveDir.x * speed,
                    moveDir.z * speed
                );
                
                //Check rotation
                if (moveDir != Vector3.zero)
                {
                    Quaternion toRot = Quaternion.LookRotation(moveDir);
                    if (_playerTransform)
                    {
                        _playerTransform.rotation = Quaternion.Slerp(_playerTransform.rotation, toRot, 10f * Time.deltaTime);
                    }
                }
            }
        }
        /// <summary>
        /// Applies gravity to the player when not grounded and handles falling state transitions.
        /// </summary>
        private void ApplyGravity()
        {
            if (!_characterController.isGrounded)
            {
                // Apply gravity when not grounded
                _velocity.y += Physics.gravity.y * Time.fixedDeltaTime;

                // Transition to Falling state if descending
                if (_velocity.y <= 0) 
                {
                    TransitionToFallingState();
                }
            }     
        }

        /// <summary>
        /// Transitions the player to the Falling state.
        /// </summary>
        private void TransitionToFallingState()
        {
            SetLocomotionState(new FallingMotion());
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the locomotion system and sets the initial state.
        /// </summary>
        private void InitializeLocomotion()
        {
            SetLocomotionState(new IdleMotion()); // Start with Idle state
            _playerLocomotionAnimator = new PlayerLocomotionAnimator(_animator);
        }

        #endregion


        public void Rotate(Vector3 direction)
        {
            //transform.Rotate(direction);
        } 
    }
}
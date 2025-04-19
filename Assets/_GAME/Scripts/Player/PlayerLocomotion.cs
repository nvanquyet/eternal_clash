using _GAME.Scripts.Player.Locomotion;
using _GAME.Scripts.Player.Locomotion.States;
using Player.Locomotion;
using UnityEngine;

namespace _GAME.Scripts.Player
{
    public class PlayerLocomotion : MonoBehaviour
    {
        // Configuration for movement settings
        [SerializeField] private PlayerLocomotionConfig playerLocomotionConfig;

        // References to required components
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Animator animator;

        // Internal state and velocity tracking
        private Vector3 _velocity;
        private ALocomotionState _currentState;
        private PlayerLocomotionAnimator _playerLocomotionAnimator;

        #region Properties
        // Expose key properties for external access
        public CharacterController CharacterController => characterController;
        public PlayerLocomotionConfig Config => playerLocomotionConfig;
        public ALocomotionState CurrentState => _currentState;
        public Vector3 Velocity => _velocity;
        #endregion

        #region Unity Methods

#if UNITY_EDITOR
        /// <summary>
        /// Ensures required components are assigned in the editor.
        /// </summary>
        private void OnValidate()
        {
            characterController ??= GetComponentInChildren<CharacterController>();
            animator ??= GetComponentInChildren<Animator>();
        }
#endif

        /// <summary>
        /// Initializes the locomotion system on start.
        /// </summary>
        private void Start()
        {
            InitializeLocomotion();
        }

        /// <summary>
        /// Updates the current locomotion state every frame.
        /// </summary>
        private void Update()
        {
            _currentState?.OnUpdate(this);
        }

        /// <summary>
        /// Handles physics-based updates and applies gravity.
        /// </summary>
        private void FixedUpdate()
        {
            _currentState?.OnFixedUpdate(this);
            ApplyGravity();
                   
            // Apply movement
            characterController.Move(_velocity * Time.fixedDeltaTime);
        }

        /// <summary>
        /// Updates the animator with the latest locomotion data.
        /// </summary>
        private void LateUpdate()
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
        public void ApplyHorizontalVelocity(float x, float z)
        {
            _velocity.x = x;
            _velocity.z = z;
        }
        /// <summary>
        /// Applies gravity to the player when not grounded and handles falling state transitions.
        /// </summary>
        private void ApplyGravity()
        {
            if (!characterController.isGrounded)
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
            _playerLocomotionAnimator = new PlayerLocomotionAnimator(animator);
        }

        #endregion


        public void Rotate(Vector3 direction)
        {
            //transform.Rotate(direction);
        } 
    }
}
using _GAME.Scripts.Player.Enum;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion
{
    public class PlayerLocomotionAnimator
    {
        private static readonly int XVelocity = Animator.StringToHash("xVelocity");
        private static readonly int ZVelocity = Animator.StringToHash("zVelocity");
        private static readonly int YVelocity = Animator.StringToHash("yVelocity");
        private static readonly int IsGrounded = Animator.StringToHash("isGrounded");
        
        private readonly Animator _animator;
        private readonly PlayerLocomotion _playerLocomotion;
        private readonly PlayerController _playerController;

        // Animation smoothing for better visual quality
        private Vector3 _smoothedInputDirection;
        private bool _lastGroundedState;
        private const float ANIMATION_SMOOTHING = 8f;

        public PlayerLocomotionAnimator(Animator animator, PlayerLocomotion playerLocomotion, PlayerController playerController)
        {
            _animator = animator;
            _playerLocomotion = playerLocomotion;
            _playerController = playerController;
            
            _smoothedInputDirection = Vector3.zero;
            _lastGroundedState = false;
        }

        public void OnLateUpdate()
        {
            if (_animator == null || _playerLocomotion == null || _playerController == null) return;

            // Chỉ owner/server update animation parameters - sẽ được sync qua NetworkVariable
            if (!_playerController.IsServer && !_playerController.IsOwner) return;

            Vector3 velocity = _playerLocomotion.Velocity;
            Vector3 inputDirection = _playerLocomotion.InputDirection;
            LocomotionState state = _playerLocomotion.CurrentState.LocomotionState;
            bool isGrounded = _playerLocomotion.IsGrounded;

            // Smooth input direction for better animation transitions
            SmoothInputDirection(inputDirection);

            // Update grounded state immediately for responsive animations
            if (isGrounded != _lastGroundedState)
            {
                _animator.SetBool(IsGrounded, isGrounded);
                _lastGroundedState = isGrounded;
            }

            if (isGrounded)
            {
                UpdateGroundedAnimation(state);
            }
            else
            {
                UpdateAirborneAnimation(velocity, state);
            }
        }

        private void SmoothInputDirection(Vector3 targetDirection)
        {
            // Smooth the input direction for better animation blending
            _smoothedInputDirection = Vector3.Lerp(_smoothedInputDirection, targetDirection, 
                ANIMATION_SMOOTHING * Time.deltaTime);
        }

        private void UpdateGroundedAnimation(LocomotionState state)
        {
            float multiplier = GetAnimationMultiplier(state);
            
            // Use smoothed input direction for grounded movement
            Vector3 animDirection = _smoothedInputDirection;
            
            // Special handling for idle state
            if (state == LocomotionState.Idle)
            {
                animDirection = Vector3.zero;
            }
            
            UpdateVelocityParameters(animDirection, multiplier);
        }

        private void UpdateAirborneAnimation(Vector3 velocity, LocomotionState state)
        {
            // Y velocity always follows physics for smooth jump/fall animations
            _animator.SetFloat(YVelocity, velocity.y);

            // Handle horizontal movement based on state
            Vector3 horizontalAnim = Vector3.zero;
            
            switch (state)
            {
                case LocomotionState.Dashing:
                    // During dash, use normalized velocity direction for stable animation
                    Vector3 dashDir = new Vector3(velocity.x, 0, velocity.z);
                    if (dashDir.magnitude > 0.1f)
                    {
                        horizontalAnim = dashDir.normalized * GetAnimationMultiplier(state);
                    }
                    break;
                    
                case LocomotionState.Jumping:
                case LocomotionState.Falling:
                    // For air movement, use smoothed input for responsive feel
                    horizontalAnim = new Vector3(_smoothedInputDirection.x, 0, _smoothedInputDirection.z);
                    break;
            }

            // Update horizontal animation parameters
            _animator.SetFloat(XVelocity, Mathf.Clamp(horizontalAnim.x, -2f, 2f));
            _animator.SetFloat(ZVelocity, Mathf.Clamp(horizontalAnim.z, -2f, 2f));
        }

        private float GetAnimationMultiplier(LocomotionState state)
        {
            return state switch
            {
                LocomotionState.Running => 2f,
                LocomotionState.Walking => 1f,
                LocomotionState.Dashing => 1f,
                LocomotionState.Jumping => 1f,
                LocomotionState.Falling => 1f,
                LocomotionState.Idle => 0f,
                _ => 0f
            };
        }

        private void UpdateVelocityParameters(Vector3 velocity, float maxClamp)
        {
            _animator.SetFloat(XVelocity, Mathf.Clamp(velocity.x, -maxClamp, maxClamp));
            _animator.SetFloat(ZVelocity, Mathf.Clamp(velocity.z, -maxClamp, maxClamp));
        }

        // Method để force update animation từ network data (cho non-owners)
        public void UpdateFromNetworkData(Vector3 networkVelocity, bool networkGrounded)
        {
            if (_animator == null) return;
            
            // Chỉ non-owners sử dụng method này
            if (_playerController.IsOwner || _playerController.IsServer) return;

            _animator.SetFloat(XVelocity, Mathf.Clamp(networkVelocity.x, -2f, 2f));
            _animator.SetFloat(ZVelocity, Mathf.Clamp(networkVelocity.z, -2f, 2f));
            _animator.SetFloat(YVelocity, networkVelocity.y);
            _animator.SetBool(IsGrounded, networkGrounded);
        }

        // Method để reset animation state khi cần
        public void ResetAnimationState()
        {
            if (_animator == null) return;

            _smoothedInputDirection = Vector3.zero;
            _animator.SetFloat(XVelocity, 0f);
            _animator.SetFloat(ZVelocity, 0f);
            _animator.SetFloat(YVelocity, 0f);
            _animator.SetBool(IsGrounded, true);
        }
    }
}
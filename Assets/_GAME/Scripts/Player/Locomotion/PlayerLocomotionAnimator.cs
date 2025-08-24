using _GAME.Scripts.Player.Enum;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion
{
    public class PlayerLocomotionAnimator
    {
        private static readonly int XVelocity  = Animator.StringToHash("xVelocity");
        private static readonly int ZVelocity  = Animator.StringToHash("zVelocity");
        private static readonly int YVelocity  = Animator.StringToHash("yVelocity");
        private static readonly int IsGrounded = Animator.StringToHash("isGrounded");

        private readonly Animator _animator;
        private readonly PlayerLocomotion _playerLocomotion;
        private readonly PlayerController _playerController;

        // smoothing
        private Vector3 _smoothedInputDirection = Vector3.zero;
        private bool _lastGroundedState = false;
        private const float ANIMATION_SMOOTHING = 8f;
        private const float DAMP_TIME = 0.08f; // damping cho SetFloat

        public PlayerLocomotionAnimator(
            Animator animator,
            PlayerLocomotion playerLocomotion,
            PlayerController playerController)
        {
            _animator = animator;
            _playerLocomotion = playerLocomotion;
            _playerController = playerController;
        }

        public void OnLateUpdate()
        {
            if (_animator == null || _playerLocomotion == null || _playerController == null) return;

            // CHỈ OWNER cập nhật tham số
            if (!_playerController.IsOwner) return;

            var velocity       = _playerLocomotion.Velocity;
            var inputDirection = _playerLocomotion.InputDirection;
            var state          = _playerLocomotion.CurrentState.LocomotionState;
            var isGrounded     = _playerLocomotion.IsGrounded;

            SmoothInputDirection(inputDirection);

            if (isGrounded != _lastGroundedState)
            {
                _animator.SetBool(IsGrounded, isGrounded);
                _lastGroundedState = isGrounded;
            }

            if (isGrounded)
                UpdateGroundedAnimation(state);
            else
                UpdateAirborneAnimation(velocity, state);

            // === SYNC TO OTHER CLIENTS ===
            float currentXVel = _animator.GetFloat(XVelocity);
            float currentZVel = _animator.GetFloat(ZVelocity);
            float currentYVel = _animator.GetFloat(YVelocity);
            _playerController.SyncAnimationRpc(currentXVel, currentZVel, currentYVel, isGrounded);
        }

        private void SmoothInputDirection(Vector3 targetDirection)
        {
            _smoothedInputDirection = Vector3.Lerp(
                _smoothedInputDirection, targetDirection, ANIMATION_SMOOTHING * Time.deltaTime);
        }

        private void UpdateGroundedAnimation(LocomotionState state)
        {
            float multiplier = GetAnimationMultiplier(state);

            Vector3 animDirection = state == LocomotionState.Idle
                ? Vector3.zero
                : _smoothedInputDirection;

            UpdateVelocityParameters(animDirection, multiplier);
        }

        private void UpdateAirborneAnimation(Vector3 velocity, LocomotionState state)
        {
            _animator.SetFloat(YVelocity, velocity.y, DAMP_TIME, Time.deltaTime);

            Vector3 horizontalAnim = Vector3.zero;

            switch (state)
            {
                case LocomotionState.Dashing:
                {
                    Vector3 dashDir = new Vector3(velocity.x, 0f, velocity.z);
                    if (dashDir.sqrMagnitude > 0.01f)
                        horizontalAnim = dashDir.normalized * GetAnimationMultiplier(state);
                    break;
                }
                case LocomotionState.Jumping:
                case LocomotionState.Falling:
                {
                    horizontalAnim = new Vector3(_smoothedInputDirection.x, 0f, _smoothedInputDirection.z);
                    break;
                }
            }

            // Clamp + damping cho mượt
            _animator.SetFloat(XVelocity, Mathf.Clamp(horizontalAnim.x, -2f, 2f), DAMP_TIME, Time.deltaTime);
            _animator.SetFloat(ZVelocity, Mathf.Clamp(horizontalAnim.z, -2f, 2f), DAMP_TIME, Time.deltaTime);
        }

        private float GetAnimationMultiplier(LocomotionState state)
        {
            return state switch
            {
                LocomotionState.Running  => 2f,
                LocomotionState.Walking  => 1f,
                LocomotionState.Dashing  => 1f,
                LocomotionState.Jumping  => 1f,
                LocomotionState.Falling  => 1f,
                LocomotionState.Idle     => 0f,
                _                        => 0f
            };
        }

        private void UpdateVelocityParameters(Vector3 velocity, float maxClamp)
        {
            _animator.SetFloat(XVelocity, Mathf.Clamp(velocity.x, -maxClamp, maxClamp), DAMP_TIME, Time.deltaTime);
            _animator.SetFloat(ZVelocity, Mathf.Clamp(velocity.z, -maxClamp, maxClamp), DAMP_TIME, Time.deltaTime);
        }

        // === Animation Triggers ===

        /// Gọi cho các action kiểu Trigger (Dash/Attack). Chỉ owner gọi.
        public void SetTrigger(string triggerName)
        {
            if (!_playerController.IsOwner) return;
            
            // Set local trigger (instant)
            _animator.SetTrigger(triggerName);
            
            // Sync to other clients
            _playerController.SyncTriggerRpc(triggerName);
        }

        /// Reset về trạng thái mặc định (owner side).
        public void ResetAnimationState()
        {
            if (_animator == null) return;
            _smoothedInputDirection = Vector3.zero;
            _animator.SetFloat(XVelocity, 0f);
            _animator.SetFloat(ZVelocity, 0f);
            _animator.SetFloat(YVelocity, 0f);
            _animator.SetBool(IsGrounded, true);
            
            // Sync reset state to other clients
            if (_playerController.IsOwner)
            {
                _playerController.SyncAnimationRpc(0f, 0f, 0f, true);
            }
        }
    }
}
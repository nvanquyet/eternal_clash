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

        public PlayerLocomotionAnimator(Animator animator)
        {
            _animator = animator;
        }

        public void OnLateUpdate(PlayerLocomotion playerLocomotion)
        {
            Vector3 velocity = playerLocomotion.Velocity;
            LocomotionState state = playerLocomotion.CurrentState.LocomotionState;

            switch (state)
            {
                case LocomotionState.Idle:
                case LocomotionState.Walking:
                case LocomotionState.Running:
                    UpdateGroundedState(velocity, state == LocomotionState.Running ? 2 : 1);
                    break;
                case LocomotionState.Jumping:
                case LocomotionState.Falling:
                    UpdateAirborneState(velocity);
                    break;
            }
        }

        private void UpdateGroundedState(Vector3 velocity, float maxClamp)
        {
            _animator?.SetBool(IsGrounded, true);
            UpdateAnimator(velocity, maxClamp);
        }

        private void UpdateAirborneState(Vector3 velocity)
        {
            _animator?.SetBool(IsGrounded, false);
            _animator?.SetFloat(YVelocity, velocity.y);
        }

        private void UpdateAnimator(Vector3 velocity, float maxClamp)
        {
            _animator?.SetFloat(XVelocity, Mathf.Clamp(velocity.x, -maxClamp, maxClamp));
            _animator?.SetFloat(ZVelocity, Mathf.Clamp(velocity.z, -maxClamp, maxClamp));
        }
    }
}
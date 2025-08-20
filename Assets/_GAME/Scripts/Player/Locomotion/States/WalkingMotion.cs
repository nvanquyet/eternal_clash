using _GAME.Scripts.Player.Enum;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion.States
{
    public class WalkingMotion : ALocomotionState
    {
        public override LocomotionState LocomotionState => LocomotionState.Walking;

        public override void ProcessInput(PlayerInputData input, PlayerLocomotion locomotion)
        {
            // Try dash first
            if (DashingMotion.TryStartDash(input, locomotion)) return;

            if (input.jumpPressed && locomotion.IsGrounded)
            {
                TransitionTo(locomotion, new JumpingMotion());
                return;
            }

            if (input.sprintHeld)
            {
                TransitionTo(locomotion, new RunningMotion());
                return;
            }
        }

        public override void OnFixedUpdate(PlayerInputData input, PlayerLocomotion locomotion)
        {
            if (input.moveInput.magnitude < 0.1f)
            {
                TransitionTo(locomotion, new IdleMotion());
                return;
            }

            Vector3 inputDirection = new Vector3(input.moveInput.x, 0, input.moveInput.y);
            locomotion.ApplyMovement(inputDirection, GetSpeed(locomotion));
        }

        protected virtual float GetSpeed(PlayerLocomotion locomotion) => locomotion.Config.WalkSpeed;
    }
}
using _GAME.Scripts.Player.Enum;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion.States
{
    public class IdleMotion : ALocomotionState
    {
        public override LocomotionState LocomotionState => LocomotionState.Idle;

        public override void ProcessInput(PlayerInputData input, PlayerLocomotion locomotion)
        {
            // Try dash first (highest priority)
            if (DashingMotion.TryStartDash(input, locomotion)) return;

            if (input.jumpPressed && locomotion.IsGrounded)
            {
                TransitionTo(locomotion, new JumpingMotion());
                return;
            }
        }

        public override void OnFixedUpdate(PlayerInputData input, PlayerLocomotion locomotion)
        {
            if (input.moveInput.magnitude > 0.1f)
            {
                var nextState = input.sprintHeld ? new RunningMotion() : new WalkingMotion();
                TransitionTo(locomotion, nextState);
                return;
            }

            locomotion.ApplyMovement(Vector3.zero, 0);
        }
    }
}
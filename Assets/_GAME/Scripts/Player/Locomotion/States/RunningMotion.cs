using _GAME.Scripts.Player.Enum;
using UnityEngine;
namespace _GAME.Scripts.Player.Locomotion.States
{
    public class RunningMotion : WalkingMotion
    {
        public override LocomotionState LocomotionState => LocomotionState.Running;

        public override void ProcessInput(PlayerInputData input, PlayerLocomotion locomotion)
        {
            // Try dash first
            if (DashingMotion.TryStartDash(input, locomotion)) return;

            if (input.jumpPressed && locomotion.IsGrounded)
            {
                TransitionTo(locomotion, new JumpingMotion());
                return;
            }

            if (!input.sprintHeld)
            {
                TransitionTo(locomotion, new WalkingMotion());
                return;
            }
        }

        protected override float GetSpeed(PlayerLocomotion locomotion) => locomotion.Config.RunSpeed;
    }
}
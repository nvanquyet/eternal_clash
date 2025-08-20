using _GAME.Scripts.Player.Enum;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion.States
{
    public class JumpingMotion : ALocomotionState
    {
        public override LocomotionState LocomotionState => LocomotionState.Jumping;

        public override void OnEnter(PlayerLocomotion locomotion)
        {
            if (locomotion.IsGrounded)
            {
                locomotion.ApplyVerticalVelocity(locomotion.Config.JumpForce);
            }
        }

        public override void ProcessInput(PlayerInputData input, PlayerLocomotion locomotion)
        {
            // Try dash first (air dash available)
            if (DashingMotion.TryStartDash(input, locomotion)) return;
        }

        public override void OnFixedUpdate(PlayerInputData input, PlayerLocomotion locomotion)
        {
            // Check if we should transition to falling
            if (locomotion.Velocity.y <= 0)
            {
                TransitionTo(locomotion, new FallingMotion());
                return;
            }

            // Air movement with reduced control
            if (input.moveInput.magnitude > 0.1f)
            {
                Vector3 inputDirection = new Vector3(input.moveInput.x, 0, input.moveInput.y);
                float baseSpeed = GetAirControlBaseSpeed(locomotion, input);
                locomotion.ApplyAirMovement(inputDirection, baseSpeed, locomotion.Config.JumpAirControlMultiplier);
            }
        }

        private float GetAirControlBaseSpeed(PlayerLocomotion locomotion, PlayerInputData input)
        {
            if (locomotion.Config.UseLastGroundSpeedForAirControl)
            {
                return locomotion.LastGroundSpeed > 0 ? locomotion.LastGroundSpeed : locomotion.Config.WalkSpeed;
            }

            return input.sprintHeld ? locomotion.Config.RunSpeed : locomotion.Config.WalkSpeed;
        }
    }
}
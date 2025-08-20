using _GAME.Scripts.Player.Enum;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion.States
{
    public class FallingMotion : ALocomotionState
    {
        public override LocomotionState LocomotionState => LocomotionState.Falling;

        public override void ProcessInput(PlayerInputData input, PlayerLocomotion locomotion)
        {
            // Try dash first (air dash available)
            if (DashingMotion.TryStartDash(input, locomotion)) return;
        }

        public override void OnFixedUpdate(PlayerInputData input, PlayerLocomotion locomotion)
        {
            // Check if we landed
            if (locomotion.IsGrounded)
            {
                TransitionTo(locomotion, new IdleMotion());
                return;
            }

            // Air movement with even more reduced control than jumping
            if (input.moveInput.magnitude > 0.1f)
            {
                Vector3 inputDirection = new Vector3(input.moveInput.x, 0, input.moveInput.y);
                float baseSpeed = GetAirControlBaseSpeed(locomotion, input);
                locomotion.ApplyAirMovement(inputDirection, baseSpeed, locomotion.Config.FallAirControlMultiplier);
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
using _GAME.Scripts.Player.Enum;

namespace _GAME.Scripts.Player.Locomotion.States
{
    public class JumpingMotion : ALocomotionState
    {
        public override LocomotionState LocomotionState => LocomotionState.Jumping;

        public override void OnEnter(PlayerLocomotion playerLocomotion)
        {
            if (playerLocomotion.CharacterController.isGrounded)
            {
                playerLocomotion.ApplyVerticalVelocity(playerLocomotion.Config.JumpForce);
            }
        }

        public override void OnUpdate(PlayerLocomotion playerLocomotion)
        {
            if (playerLocomotion.Velocity.y <= 0)
            {
                playerLocomotion.SetLocomotionState(new FallingMotion());
            }
        }

        public override void OnExit(PlayerLocomotion playerLocomotion)
        {
        }

        public override void OnFixedUpdate(PlayerLocomotion playerLocomotion)
        {
        }
    }
}
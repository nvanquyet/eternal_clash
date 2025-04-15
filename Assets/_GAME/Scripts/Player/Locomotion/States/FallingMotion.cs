using _GAME.Scripts.Player.Enum;

namespace _GAME.Scripts.Player.Locomotion.States
{
    public class FallingMotion : ALocomotionState
    {
        public override LocomotionState LocomotionState => LocomotionState.Falling;

        public override void OnEnter(PlayerLocomotion playerLocomotion) { }

        public override void OnUpdate(PlayerLocomotion playerLocomotion)
        {
            if (playerLocomotion.CharacterController.isGrounded)
            {
                playerLocomotion.SetLocomotionState(new IdleMotion());
            }
        }

        public override void OnExit(PlayerLocomotion playerLocomotion) { }
        public override void OnFixedUpdate(PlayerLocomotion playerLocomotion) { }
    }
}
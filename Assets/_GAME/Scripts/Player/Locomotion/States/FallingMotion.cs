using _GAME.Scripts.Player.Enum;

namespace _GAME.Scripts.Player.Locomotion.States
{
    public class FallingMotion : ALocomotionState
    {
        public override LocomotionState LocomotionState => LocomotionState.Falling;

        public override void OnEnter(PlayerLocomotion playerLocomotion) { }

        protected override bool SwitchMotion(PlayerLocomotion playerLocomotion)
        {
            if (!playerLocomotion.CharacterController.isGrounded) return false;
            playerLocomotion.SetLocomotionState(new IdleMotion());
            return true;

        }

        public override void OnExit(PlayerLocomotion playerLocomotion) { }
        public override void OnFixedUpdate(PlayerLocomotion playerLocomotion) { }
    }
}
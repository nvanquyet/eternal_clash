using _GAME.Scripts.Player.Enum;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion.States
{
    public class WalkingMotion : ALocomotionState
    {
        public override LocomotionState LocomotionState => LocomotionState.Walking;

        public override void OnEnter(PlayerLocomotion playerLocomotion)
        {
        }


        public override void OnExit(PlayerLocomotion playerLocomotion)
        {
        }

        public override void OnFixedUpdate(PlayerLocomotion playerLocomotion)
        {
            Vector3 inputDirection = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
            if (inputDirection.magnitude == 0)
            {
                playerLocomotion.SetLocomotionState(new IdleMotion());
                return;
            }

            playerLocomotion.ApplyInputDirection(inputDirection.x,  inputDirection.z, GetSpeed(playerLocomotion));
        }

        protected virtual float GetSpeed(PlayerLocomotion playerLocomotion) => playerLocomotion.Config.WalkSpeed;

        protected override bool SwitchMotion(PlayerLocomotion playerLocomotion)
        {
            if (Input.GetKey(KeyCode.Space) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyUp(KeyCode.Space))
            {
                playerLocomotion.SetLocomotionState(new JumpingMotion());
                return true;
            }

            if (Input.GetKey(KeyCode.LeftShift))
            {
                playerLocomotion.SetLocomotionState(new RunningMotion());
                return true;
            }

            return false;
        }
    }
}
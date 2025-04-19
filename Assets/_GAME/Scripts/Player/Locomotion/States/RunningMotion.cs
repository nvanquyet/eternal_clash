using _GAME.Scripts.Player.Enum;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion.States
{
    public class RunningMotion : WalkingMotion
    {
        public override LocomotionState LocomotionState => LocomotionState.Running;
        
        protected override float GetSpeed(PlayerLocomotion playerLocomotion) => playerLocomotion.Config.RunSpeed;

        protected override bool SwitchMotion(PlayerLocomotion playerLocomotion)
        {
            if (Input.GetKey(KeyCode.Space) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyUp(KeyCode.Space))
            {
                playerLocomotion.SetLocomotionState(new JumpingMotion());
                return true;
            }
            if (Input.GetKeyUp(KeyCode.LeftShift))
            {
                playerLocomotion.SetLocomotionState(new WalkingMotion());
                return true;
            }

            return false;
        }
    }
}

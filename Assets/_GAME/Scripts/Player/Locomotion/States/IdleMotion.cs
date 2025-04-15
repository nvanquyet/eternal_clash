using _GAME.Scripts.Player.Enum;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion.States
{
    public class IdleMotion : ALocomotionState
    {
        public override LocomotionState LocomotionState => LocomotionState.Idle;

        public override void OnEnter(PlayerLocomotion playerLocomotion)
        {
            
        }

        public override void OnUpdate(PlayerLocomotion playerLocomotion)
        {
        }

        public override void OnExit(PlayerLocomotion playerLocomotion)
        {
        }

        public override void OnFixedUpdate(PlayerLocomotion playerLocomotion)
        {
            if (Input.GetKey(KeyCode.Space) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyUp(KeyCode.Space))
            {
                playerLocomotion.SetLocomotionState(new JumpingMotion());
                return;
            }

            Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
            if (move.magnitude != 0)
            {
                if (Input.GetKey(KeyCode.LeftShift))
                {
                    playerLocomotion.SetLocomotionState(new RunningMotion());
                    return;
                }
                playerLocomotion.SetLocomotionState(new WalkingMotion());
                return;
            }

            playerLocomotion.ApplyHorizontalVelocity(0, 0);
        }
    }
}
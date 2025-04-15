using _GAME.Scripts.Player.Enum;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion.States
{
    public class WalkingMotion : ALocomotionState
    {
        public override LocomotionState LocomotionState => LocomotionState.Walking;

        public override void OnEnter(PlayerLocomotion playerLocomotion) { }

        public override void OnUpdate(PlayerLocomotion playerLocomotion) { }

        public override void OnExit(PlayerLocomotion playerLocomotion) { }

        public override void OnFixedUpdate(PlayerLocomotion playerLocomotion)
        {
           
            if (Input.GetKey(KeyCode.Space) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyUp(KeyCode.Space))
            {
                playerLocomotion.SetLocomotionState(new JumpingMotion());
                return;
            }
            if (Input.GetKey(KeyCode.LeftShift))
            {
                playerLocomotion.SetLocomotionState(new RunningMotion());
                return;
            }
            Vector3 inputDirection = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
            if (inputDirection.magnitude == 0)
            {
                playerLocomotion.SetLocomotionState(new IdleMotion());
                return;
            }
            
            // Convert input direction to world space
            inputDirection = playerLocomotion.transform.TransformDirection(inputDirection);

            // Update horizontal velocity
            playerLocomotion.ApplyHorizontalVelocity(
                inputDirection.x * playerLocomotion.Config.WalkSpeed,
                inputDirection.z * playerLocomotion.Config.WalkSpeed
            );
        }
    }
}


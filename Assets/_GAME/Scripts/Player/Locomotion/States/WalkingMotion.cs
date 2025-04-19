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
            if (SwitchMotion(playerLocomotion)) return;
            Vector3 inputDirection = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
            if (inputDirection.magnitude == 0)
            {
                playerLocomotion.SetLocomotionState(new IdleMotion());
                return;
            }

            if (Camera.main != null)
            {
                var cam = Camera.main.transform;
                Vector3 moveDir = Vector3.Normalize(cam.forward * inputDirection.z + cam.right * inputDirection.x);
                moveDir.y = 0;
                // Update horizontal velocity
                playerLocomotion.ApplyHorizontalVelocity(
                    moveDir.x * GetSpeed(playerLocomotion),
                    moveDir.z * GetSpeed(playerLocomotion)
                );
                
                //Check rotation
                if (moveDir != Vector3.zero)
                {
                    Quaternion toRot = Quaternion.LookRotation(moveDir);
                    playerLocomotion.transform.rotation = Quaternion.Slerp(playerLocomotion.transform.rotation, toRot, 10f * Time.deltaTime);
                }
            }
            
        }
        protected virtual float GetSpeed(PlayerLocomotion playerLocomotion) => playerLocomotion.Config.WalkSpeed;

        protected virtual bool SwitchMotion(PlayerLocomotion playerLocomotion)
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


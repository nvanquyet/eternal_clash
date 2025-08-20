using _GAME.Scripts.Player.Enum;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion
{
    public abstract class ALocomotionState
    {
        public abstract LocomotionState LocomotionState { get; }
        
        public virtual void OnEnter(PlayerLocomotion locomotion) { }
        public virtual void OnExit(PlayerLocomotion locomotion) { }
        public virtual void ProcessInput(PlayerInputData input, PlayerLocomotion locomotion) { }
        public virtual void ProcessInput(PlayerInputData input, PlayerLocomotion locomotion, Vector3 forward, Vector3 right) { }
        public abstract void OnFixedUpdate(PlayerInputData input, PlayerLocomotion locomotion);
        
        protected void TransitionTo(PlayerLocomotion locomotion, ALocomotionState newState)
        {
            locomotion.SetState(newState);
        }
    }
}
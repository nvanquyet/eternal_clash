using _GAME.Scripts.Player.Enum;

namespace _GAME.Scripts.Player.Locomotion
{
    public abstract class ALocomotionState
    {
        public abstract LocomotionState LocomotionState { get; }
        public abstract void OnEnter(PlayerLocomotion playerLocomotion);

        
        public abstract void OnFixedUpdate(PlayerLocomotion playerLocomotion);
        public abstract void OnExit(PlayerLocomotion playerLocomotion);
        
        public virtual void OnUpdate(PlayerLocomotion playerLocomotion)
        {
            SwitchMotion(playerLocomotion);
        }

        protected abstract bool SwitchMotion(PlayerLocomotion playerLocomotion);
    }
}
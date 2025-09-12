namespace _GAME.Scripts.DesignPattern.Interaction
{
        /// <summary>
        /// States an interactable entity can be in
        /// </summary>
        public enum InteractionState
        {
            Enable,
            Disabled
        }
        /// <summary>
       /// Types of damage that can be dealt
       /// </summary>
       public enum DamageType
       {
           Physical,
           Magical,
           Fire,
           Ice,
           Lightning,
           Poison,
           True // Ignores all defenses
       }
}
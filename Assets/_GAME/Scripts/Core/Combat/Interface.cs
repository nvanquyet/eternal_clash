using UnityEngine;

namespace _GAME.Scripts.Core.Combat
{
    public interface IDamageStrategy
    {
        float Calculate(float baseDamage, float defense);
    }

    public class PhysicalDamageStrategy : IDamageStrategy
    {
        public float Calculate(float baseDamage, float defense)
        {
            return Mathf.Max(1f, baseDamage - defense);
        }
    }

    public class TrueDamageStrategy : IDamageStrategy
    {
        public float Calculate(float baseDamage, float defense)
        {
            return baseDamage;
        }
    }

    public class MagicDamageStrategy : IDamageStrategy
    {
        public float Calculate(float baseDamage, float defense)
        {
            // Magic damage reduces defense effectiveness by 50%
            return Mathf.Max(1f, baseDamage - (defense * 0.5f));
        }
    }
}
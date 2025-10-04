using _GAME.Scripts.Core.Components;
using UnityEngine;

namespace _GAME.Scripts.Core.Combat
{
    /// <summary>
    /// Component that provides defense stats
    /// Works with existing HealthComponent
    /// </summary>
    public class DefenseComponent : MonoBehaviour, IPlayerComponent
    {
        [Header("Defense Settings")]
        [SerializeField] private float baseDefense = 0f;
        [SerializeField] private float damageReduction = 0f; // 0-1 range

        private IPlayer _owner;
        public bool IsActive => enabled;

        public float DefenseValue => baseDefense;
        public float DamageReduction => Mathf.Clamp01(damageReduction);

        public void Initialize(IPlayer owner)
        {
            _owner = owner;
        }

        public void OnNetworkSpawn() { }
        public void OnNetworkDespawn() { }

        public void AddTemporaryDefense(float amount, float duration)
        {
            StartCoroutine(TemporaryDefenseCoroutine(amount, duration));
        }

        private System.Collections.IEnumerator TemporaryDefenseCoroutine(float amount, float duration)
        {
            baseDefense += amount;
            yield return new WaitForSeconds(duration);
            baseDefense = Mathf.Max(0f, baseDefense - amount);
        }
    }
}
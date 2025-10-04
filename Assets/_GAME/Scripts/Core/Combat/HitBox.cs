using _GAME.Scripts.Core.Player;
using _GAME.Scripts.DesignPattern.Interaction;
using UnityEngine;

namespace _GAME.Scripts.Core.Combat
{
     /// <summary>
    /// Simplified hitbox - just forwards damage with modifiers
    /// No complex inheritance, no IDefendable implementation
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HitBox : MonoBehaviour
    {
        [SerializeField] private HitBoxData hitBoxData = HitBoxData.Default;
        
        private ModularPlayer _owner;
        private Collider _collider;

        public HitBoxType Type => hitBoxData.type;
        public float DamageMultiplier => hitBoxData.damageMultiplier;
        public float ArmorValue => hitBoxData.armorValue;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _owner = GetComponentInParent<ModularPlayer>();

            if (_owner == null)
            {
                Debug.LogError($"[HitBox] {name}: No ModularPlayer found in parent!");
            }
        }

        /// <summary>
        /// Called by projectile/melee weapon when hit
        /// </summary>
        public void OnHit(ulong attackerId, float baseDamage, DamageType damageType)
        {
            if (_owner == null || !_owner.IsAlive()) return;

            // Calculate final damage with hitbox modifiers
            float modifiedDamage = baseDamage * hitBoxData.damageMultiplier;
            
            // Apply armor reduction (only for physical damage)
            if (damageType == DamageType.Physical)
            {
                modifiedDamage = Mathf.Max(1f, modifiedDamage - hitBoxData.armorValue);
            }

            // Forward to combat system
            var combatSystem = FindObjectOfType<CombatSystem>();
            if (combatSystem != null)
            {
                combatSystem.ApplyDamage(attackerId, _owner.ClientId, modifiedDamage, damageType);
            }
            else
            {
                // Fallback: direct damage
                _owner.Health?.TakeDamage(modifiedDamage);
            }

            // Visual feedback
            ShowHitEffect(modifiedDamage, hitBoxData.type);
        }

        private void ShowHitEffect(float damage, HitBoxType type)
        {
            // Implement visual feedback based on hitbox type
            switch (type)
            {
                case HitBoxType.Head:
                    Debug.Log($"[HitBox] HEADSHOT! {damage} damage");
                    break;
                case HitBoxType.Critical:
                    Debug.Log($"[HitBox] CRITICAL HIT! {damage} damage");
                    break;
                default:
                    Debug.Log($"[HitBox] Hit for {damage} damage");
                    break;
            }
        }

        public void SetHitBoxData(HitBoxData data)
        {
            hitBoxData = data;
        }

        #if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = GetGizmoColor();
            if (_collider != null && _collider is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);
            }
        }

        private Color GetGizmoColor()
        {
            return hitBoxData.type switch
            {
                HitBoxType.Head => Color.red,
                HitBoxType.Critical => Color.yellow,
                HitBoxType.Limb => Color.blue,
                _ => Color.green
            };
        }
        #endif
    }
}
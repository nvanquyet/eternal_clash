using System;
using _GAME.Scripts.DesignPattern.Interaction;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player.HitBox
{
    /// <summary>
    /// Generic hitBox info - có thể define cho bất kỳ object nào
    /// </summary>
    [System.Serializable]
    public struct HitBoxInfo
    {
        [Header("Identity")] 
        public HitBoxType hitBoxType; 
        public HitBoxCategory category; 
        public string customId;

        [Header("Damage Settings")] public float damageMultiplier;
        public float armorValue;
        public bool isPenetrable;

        [Header("Effects")] public bool hasSpecialEffect;
        public string specialEffectId; 

        public string HitBoxId => customId ?? hitBoxType.ToString();
        public HitBoxCategory Category => category;
        public HitBoxType HitBoxType => hitBoxType;
    }

    /// <summary>
    /// Struct chứa thông tin khi hitBox bị hit
    /// </summary>
    [Serializable]
    public struct HitBoxDamageInfo
    {
        public HitBoxCategory hitBoxCategory;
        public float originalDamage;
        public float finalDamage;
        public DamageType damageType;
        public bool hadSpecialEffect;
        public string specialEffectId;

        public HitBoxDamageInfo(HitBoxCategory category, float origDmg, float finalDmg, DamageType type,
            bool special = false, string effectId = "")
        {
            hitBoxCategory = category;
            originalDamage = origDmg;
            finalDamage = finalDmg;
            damageType = type;
            hadSpecialEffect = special;
            specialEffectId = effectId;
        }
    }
    public class ModularHitBox : ADefendable
    {
        [Header("HitBox Settings")] 
        [SerializeField] private HitBoxInfo hitBoxInfo;
        
        [SerializeField] private bool showDebugGizmos = true;
        [SerializeField] private Color debugColor = Color.red;
        private ModularRootHitBox rootModule;

        public ModularRootHitBox RootModule
        {
            get
            {
                if(rootModule == null)
                    rootModule = GetComponentInParent<ModularRootHitBox>();
                return rootModule;
            }
        }
        

        // Events
        public event Action<ModularHitBox, HitBoxDamageInfo> OnHitBoxDamaged;

        // Properties
        public HitBoxType HitBoxType => hitBoxInfo.hitBoxType;
        public string HitBoxId => hitBoxInfo.HitBoxId;
        public HitBoxCategory Category => hitBoxInfo.Category;

        #region Unity Lifecycle
        
        protected override void Start()
        {
            ValidateSetup();
            base.Start();
        }
        

        public override float TakeDamage(IAttackable attacker, float damage,
            DamageType damageType = DamageType.Physical)
        {
            if (RootModule == null || !IsAlive)
                return 0f;

            // Forward to root module
            var actualDamage = ProcessHit(attacker, damage, damageType);
            return RootModule.TakeDamage(attacker, actualDamage, damageType);
        }

        public override bool Interact(IInteractable target) => false;

        public override void OnInteracted(IInteractable initiator)
        {
        }

        #endregion

        #region Setup
        
        private void ValidateSetup()
        {
            if (hitBoxInfo.HitBoxType == HitBoxType.None)
            {
                Debug.LogWarning($"[ModularHitBox] Empty hitBox type on {name}");
            }

            if (hitBoxInfo.damageMultiplier <= 0)
            {
                Debug.LogWarning($"[ModularHitBox] Invalid damage multiplier on {name}");
                hitBoxInfo.damageMultiplier = 1f;
            }
        }

        #endregion

        #region Damage Handling

        /// <summary>
        /// Called by projectiles or other attackers when they hit this hitBox
        /// </summary>
        private float ProcessHit(IAttackable attacker, float damage, DamageType damageType)
        {
            if (!IsAlive || RootModule == null)
                return 0f;

            // Calculate final damage
            var armorReduction = Mathf.Max(0, hitBoxInfo.armorValue);
            var damageAfterArmor = Mathf.Max(1f, damage - armorReduction);
            var finalDamage = damageAfterArmor * hitBoxInfo.damageMultiplier;

            // Create damage info
            var damageInfo = new HitBoxDamageInfo(
                hitBoxInfo.Category,
                damage,
                finalDamage,
                damageType,
                hitBoxInfo.hasSpecialEffect,
                hitBoxInfo.specialEffectId
            );

            // Fire local event
            OnHitBoxDamaged?.Invoke(this, damageInfo);
            return finalDamage;
        }

        #endregion

        #region Debug

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos || InteractionCollider == null) return;

            Gizmos.color = IsAlive ? debugColor : Color.gray;

            if (InteractionCollider is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else if (InteractionCollider is SphereCollider sphere)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
            else if (InteractionCollider is CapsuleCollider capsule)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                // Simplified capsule visualization
                Gizmos.DrawWireSphere(Vector3.up * capsule.height * 0.5f, capsule.radius);
                Gizmos.DrawWireSphere(Vector3.down * capsule.height * 0.5f, capsule.radius);
            }
        }

        #endregion
    }
}
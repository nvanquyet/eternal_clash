using System;
using _GAME.Scripts.DesignPattern.Interaction;
using JetBrains.Annotations;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player.HitBox
{
    /// <summary>
    /// Generic hitBox info - can be defined for any object
    /// </summary>
    [System.Serializable]
    public struct HitBoxInfo
    {
        [Header("Identity")] 
        public HitBoxType hitBoxType; 
        public HitBoxCategory category; 
        public string customId;

        [Header("Damage Settings")] 
        public float damageMultiplier;
        public float armorValue;
        public bool isPenetrable;

        [Header("Effects")] 
        public bool hasSpecialEffect;
        public string specialEffectId; 

        public string HitBoxId => string.IsNullOrEmpty(customId) ? hitBoxType.ToString() : customId;
        public HitBoxCategory Category => category;
        public HitBoxType HitBoxType => hitBoxType;
    }

    /// <summary>
    /// Struct containing information when hitBox is hit
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

    /// <summary>
    /// Simple MonoBehaviour hitbox that forwards damage to ModularRootHitBox
    /// Implements IDefendable to receive damage, but forwards all operations to root
    /// Does NOT inherit from network classes - purely local damage receiver
    /// </summary>
    public class ModularHitBox : MonoBehaviour, IDefendable
    {
        [Header("HitBox Settings")] 
        [SerializeField] private HitBoxInfo hitBoxInfo = new HitBoxInfo
        {
            hitBoxType = HitBoxType.None,
            category = HitBoxCategory.BodyPart,
            damageMultiplier = 1f,
            armorValue = 0f,
            isPenetrable = true,
            hasSpecialEffect = false
        };
        
        [Header("Debug")] 
        [SerializeField] private bool showDebugGizmos = true;
        [SerializeField] private Color debugColor = Color.red;
        
        private ModularRootHitBox rootModule;
        private Collider hitboxCollider;

        // Events - forward from root module
        public event Action<float, float> OnHealthChanged
        {
            add => RootModule.OnHealthChanged += value;
            remove => RootModule.OnHealthChanged -= value;
        }
        
        public event Action<IDefendable, IAttackable> OnDied
        {
            add => RootModule.OnDied += value;
            remove => RootModule.OnDied -= value;
        }

        // Local hitbox-specific event
        public event Action<ModularHitBox, HitBoxDamageInfo> OnHitBoxDamaged;

        #region IInteractable Implementation

        public string EntityId => $"{name}_{GetInstanceID()}";
        
        public bool CanInteract => RootModule != null && RootModule.CanInteract;
        
        public bool IsActive 
        { 
            get => gameObject.activeInHierarchy && (RootModule?.IsActive ?? false);
            set => gameObject.SetActive(value);
        }
        
        public Vector3 Position => transform.position;
        
        public Collider InteractionCollider
        {
            get
            {
                if (hitboxCollider != null) return hitboxCollider;
                hitboxCollider = GetComponent<Collider>();
                if (hitboxCollider == null)
                    Debug.LogWarning($"[ModularHitBox] No collider found on {name}");
                return hitboxCollider;
            }
        }

        /// <summary>
        /// HitBoxes don't actively interact - they only receive interactions
        /// </summary>
        public bool Interact(IInteractable target) => false;

        /// <summary>
        /// When something interacts with this hitbox
        /// </summary>
        public void OnInteracted(IInteractable initiator)
        {
            // Forward to root if needed
            RootModule?.OnInteracted(initiator);
        }

        #endregion

        #region IDefendable Implementation

        public float CurrentHealth => RootModule?.CurrentHealth ?? 0f;
        public float MaxHealth => RootModule?.MaxHealth ?? 0f;
        public float DefenseValue => RootModule?.DefenseValue ?? 0f + hitBoxInfo.armorValue;
        public bool IsAlive => RootModule != null && RootModule.IsAlive;
        public bool IsInvulnerable => RootModule?.IsInvulnerable ?? false;

        /// <summary>
        /// Main damage entry point - applies hitbox-specific calculations then forwards to root
        /// </summary>
        public float TakeDamage(IAttackable attacker, float damage, DamageType damageType = DamageType.Physical)
        {
            if (RootModule == null || !IsAlive)
                return 0f;

            // Calculate hitbox-specific damage modifications
            var processedDamage = ProcessHitBoxDamage(damage, damageType);
            
            // Forward to root module for network synchronization
            var actualDamage = RootModule.TakeDamage(attacker, processedDamage, damageType);
            
            // Fire local hitbox event
            var damageInfo = new HitBoxDamageInfo(
                hitBoxInfo.Category,
                damage,
                actualDamage,
                damageType,
                hitBoxInfo.hasSpecialEffect,
                hitBoxInfo.specialEffectId
            );
            
            OnHitBoxDamaged?.Invoke(this, damageInfo);
            
            return actualDamage;
        }

        /// <summary>
        /// Forward heal to root module
        /// </summary>
        public float Heal(float amount)
        {
            return RootModule?.Heal(amount) ?? 0f;
        }

        /// <summary>
        /// Forward death handling to root module
        /// </summary>
        public void OnDeath(IAttackable killer = null)
        {
            RootModule?.OnDeath(killer);
        }

        #endregion

        #region Properties

        [CanBeNull]
        private ModularRootHitBox RootModule
        {
            get
            {
                if (rootModule == null)
                    rootModule = GetComponentInParent<ModularRootHitBox>();
                return rootModule;
            }
        }

        public HitBoxType HitBoxType => hitBoxInfo.hitBoxType;
        public string HitBoxId => hitBoxInfo.HitBoxId;
        public HitBoxCategory Category => hitBoxInfo.Category;

        #endregion

        #region Unity Lifecycle
        
        private void Start()
        {
            GameEvent.OnRoleAssignedSuccess += ValidateSetup;
        }
        
        private void OnDestroy()
        {
            GameEvent.OnRoleAssignedSuccess -= ValidateSetup;
        }

        private void ValidateSetup()
        {
            if (hitBoxInfo.HitBoxType == HitBoxType.None)
            {
                Debug.LogWarning($"[ModularHitBox] Empty hitBox type on {name}");
            }

            if (hitBoxInfo.damageMultiplier <= 0)
            {
                Debug.LogWarning($"[ModularHitBox] Invalid damage multiplier on {name}, setting to 1.0");
                hitBoxInfo.damageMultiplier = 1f;
            }

            if (RootModule == null)
            {
                Debug.LogWarning($"[ModularHitBox] No ModularRootHitBox found in parents of {name}!");
            }

            if (InteractionCollider == null)
            {
                Debug.LogError($"[ModularHitBox] No collider found on {name}!");
            }
        }

        #endregion

        #region Damage Processing

        /// <summary>
        /// Apply hitbox-specific damage calculations before forwarding to root
        /// </summary>
        private float ProcessHitBoxDamage(float baseDamage, DamageType damageType)
        {
            if (damageType == DamageType.True) 
                return baseDamage * hitBoxInfo.damageMultiplier;

            // Apply armor reduction first
            var damageAfterArmor = Mathf.Max(1f, baseDamage - hitBoxInfo.armorValue);
            
            // Apply damage multiplier
            var finalDamage = damageAfterArmor * hitBoxInfo.damageMultiplier;
            
            return finalDamage;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Get hitbox info (read-only)
        /// </summary>
        public HitBoxInfo GetHitBoxInfo() => hitBoxInfo;

        /// <summary>
        /// Update hitbox info at runtime
        /// </summary>
        public void UpdateHitBoxInfo(HitBoxInfo newInfo)
        {
            hitBoxInfo = newInfo;
            ValidateSetup();
        }

        #endregion

        #region Debug Visualization

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos || InteractionCollider == null) return;

            // Change color based on state
            Color gizmoColor = debugColor;
            if (!IsAlive) 
                gizmoColor = Color.gray;
            else if (!CanInteract) 
                gizmoColor = Color.yellow;

            Gizmos.color = gizmoColor;
            DrawColliderGizmo();
        }

        private void DrawColliderGizmo()
        {
            var col = InteractionCollider;
            if (col == null) return;

            Gizmos.matrix = transform.localToWorldMatrix;

            switch (col)
            {
                case BoxCollider box:
                    Gizmos.DrawWireCube(box.center, box.size);
                    break;
                    
                case SphereCollider sphere:
                    Gizmos.DrawWireSphere(sphere.center, sphere.radius);
                    break;
                    
                case CapsuleCollider capsule:
                    // Simplified capsule visualization
                    Vector3 topCenter = Vector3.up * (capsule.height * 0.5f - capsule.radius);
                    Vector3 bottomCenter = Vector3.down * (capsule.height * 0.5f - capsule.radius);
                    Gizmos.DrawWireSphere(topCenter + capsule.center, capsule.radius);
                    Gizmos.DrawWireSphere(bottomCenter + capsule.center, capsule.radius);
                    break;
                    
                case MeshCollider mesh when mesh.convex:
                    if (mesh.sharedMesh != null)
                        Gizmos.DrawWireMesh(mesh.sharedMesh);
                    break;
            }
        }

        #endregion
    }
}
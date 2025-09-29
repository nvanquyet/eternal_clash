// ModularHitBox.cs
using System;
using _GAME.Scripts.DesignPattern.Interaction;
using JetBrains.Annotations;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player.HitBox
{
    /// <summary>
    /// MonoBehaviour hitbox cục bộ: nhận va chạm, xử lý multiplier/armor theo bộ phận
    /// rồi forward damage lên ModularRootHitBox (ADefendable) để server sync HP.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class ModularHitBox : MonoBehaviour, IDefendable
    {
        [Serializable]
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
        [SerializeField] private Collider _hitboxCollider;

        private ModularRootHitBox _rootModule;

        // Bubble các event từ root để UI có thể sub trực tiếp ở child khi cần
        public event Action<float, float> OnHealthChanged
        {
            add { if (RootModule != null) RootModule.OnHealthChanged += value; }
            remove { if (RootModule != null) RootModule.OnHealthChanged -= value; }
        }

        public event Action<IDefendable, IAttackable> OnDied
        {
            add { if (RootModule != null) RootModule.OnDied += value; }
            remove { if (RootModule != null) RootModule.OnDied -= value; }
        }

        // Sự kiện riêng cho từng hitbox (để thể hiện FX khác nhau theo bộ phận)
        public event Action<ModularHitBox, HitBoxDamageInfo> OnHitBoxDamaged;

        #region IInteractable

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
                if (_hitboxCollider == null)
                    _hitboxCollider = GetComponent<Collider>();
                return _hitboxCollider;
            }
        }

        public bool Interact(IInteractable target) => false;

        public void OnInteracted(IInteractable initiator)
        {
            RootModule?.OnInteracted(initiator);
        }

        #endregion

        #region IDefendable

        public float CurrentHealth => RootModule?.CurrentHealth ?? 0f;
        public float MaxHealth => RootModule?.MaxHealth ?? 0f;

        // FIX: tổng armor = armor root + armor cục bộ của hitbox
        public float DefenseValue => (RootModule?.DefenseValue ?? 0f) + hitBoxInfo.armorValue;

        public bool IsAlive => RootModule != null && RootModule.IsAlive;
        public bool IsInvulnerable => RootModule?.IsInvulnerable ?? false;

        public float TakeDamage(IAttackable attacker, float damage, DamageType damageType = DamageType.Physical)
        {
            // Bảo đảm chỉ server trừ máu (phòng client-side misuse)
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                return 0f;

            if (RootModule == null || !IsAlive)
                return 0f;

            // Xử lý theo bộ phận (armor/multiplier cục bộ)
            var processedDamage = ProcessHitBoxDamage(damage, damageType);

            // Forward lên root để sync HP qua NetVar + bắn ClientRpc FX
            var actualDamage = RootModule.TakeDamage(attacker, processedDamage, damageType);

            // Notify per-hitbox (ví dụ headshot popup...)
            var info = new HitBoxDamageInfo(
                hitBoxInfo.Category,
                damage,
                actualDamage,
                damageType,
                hitBoxInfo.hasSpecialEffect,
                hitBoxInfo.specialEffectId
            );
            OnHitBoxDamaged?.Invoke(this, info);

            return actualDamage;
        }

        public float Heal(float amount) => RootModule?.Heal(amount) ?? 0f;

        public void OnDeath(IAttackable killer = null)
        {
            RootModule?.OnDeath(killer);
        }

        #endregion

        #region Data structs

        [Serializable]
        public struct HitBoxDamageInfo
        {
            public HitBoxCategory hitBoxCategory;
            public float originalDamage;
            public float finalDamage;
            public DamageType damageType;
            public bool hadSpecialEffect;
            public string specialEffectId;

            public HitBoxDamageInfo(HitBoxCategory category, float orig, float final, DamageType type,
                bool special = false, string effectId = "")
            {
                hitBoxCategory = category;
                originalDamage = orig;
                finalDamage = final;
                damageType = type;
                hadSpecialEffect = special;
                specialEffectId = effectId;
            }
        }

        #endregion

        #region Props/Cache

        [CanBeNull]
        private ModularRootHitBox RootModule
        {
            get
            {
                if (_rootModule == null)
                    _rootModule = transform.root.GetComponentInChildren<ModularRootHitBox>();
                return _rootModule;
            }
        }

        public HitBoxType HitBoxType => hitBoxInfo.hitBoxType;
        public string HitBoxId => hitBoxInfo.HitBoxId;
        public HitBoxCategory Category => hitBoxInfo.Category;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            GameEvent.OnRoleAssigned += ValidateSetup;
        }

        private void OnDestroy()
        {
            GameEvent.OnRoleAssigned -= ValidateSetup;
        }

        private void ValidateSetup()
        {
            if (hitBoxInfo.hitBoxType == HitBoxType.None)
                Debug.LogWarning($"[ModularHitBox] Empty HitBoxType on {name}");

            if (hitBoxInfo.damageMultiplier <= 0f)
            {
                Debug.LogWarning($"[ModularHitBox] Invalid damage multiplier on {name}, set to 1.0");
                hitBoxInfo.damageMultiplier = 1f;
            }

            if (RootModule == null)
                Debug.LogWarning($"[ModularHitBox] No ModularRootHitBox found in parents of {name}!");

            if (InteractionCollider == null)
                Debug.LogError($"[ModularHitBox] No Collider found on {name}!");
        }

        #endregion

        #region Damage helpers

        private float ProcessHitBoxDamage(float baseDamage, DamageType damageType)
        {
            if (damageType == DamageType.True)
                return baseDamage * Mathf.Max(0f, hitBoxInfo.damageMultiplier);

            // Armor cục bộ của bộ phận
            var afterArmor = Mathf.Max(1f, baseDamage - Mathf.Max(0f, hitBoxInfo.armorValue));

            // Multiplier cục bộ của bộ phận
            return afterArmor * Mathf.Max(0f, hitBoxInfo.damageMultiplier);
        }

        #endregion

        #region Public API

        public HitBoxInfo GetHitBoxInfo() => hitBoxInfo;

        public void UpdateHitBoxInfo(HitBoxInfo newInfo)
        {
            hitBoxInfo = newInfo;
            ValidateSetup();
        }

        #endregion
    }
}

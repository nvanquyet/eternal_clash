using System;
using UnityEngine;

namespace _GAME.Scripts.DesignPattern.Interaction
{
    #region Core Base Class

    /// <summary>
    /// Base abstract class for all interactable entities
    /// </summary>
    public abstract class InteractableBase : MonoBehaviour, IInteractable
    {
        [Header("Base Interactable Settings")] [SerializeField]
        protected string entityId;

        [SerializeField] protected bool isActive = true;
        [SerializeField] protected InteractionState currentState = InteractionState.Idle;

        public string EntityId => string.IsNullOrEmpty(entityId) ? gameObject.GetInstanceID().ToString() : entityId;

        public virtual bool CanInteract => isActive && currentState != InteractionState.Dead &&
                                           currentState != InteractionState.Disabled;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }

        public Vector3 Position => transform.position;
        public InteractionState CurrentState => currentState;

        protected virtual void Awake()
        {
            if (string.IsNullOrEmpty(entityId))
                entityId = $"{gameObject.name}_{GetInstanceID()}";
        }

        public abstract bool Interact(IInteractable target);
        public abstract void OnInteracted(IInteractable initiator);

        protected virtual void SetState(InteractionState newState)
        {
            if (currentState == newState) return;
            var previousState = currentState;
            currentState = newState;
            OnStateChanged(previousState, newState);
        }

        protected abstract void OnStateChanged(InteractionState previousState, InteractionState newState);
    }

    #endregion

    #region Trigger Interaction System

    /// <summary>
    /// Base class for trigger-based interactions (doors, chests, NPCs, etc.)
    /// </summary>
    public abstract class ATriggerInteractable : InteractableBase
    {
        [Header("Trigger Settings")] [SerializeField]
        protected LayerMask interactionLayer = -1;

        [SerializeField] protected string[] interactionTags = { "Player" };
        [SerializeField] protected bool requireKeyPress = true;
        [SerializeField] protected KeyCode interactionKey = KeyCode.E;
        [SerializeField] protected float interactionCooldown = 0.5f;

        protected IInteractable currentInteractor;
        protected float lastInteractionTime;

        public bool RequireKeyPress => requireKeyPress;
        public KeyCode InteractionKey => interactionKey;

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (!CanInteract || !IsValidInteractor(other.gameObject)) return;

            if (other.TryGetComponent<IInteractable>(out var interactor))
            {
                currentInteractor = interactor;
                OnInteractorEntered(interactor);
            }
        }

        protected virtual void OnTriggerStay(Collider other)
        {
            if (!CanInteract || currentInteractor == null) return;

            if (CanPerformInteraction())
                PerformInteraction();
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            if (currentInteractor != null && other.GetComponent<IInteractable>() == currentInteractor)
            {
                OnInteractorExited(currentInteractor);
                currentInteractor = null;
            }
        }

        private bool CanPerformInteraction()
        {
            bool cooldownReady = Time.time > lastInteractionTime + interactionCooldown;
            return cooldownReady && (!requireKeyPress || Input.GetKeyDown(interactionKey));
        }

        protected virtual bool IsValidInteractor(GameObject obj)
        {
            // Check layer
            if (((1 << obj.layer) & interactionLayer) == 0) return false;

            // Check tags
            if (interactionTags.Length > 0)
            {
                foreach (string tag in interactionTags)
                {
                    if (obj.CompareTag(tag)) return true;
                }

                return false;
            }

            return true;
        }

        protected virtual void PerformInteraction()
        {
            if (currentInteractor?.Interact(this) == true)
            {
                lastInteractionTime = Time.time;
                OnInteracted(currentInteractor);
            }
        }

        protected abstract void OnInteractorEntered(IInteractable interactor);
        protected abstract void OnInteractorExited(IInteractable interactor);
    }

    #endregion

    #region Combat System Base Classes

    /// <summary>
    /// Base class for attackable entities with collision/trigger support
    /// </summary>
    public abstract class AAttackable : InteractableBase, IAttackable
    {
        [Header("Attack Settings")] [SerializeField]
        protected float baseDamage = 10f;

        [SerializeField] protected float attackRange = 2f;
        [SerializeField] protected float attackCooldown = 1f;
        [SerializeField] protected DamageType primaryDamageType = DamageType.Physical;

        [Header("Collision Attack Settings")] [SerializeField]
        protected bool enableCollisionAttack = false;

        [SerializeField] protected LayerMask attackableLayers = -1;
        [SerializeField] protected string[] attackableTags = { "Enemy", "Destructible" };
        [SerializeField] protected bool destroyAfterCollisionAttack = true;

        private float nextAttackTime;
        private bool hasCollisionAttacked;

        public float BaseDamage => baseDamage;
        public float AttackRange => attackRange;
        public float AttackCooldown => attackCooldown;

        public bool CanAttack => CanInteract && Time.time >= nextAttackTime &&
                                 currentState != InteractionState.Attacking && currentState != InteractionState.Dead;

        public float NextAttackTime => nextAttackTime;
        public DamageType PrimaryDamageType => primaryDamageType;

        public event Action<IAttackable, IDefendable, float> OnAttackPerformed;

        #region Collision Attack System

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (enableCollisionAttack && !hasCollisionAttacked && CanAttack)
                ProcessCollisionAttack(other);
        }

        protected virtual void OnCollisionEnter(Collision collision)
        {
            if (enableCollisionAttack && !hasCollisionAttacked && CanAttack)
                ProcessCollisionAttack(collision.collider);
        }

        private void ProcessCollisionAttack(Collider other)
        {
            if (!IsValidTarget(other.gameObject))
            {
                OnHitInvalidTarget(other);
                return;
            }

            if (other.TryGetComponent<IDefendable>(out var target))
                PerformCollisionAttack(target);
            else
                OnHitNonDefendableTarget(other);
        }

        protected virtual bool IsValidTarget(GameObject target)
        {
            // Check layer
            if (((1 << target.layer) & attackableLayers) == 0) return false;

            // Check tags
            if (attackableTags.Length > 0)
            {
                foreach (string tag in attackableTags)
                {
                    if (target.CompareTag(tag)) return true;
                }

                return false;
            }

            return true;
        }

        private void PerformCollisionAttack(IDefendable target)
        {
            if (target?.IsAlive != true) return;

            hasCollisionAttacked = true;
            SetState(InteractionState.Attacking);

            float damage = CalculateDamage(target);
            float actualDamage = target.TakeDamage(this, damage, primaryDamageType);

            nextAttackTime = Time.time + attackCooldown;

            OnAttackPerformed?.Invoke(this, target, actualDamage);
            OnSuccessfulCollisionAttack(target, actualDamage);

            if (destroyAfterCollisionAttack)
            {
                SetState(InteractionState.Dead);
                HandleDestruction();
            }
            else
            {
                SetState(InteractionState.Idle);
            }
        }

        #endregion

        #region Manual Attack System

        public virtual bool Attack(IDefendable target)
        {
            if (!CanAttack || target?.IsAlive != true || !IsInAttackRange(target))
                return false;

            return PerformAttack(target);
        }

        protected virtual bool PerformAttack(IDefendable target)
        {
            SetState(InteractionState.Attacking);

            float damage = CalculateDamage(target);
            float actualDamage = target.TakeDamage(this, damage, primaryDamageType);

            nextAttackTime = Time.time + attackCooldown;
            OnAttackPerformed?.Invoke(this, target, actualDamage);

            SetState(InteractionState.Idle);
            return actualDamage > 0;
        }

        public virtual bool IsInAttackRange(IDefendable target) =>
            target != null && Vector3.Distance(Position, target.Position) <= attackRange;

        public virtual float CalculateDamage(IDefendable target) => baseDamage;

        #endregion

        #region Abstract Methods

        protected abstract void OnSuccessfulCollisionAttack(IDefendable target, float actualDamage);
        protected abstract void OnHitInvalidTarget(Collider other);
        protected abstract void OnHitNonDefendableTarget(Collider other);
        protected abstract void HandleDestruction();

        #endregion
    }

    /// <summary>
    /// Base class for defendable entities
    /// </summary>
    public abstract class ADefendable : InteractableBase, IDefendable
    {
        [Header("Defense Settings")] [SerializeField]
        protected float maxHealth = 100f;

        [SerializeField] protected float currentHealth;
        [SerializeField] protected float defenseValue = 0f;
        [SerializeField] protected bool isInvulnerable = false;

        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public float DefenseValue => defenseValue;
        public bool IsAlive => currentHealth > 0;
        public bool IsInvulnerable => isInvulnerable;

        public event Action<float, float> OnHealthChanged;
        public event Action<IDefendable, IAttackable> OnDied;

        protected override void Awake()
        {
            base.Awake();
            if (currentHealth <= 0) currentHealth = maxHealth;
        }

        public virtual float TakeDamage(IAttackable attacker, float damage, DamageType damageType = DamageType.Physical)
        {
            if (!IsAlive || IsInvulnerable) return 0f;

            float finalDamage = CalculateFinalDamage(damage, damageType);
            currentHealth = Mathf.Max(0, currentHealth - finalDamage);

            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            if (!IsAlive)
            {
                SetState(InteractionState.Dead);
                OnDeath(attacker);
            }

            return finalDamage;
        }

        public virtual float Heal(float amount)
        {
            if (!IsAlive) return 0f;

            float previousHealth = currentHealth;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            float actualHealed = currentHealth - previousHealth;

            if (actualHealed > 0)
                OnHealthChanged?.Invoke(currentHealth, maxHealth);

            return actualHealed;
        }

        public virtual void OnDeath(IAttackable killer = null)
        {
            SetState(InteractionState.Dead);
            OnDied?.Invoke(this, killer);
        }

        protected virtual float CalculateFinalDamage(float baseDamage, DamageType damageType) =>
            damageType == DamageType.True ? baseDamage : Mathf.Max(1f, baseDamage - defenseValue);
    }

    /// <summary>
    /// Base class for entities that can both attack and defend
    /// </summary>
    public abstract class ACombatEntity : ADefendable, IAttackable
    {
        [Header("Combat Settings")] [SerializeField]
        protected float baseDamage = 10f;

        [SerializeField] protected float attackRange = 2f;
        [SerializeField] protected float attackCooldown = 1f;
        [SerializeField] protected DamageType primaryDamageType = DamageType.Physical;

        private float nextAttackTime;

        // IAttackable implementation
        public float BaseDamage => baseDamage;
        public float AttackRange => attackRange;
        public float AttackCooldown => attackCooldown;

        public bool CanAttack => CanInteract && Time.time >= nextAttackTime &&
                                 currentState != InteractionState.Attacking && IsAlive;

        public float NextAttackTime => nextAttackTime;
        public DamageType PrimaryDamageType => primaryDamageType;

        public event Action<IAttackable, IDefendable, float> OnAttackPerformed;

        public virtual bool Attack(IDefendable target)
        {
            if (!CanAttack || target?.IsAlive != true || !IsInAttackRange(target))
                return false;

            SetState(InteractionState.Attacking);

            float damage = CalculateDamage(target);
            float actualDamage = target.TakeDamage(this, damage, primaryDamageType);

            nextAttackTime = Time.time + attackCooldown;
            OnAttackPerformed?.Invoke(this, target, actualDamage);

            SetState(InteractionState.Idle);
            return actualDamage > 0;
        }

        public virtual bool IsInAttackRange(IDefendable target) =>
            target != null && Vector3.Distance(Position, target.Position) <= attackRange;

        public virtual float CalculateDamage(IDefendable target) => baseDamage;
    }

    #endregion
}
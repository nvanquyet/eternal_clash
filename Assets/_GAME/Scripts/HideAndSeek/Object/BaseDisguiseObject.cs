using System;
using System.Collections.Generic;
using System.Linq;
using _GAME.Scripts.DesignPattern.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Object
{
    public abstract class BaseDisguiseObject : ADefendable, IObjectDisguise
    {
        [Header("Disguise Object Settings")] [SerializeField]
        protected ObjectType objectType;

        [SerializeField] protected float objectMaxHealth = 10f;
        [SerializeField] protected Vector3 objectSize = Vector3.one;
        [SerializeField] protected bool canBeOccupied = true;

        [Header("Visual Settings")] [SerializeField]
        protected Material normalMaterial;

        [SerializeField] protected Material occupiedMaterial;
        [SerializeField] protected Material damagedMaterial;
        [SerializeField] protected ParticleSystem hitEffect;
        [SerializeField] protected ParticleSystem destructionEffect;

        // Network variables
        private NetworkVariable<bool> networkOccupied = new NetworkVariable<bool>(false);
        private NetworkVariable<ulong> occupyingHiderId = new NetworkVariable<ulong>(0);
        private NetworkVariable<float> networkHealth = new NetworkVariable<float>();

        public IHider CurrentHider { get; private set; }
        public Renderer ObjectRenderer { get; private set; }
        public Collider ObjectCollider { get; private set; }

        // IObjectDisguise implementation
        public ObjectType Type => objectType;
        public float MaxHealth => objectMaxHealth;
        public override float CurrentHealth => networkHealth.Value;
        public bool IsOccupied => networkOccupied.Value;

        public static event Action<ObjectType, bool> OnObjectOccupiedChanged;
        public static event Action<ObjectType, float> OnObjectHealthChanged;
        public static event Action<ObjectType, Vector3> OnObjectDestroyed;

        protected override void Awake()
        {
            base.Awake();

            ObjectRenderer = GetComponent<Renderer>();
            ObjectCollider = GetComponent<Collider>();

            // Set health based on object type
            SetHealthByObjectType();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            networkOccupied.OnValueChanged += OnOccupiedChanged;
            occupyingHiderId.OnValueChanged += OnOccupyingHiderChanged;
            networkHealth.OnValueChanged += OnHealthNetworkChanged;

            if (IsServer)
            {
                networkHealth.Value = objectMaxHealth;
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            networkOccupied.OnValueChanged -= OnOccupiedChanged;
            occupyingHiderId.OnValueChanged -= OnOccupyingHiderChanged;
            networkHealth.OnValueChanged -= OnHealthNetworkChanged;
        }

        private void SetHealthByObjectType()
        {
            switch (objectType)
            {
                case ObjectType.TrashBag:
                    objectMaxHealth = 1f;
                    break;
                case ObjectType.Chair:
                    objectMaxHealth = 5f;
                    break;
                case ObjectType.Table:
                    objectMaxHealth = 10f;
                    break;
                case ObjectType.Barrel:
                    objectMaxHealth = 50f;
                    break;
                case ObjectType.Container:
                    objectMaxHealth = 100f;
                    break;
                default:
                    objectMaxHealth = 10f;
                    break;
            }

            maxHealth = objectMaxHealth;
            currentHealth = objectMaxHealth;
        }

        public virtual void OccupyObject(IHider hider)
        {
            if (!canBeOccupied || IsOccupied || !IsAlive) return;

            if (IsServer)
            {
                networkOccupied.Value = true;
                occupyingHiderId.Value = hider.ClientId;
            }

            CurrentHider = hider;
            OnObjectOccupied(hider);
        }

        public virtual void ReleaseObject()
        {
            if (!IsOccupied) return;

            if (IsServer)
            {
                networkOccupied.Value = false;
                occupyingHiderId.Value = 0;
            }

            var previousHider = CurrentHider;
            CurrentHider = null;
            OnObjectReleased(previousHider);
        }

        public virtual void TakeDamage(float damage, ISeeker attacker)
        {
            if (!IsAlive) return;

            if (IsServer)
            {
                float actualDamage = base.TakeDamage(attacker, damage, DamageType.Physical);
                TakeDamageClientRpc(damage, attacker.ClientId);

                // If occupied and object is destroyed, kill the hider
                if (!IsAlive && IsOccupied && CurrentHider != null)
                {
                    KillOccupyingHider(attacker);
                }
            }
        }

        private void KillOccupyingHider(ISeeker attacker)
        {
            if (CurrentHider != null)
            {
                var gameManager = FindObjectOfType<GameManager>();
                gameManager?.PlayerKilledServerRpc(attacker.ClientId, CurrentHider.ClientId);
            }
        }

        protected virtual void OnObjectOccupied(IHider hider)
        {
            // Change material to show occupation
            if (ObjectRenderer != null && occupiedMaterial != null)
            {
                ObjectRenderer.material = occupiedMaterial;
            }

            // Add visual effects for occupation
            CreateOccupationEffect();
        }

        protected virtual void OnObjectReleased(IHider hider)
        {
            // Restore normal material
            if (ObjectRenderer != null && normalMaterial != null)
            {
                ObjectRenderer.material = normalMaterial;
            }

            // Remove occupation effects
            RemoveOccupationEffect();
        }

        protected virtual void CreateOccupationEffect()
        {
            // Override in derived classes for specific effects
            // Could add subtle glow, particles, etc.
        }

        protected virtual void RemoveOccupationEffect()
        {
            // Override in derived classes to remove effects
        }

        #region Server RPCs

        [ServerRpc(RequireOwnership = false)]
        public void OccupyObjectServerRpc(ulong hiderId)
        {
            var hider = FindObjectsOfType<MonoBehaviour>().OfType<IHider>().FirstOrDefault(h => h.ClientId == hiderId);
            if (hider != null)
            {
                OccupyObject(hider);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void ReleaseObjectServerRpc()
        {
            ReleaseObject();
        }

        #endregion

        #region Client RPCs

        [ClientRpc]
        protected void TakeDamageClientRpc(float damage, ulong attackerId)
        {
            // Create hit effect
            if (hitEffect != null)
            {
                var effect = Instantiate(hitEffect, transform.position, Quaternion.identity);
                Destroy(effect.gameObject, 2f);
            }

            // Update material based on health
            UpdateDamageMaterial();

            OnObjectHealthChanged?.Invoke(objectType, CurrentHealth / MaxHealth);
        }

        [ClientRpc]
        protected void DestroyObjectClientRpc()
        {
            // Create destruction effect
            if (destructionEffect != null)
            {
                var effect = Instantiate(destructionEffect, transform.position, Quaternion.identity);
                Destroy(effect.gameObject, 5f);
            }

            OnObjectDestroyed?.Invoke(objectType, transform.position);

            // Hide object
            if (ObjectRenderer != null)
                ObjectRenderer.enabled = false;
            if (ObjectCollider != null)
                ObjectCollider.enabled = false;
        }

        #endregion

        #region Event Handlers

        private void OnOccupiedChanged(bool previousValue, bool newValue)
        {
            OnObjectOccupiedChanged?.Invoke(objectType, newValue);
        }

        private void OnOccupyingHiderChanged(ulong previousValue, ulong newValue)
        {
            if (newValue != 0)
            {
                var hider = FindObjectsOfType<MonoBehaviour>().OfType<IHider>()
                    .FirstOrDefault(h => h.ClientId == newValue);
                if (hider != null)
                {
                    CurrentHider = hider;
                    OnObjectOccupied(hider);
                }
            }
            else
            {
                var previousHider = CurrentHider;
                CurrentHider = null;
                if (previousHider != null)
                    OnObjectReleased(previousHider);
            }
        }

        private void OnHealthNetworkChanged(float previousValue, float newValue)
        {
            if (newValue <= 0 && IsServer)
            {
                DestroyObjectClientRpc();
            }
        }

        #endregion

        private void UpdateDamageMaterial()
        {
            if (ObjectRenderer == null || damagedMaterial == null) return;

            float healthPercent = CurrentHealth / MaxHealth;

            if (healthPercent < 0.3f)
            {
                // Heavily damaged
                ObjectRenderer.material = damagedMaterial;
            }
            else if (healthPercent < 0.7f)
            {
                // Moderately damaged - could blend materials
                if (IsOccupied && occupiedMaterial != null)
                {
                    // Mix occupied and damaged materials
                    var material = new Material(occupiedMaterial);
                    material.color = Color.Lerp(material.color, Color.red, 0.3f);
                    ObjectRenderer.material = material;
                }
                else
                {
                    var material = new Material(normalMaterial);
                    material.color = Color.Lerp(material.color, Color.red, 0.2f);
                    ObjectRenderer.material = material;
                }
            }
        }

        #region Interaction System Integration

        public override bool Interact(IInteractable target)
        {
            if (target is IHider hider && !IsOccupied && canBeOccupied)
            {
                if (IsServer)
                    OccupyObjectServerRpc(hider.ClientId);
                return true;
            }

            return false;
        }

        public override void OnInteracted(IInteractable initiator)
        {
            if (initiator is IHider hider && !IsOccupied && canBeOccupied)
            {
                OccupyObject(hider);
            }
        }

        protected override void OnStateChanged(InteractionState previousState, InteractionState newState)
        {
            if (newState == InteractionState.Dead)
            {
                if (IsOccupied)
                    ReleaseObject();
            }
        }

        #endregion

        #region Death Override

        public override void OnDeath(IAttackable killer = null)
        {
            base.OnDeath(killer);

            if (IsOccupied)
            {
                ReleaseObject();
            }
        }

        #endregion
    }
}
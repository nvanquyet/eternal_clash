using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using HideAndSeekGame.Core;
using HideAndSeekGame.Players;
using _GAME.Scripts.DesignPattern.Interaction;

namespace HideAndSeekGame.Objects
{
    #region Base Disguise Object
    
    public abstract class BaseDisguiseObject : ADefendable, IObjectDisguise
    {
        [Header("Disguise Object Settings")]
        [SerializeField] protected ObjectType objectType;
        [SerializeField] protected float objectMaxHealth = 10f;
        [SerializeField] protected Vector3 objectSize = Vector3.one;
        [SerializeField] protected bool canBeOccupied = true;
        
        [Header("Visual Settings")]
        [SerializeField] protected Material normalMaterial;
        [SerializeField] protected Material occupiedMaterial;
        [SerializeField] protected Material damagedMaterial;
        [SerializeField] protected ParticleSystem hitEffect;
        [SerializeField] protected ParticleSystem destructionEffect;
        
        // Network variables
        protected NetworkVariable<bool> networkOccupied = new NetworkVariable<bool>(false);
        protected NetworkVariable<ulong> occupyingHiderId = new NetworkVariable<ulong>(0);
        protected NetworkVariable<float> networkHealth = new NetworkVariable<float>();
        
        protected IHider currentHider;
        protected Renderer objectRenderer;
        protected Collider objectCollider;
        
        // IObjectDisguise implementation
        public ObjectType Type => objectType;
        public float MaxHealth => objectMaxHealth;
        public override float CurrentHealth => networkHealth.Value;
        public bool IsOccupied => networkOccupied.Value;
        public override Vector3 Position => transform.position;
        
        public static event Action<ObjectType, bool> OnObjectOccupiedChanged;
        public static event Action<ObjectType, float> OnObjectHealthChanged;
        public static event Action<ObjectType, Vector3> OnObjectDestroyed;
        
        protected override void Awake()
        {
            base.Awake();
            
            objectRenderer = GetComponent<Renderer>();
            objectCollider = GetComponent<Collider>();
            
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
            
            currentHider = hider;
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
            
            var previousHider = currentHider;
            currentHider = null;
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
                if (!IsAlive && IsOccupied && currentHider != null)
                {
                    KillOccupyingHider(attacker);
                }
            }
        }
        
        private void KillOccupyingHider(ISeeker attacker)
        {
            if (currentHider != null)
            {
                var gameManager = FindObjectOfType<GameManager>();
                gameManager?.PlayerKilledServerRpc(attacker.ClientId, currentHider.ClientId);
            }
        }
        
        protected virtual void OnObjectOccupied(IHider hider)
        {
            // Change material to show occupation
            if (objectRenderer != null && occupiedMaterial != null)
            {
                objectRenderer.material = occupiedMaterial;
            }
            
            // Add visual effects for occupation
            CreateOccupationEffect();
        }
        
        protected virtual void OnObjectReleased(IHider hider)
        {
            // Restore normal material
            if (objectRenderer != null && normalMaterial != null)
            {
                objectRenderer.material = normalMaterial;
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
            if (objectRenderer != null)
                objectRenderer.enabled = false;
            if (objectCollider != null)
                objectCollider.enabled = false;
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
                var hider = FindObjectsOfType<MonoBehaviour>().OfType<IHider>().FirstOrDefault(h => h.ClientId == newValue);
                if (hider != null)
                {
                    currentHider = hider;
                    OnObjectOccupied(hider);
                }
            }
            else
            {
                var previousHider = currentHider;
                currentHider = null;
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
            if (objectRenderer == null || damagedMaterial == null) return;
            
            float healthPercent = CurrentHealth / MaxHealth;
            
            if (healthPercent < 0.3f)
            {
                // Heavily damaged
                objectRenderer.material = damagedMaterial;
            }
            else if (healthPercent < 0.7f)
            {
                // Moderately damaged - could blend materials
                if (IsOccupied && occupiedMaterial != null)
                {
                    // Mix occupied and damaged materials
                    var material = new Material(occupiedMaterial);
                    material.color = Color.Lerp(material.color, Color.red, 0.3f);
                    objectRenderer.material = material;
                }
                else
                {
                    var material = new Material(normalMaterial);
                    material.color = Color.Lerp(material.color, Color.red, 0.2f);
                    objectRenderer.material = material;
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
    
    #endregion
    
    #region Specific Disguise Objects
    
    public class TableDisguise : BaseDisguiseObject
    {
        [Header("Table Specific")]
        [SerializeField] private Transform[] hidingSpots;
        [SerializeField] private GameObject tableclothEffect;
        
        protected override void Awake()
        {
            objectType = ObjectType.Table;
            base.Awake();
        }
        
        protected override void CreateOccupationEffect()
        {
            base.CreateOccupationEffect();
            
            // Add tablecloth movement effect
            if (tableclothEffect != null)
            {
                tableclothEffect.SetActive(true);
            }
        }
        
        protected override void RemoveOccupationEffect()
        {
            base.RemoveOccupationEffect();
            
            if (tableclothEffect != null)
            {
                tableclothEffect.SetActive(false);
            }
        }
    }
    
    public class ContainerDisguise : BaseDisguiseObject
    {
        [Header("Container Specific")]
        [SerializeField] private Animator lidAnimator;
        [SerializeField] private AudioSource containerSounds;
        
        protected override void Awake()
        {
            objectType = ObjectType.Container;
            base.Awake();
        }
        
        protected override void CreateOccupationEffect()
        {
            base.CreateOccupationEffect();
            
            // Close lid
            if (lidAnimator != null)
            {
                lidAnimator.SetBool("IsOccupied", true);
            }
            
            // Play closing sound
            if (containerSounds != null)
            {
                containerSounds.Play();
            }
        }
        
        protected override void RemoveOccupationEffect()
        {
            base.RemoveOccupationEffect();
            
            // Open lid
            if (lidAnimator != null)
            {
                lidAnimator.SetBool("IsOccupied", false);
            }
        }
    }
    
    public class TrashBagDisguise : BaseDisguiseObject
    {
        [Header("Trash Bag Specific")]
        [SerializeField] private float wobbleIntensity = 0.1f;
        [SerializeField] private float wobbleSpeed = 2f;
        
        private Vector3 originalPosition;
        private bool isWobbling = false;
        
        protected override void Awake()
        {
            objectType = ObjectType.TrashBag;
            base.Awake();
            originalPosition = transform.position;
        }
        
        private void Update()
        {
            if (isWobbling && IsOccupied)
            {
                // Add subtle wobble effect
                Vector3 wobble = new Vector3(
                    Mathf.Sin(Time.time * wobbleSpeed) * wobbleIntensity,
                    0,
                    Mathf.Cos(Time.time * wobbleSpeed * 0.8f) * wobbleIntensity
                );
                transform.position = originalPosition + wobble;
            }
        }
        
        protected override void CreateOccupationEffect()
        {
            base.CreateOccupationEffect();
            isWobbling = true;
        }
        
        protected override void RemoveOccupationEffect()
        {
            base.RemoveOccupationEffect();
            isWobbling = false;
            transform.position = originalPosition;
        }
    }
    
    public class ChairDisguise : BaseDisguiseObject
    {
        [Header("Chair Specific")]
        [SerializeField] private Transform seatPosition;
        [SerializeField] private float rockingAngle = 5f;
        
        protected override void Awake()
        {
            objectType = ObjectType.Chair;
            base.Awake();
        }
        
        protected override void CreateOccupationEffect()
        {
            base.CreateOccupationEffect();
            
            // Add slight rocking motion
            StartCoroutine(RockingMotion());
        }
        
        private System.Collections.IEnumerator RockingMotion()
        {
            Vector3 originalRotation = transform.eulerAngles;
            
            while (IsOccupied)
            {
                float rock = Mathf.Sin(Time.time * 0.5f) * rockingAngle;
                transform.rotation = Quaternion.Euler(originalRotation.x, originalRotation.y, rock);
                yield return null;
            }
            
            transform.rotation = Quaternion.Euler(originalRotation);
        }
    }
    
    public class BarrelDisguise : BaseDisguiseObject
    {
        [Header("Barrel Specific")]
        [SerializeField] private ParticleSystem steamEffect;
        [SerializeField] private float steamIntensity = 10f;
        
        protected override void Awake()
        {
            objectType = ObjectType.Barrel;
            base.Awake();
        }
        
        protected override void CreateOccupationEffect()
        {
            base.CreateOccupationEffect();
            
            // Add steam effect
            if (steamEffect != null)
            {
                var emission = steamEffect.emission;
                emission.rateOverTime = steamIntensity;
                steamEffect.Play();
            }
        }
        
        protected override void RemoveOccupationEffect()
        {
            base.RemoveOccupationEffect();
            
            if (steamEffect != null)
            {
                steamEffect.Stop();
            }
        }
    }
    
    #endregion
    
    #region Disguise Object Manager
    
    public class DisguiseObjectManager : NetworkBehaviour
    {
        [Header("Object Spawning")]
        [SerializeField] private GameObject[] objectPrefabs;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private int objectsPerType = 3;
        
        [Header("Object Swapping")]
        [SerializeField] private float swapInterval = 30f; // Force swap every 30 seconds
        
        private List<BaseDisguiseObject> spawnedObjects = new List<BaseDisguiseObject>();
        private List<IHider> occupiedHiders = new List<IHider>();
        
        public static event Action OnForceObjectSwap;
        
        private void Start()
        {
            if (IsServer)
            {
                SpawnDisguiseObjects();
                InvokeRepeating(nameof(ForceObjectSwap), swapInterval, swapInterval);
            }
        }
        
        private void SpawnDisguiseObjects()
        {
            var availableSpawnPoints = new List<Transform>(spawnPoints);
            
            foreach (var prefab in objectPrefabs)
            {
                for (int i = 0; i < objectsPerType && availableSpawnPoints.Count > 0; i++)
                {
                    int randomIndex = UnityEngine.Random.Range(0, availableSpawnPoints.Count);
                    var spawnPoint = availableSpawnPoints[randomIndex];
                    availableSpawnPoints.RemoveAt(randomIndex);
                    
                    var obj = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
                    var networkObj = obj.GetComponent<NetworkObject>();
                    if (networkObj != null)
                    {
                        networkObj.Spawn();
                    }
                    
                    var disguiseObj = obj.GetComponent<BaseDisguiseObject>();
                    if (disguiseObj != null)
                    {
                        spawnedObjects.Add(disguiseObj);
                    }
                }
            }
        }
        
        [ServerRpc(RequireOwnership = false)]
        public void ForceObjectSwap()
        {
            // Get all occupied hiders
            occupiedHiders.Clear();
            foreach (var obj in spawnedObjects)
            {
                if (obj.IsOccupied && obj.currentHider != null)
                {
                    occupiedHiders.Add(obj.currentHider);
                    obj.ReleaseObject();
                }
            }
            
            // Notify clients about forced swap
            ForceObjectSwapClientRpc();
            
            // Wait a moment then reassign random objects
            StartCoroutine(ReassignObjects());
        }
        
        private System.Collections.IEnumerator ReassignObjects()
        {
            yield return new WaitForSeconds(1f);
            
            foreach (var hider in occupiedHiders)
            {
                var availableObjects = spawnedObjects.Where(o => !o.IsOccupied && o.IsAlive).ToList();
                if (availableObjects.Count > 0)
                {
                    var randomObject = availableObjects[UnityEngine.Random.Range(0, availableObjects.Count)];
                    randomObject.OccupyObject(hider);
                }
            }
        }
        
        [ClientRpc]
        private void ForceObjectSwapClientRpc()
        {
            OnForceObjectSwap?.Invoke();
        }
    }
    
    #endregion
}
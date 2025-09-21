using System;
using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.CameraHnS;
using _GAME.Scripts.Utils;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.HideAndSeek.Combat.Base
{
    public class AGun : AAttackable
    {
        [Header("Gun Settings")]
        [SerializeField] protected Transform firePoint;
        [SerializeField] protected AProjectile bulletPrefab;
        [SerializeField] protected int maxAmmo = 30;
        [SerializeField] protected float reloadTime = 2f;
        [SerializeField] protected bool autoReload = true;
        
        [Header("Audio & Effects")]
        [SerializeField] protected ParticleSystem muzzleFlash;
        [SerializeField] protected AudioClip shootSound;
        [SerializeField] protected AudioClip reloadSound;
        [SerializeField] protected AudioSource audioSource;
        
        [Header("Input Action References")]
        [SerializeField] private InputActionReference attackActionRef;
        [SerializeField] private InputActionReference reloadActionRef;
        
        [Header("Lookup Settings")]
        [SerializeField] private LookAtPoint lookAtPoint;
        
        private InputAction attackAction;
        private InputAction reloadAction;
        
        // Network synced variables
        private NetworkVariable<int> networkCurrentAmmo = new NetworkVariable<int>(
            writePerm: NetworkVariableWritePermission.Server);
        private NetworkVariable<bool> networkIsReloading = new NetworkVariable<bool>(
            writePerm: NetworkVariableWritePermission.Server);
        private NetworkVariable<double> networkReloadEndTime = new NetworkVariable<double>(
            writePerm: NetworkVariableWritePermission.Server);
        
        // Properties
        public int CurrentAmmo => networkCurrentAmmo.Value;
        public int MaxAmmo => maxAmmo;
        public bool IsReloading => networkIsReloading.Value;
        public bool HasAmmo => networkCurrentAmmo.Value > 0;
        
        // Override CanAttack to include ammo check
        public override bool CanAttack => base.CanAttack && HasAmmo && !IsReloading;
        
        // Events for UI updates
        public System.Action<int, int> OnAmmoChanged;
        public System.Action<bool> OnReloadStateChanged;
        
        protected override void Awake()
        {
            base.Awake();
            
            // Ensure we have fire point
            if (firePoint == null)
                firePoint = transform;
                
            // Setup audio source
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            // Subscribe to network variable changes for UI updates
            networkCurrentAmmo.OnValueChanged += OnAmmoValueChanged;
            networkIsReloading.OnValueChanged += OnReloadStateValueChanged;
            networkReloadEndTime.OnValueChanged += OnReloadEndTimeChanged;
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            
            // Unsubscribe from network variables
            if (networkCurrentAmmo != null)
                networkCurrentAmmo.OnValueChanged -= OnAmmoValueChanged;
            if (networkIsReloading != null)
                networkIsReloading.OnValueChanged -= OnReloadStateValueChanged;
            if (networkReloadEndTime != null)
                networkReloadEndTime.OnValueChanged -= OnReloadEndTimeChanged;
            
            if (IsOwner)
            {
                CleanupInput();
            }
        }
        
        protected virtual void Update()
        {
            // Only server handles reload timing
            if (!IsServer) return;
            
            if (networkIsReloading.Value && 
                NetworkManager.Singleton.ServerTime.Time >= networkReloadEndTime.Value)
            {
                CompleteReload();
            }
            
            // Auto reload when empty
            if (autoReload && networkCurrentAmmo.Value == 0 && !networkIsReloading.Value && CanInteract)
            {
                StartReload();
            }
        }
        
        #region Network Variable Callbacks
        
        private void OnAmmoValueChanged(int previousValue, int newValue)
        {
            OnAmmoChanged?.Invoke(newValue, maxAmmo);
        }
        
        private void OnReloadStateValueChanged(bool previousValue, bool newValue)
        {
            OnReloadStateChanged?.Invoke(newValue);
        }
        
        private void OnReloadEndTimeChanged(double previousValue, double newValue)
        {
            // Optional: Use for UI progress bar
        }
        
        #endregion
        
        #region Input System
        
        private void SetupInput()
        {
            if (attackActionRef != null)
            {
                attackAction = InputActionFactory.CreateUniqueAction(attackActionRef, GetInstanceID());
                attackAction.performed += OnFireInput;
                attackAction.Enable();
            }
            
            if (reloadActionRef != null)
            {
                reloadAction = InputActionFactory.CreateUniqueAction(reloadActionRef, GetInstanceID());
                reloadAction.performed += OnReloadInput;
                reloadAction.Enable();
            }
        }
        
        private void CleanupInput()
        {
            if (attackAction != null)
            {
                attackAction.performed -= OnFireInput;
                attackAction.Disable();
                attackAction = null;
            }
            
            if (reloadAction != null)
            {
                reloadAction.performed -= OnReloadInput;
                reloadAction.Disable();
                reloadAction = null;
            }
        }
        
        private void OnFireInput(InputAction.CallbackContext context)
        {
            if (!IsOwner || !CanAttack) return;
            
            Vector3 fireDirection = GetFireDirection();
            RequestFireServerRpc(firePoint.position, fireDirection);
        }
        
        private void OnReloadInput(InputAction.CallbackContext context)
        {
            if (!IsOwner || !CanReload()) return;
            
            RequestReloadServerRpc();
        }
        
        #endregion
        
        #region Firing System
        
        [ServerRpc]
        private void RequestFireServerRpc(Vector3 spawnPosition, Vector3 direction, ServerRpcParams rpcParams = default)
        {
            // Server validation
            if (!CanAttack)
            {
                Debug.LogWarning($"[{name}] Fire request rejected - cannot attack");
                return;
            }
            
            FireBullet(spawnPosition, direction);
        }
        
        private void FireBullet(Vector3 spawnPosition, Vector3 direction)
        {
            // Create bullet
            AProjectile bullet = Instantiate(bulletPrefab);
            
            // Initialize bullet
            //bullet.Initialize(this, spawnPosition, direction);
            
            // Spawn on network
            bullet.NetworkObject.Spawn(true);
            
            // Update gun state
            networkCurrentAmmo.Value--;
            networkNextAttackServerTime.Value = NetworkManager.Singleton.ServerTime.Time + attackCooldown;
            
            // Notify clients for effects
            FireEffectsClientRpc(spawnPosition, direction);
            
            Debug.Log($"[{name}] Fired bullet. Ammo: {networkCurrentAmmo.Value}/{maxAmmo}");
        }
        
        [ClientRpc]
        private void FireEffectsClientRpc(Vector3 spawnPosition, Vector3 direction)
        {
            PlayFireEffects();
        }
        
        protected virtual Vector3 GetFireDirection()
        {
            // Default: forward direction
            // Override this for camera-based aiming
            return firePoint.forward;
        }
        
        protected virtual void PlayFireEffects()
        {
            // Muzzle flash
            if (muzzleFlash != null)
            {
                muzzleFlash.Play();
            }
            
            // Shoot sound
            if (audioSource != null && shootSound != null)
            {
                audioSource.PlayOneShot(shootSound);
            }
        }
        
        #endregion
        
        #region Reload System
        
        [ServerRpc]
        private void RequestReloadServerRpc(ServerRpcParams rpcParams = default)
        {
            StartReload();
        }
        
        protected virtual bool CanReload()
        {
            return !networkIsReloading.Value && networkCurrentAmmo.Value < maxAmmo && CanInteract;
        }
        
        public virtual void StartReload()
        {
            if (!IsServer || !CanReload()) return;
            
            networkIsReloading.Value = true;
            networkReloadEndTime.Value = NetworkManager.Singleton.ServerTime.Time + reloadTime;
            SetState(InteractionState.Disabled);
            
            ReloadStartEffectsClientRpc();
            
            Debug.Log($"[{name}] Started reloading... ({reloadTime}s)");
        }
        
        [ClientRpc]
        private void ReloadStartEffectsClientRpc()
        {
            PlayReloadStartEffects();
        }
        
        protected virtual void CompleteReload()
        {
            if (!IsServer) return;
            
            networkCurrentAmmo.Value = maxAmmo;
            networkIsReloading.Value = false;
            SetState(InteractionState.Enable);
            
            ReloadCompleteEffectsClientRpc();
            
            Debug.Log($"[{name}] Reload completed. Ammo: {networkCurrentAmmo.Value}/{maxAmmo}");
        }
        
        [ClientRpc]
        private void ReloadCompleteEffectsClientRpc()
        {
            PlayReloadCompleteEffects();
        }
        
        protected virtual void PlayReloadStartEffects()
        {
            if (audioSource != null && reloadSound != null)
            {
                audioSource.PlayOneShot(reloadSound);
            }
        }
        
        protected virtual void PlayReloadCompleteEffects()
        {
            // Override for reload complete effects
        }
        
        // Public method to manually trigger reload
        public void TriggerReload()
        {
            if (IsOwner && CanReload())
            {
                RequestReloadServerRpc();
            }
        }
        
        #endregion
        
        #region Required Overrides from New Base System
        
        public override bool Interact(IInteractable target) => false;
        public override void OnInteracted(IInteractable initiator) { }
        
        protected override void OnSuccessfulAttack(IDefendable target, float actualDamage)
        {
            // Gun doesn't directly attack - bullets do
        }
        
        protected override void OnHitInvalidTarget(Collider other)
        {
            // Gun doesn't hit targets directly
        }
        
        protected override void OnHitNonDefendableTarget(Collider other)
        {
            // Gun doesn't hit targets directly
        }
        
        protected override void HandleDestruction()
        {
            // Handle gun destruction if needed
            if (NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
        }
        
        #endregion

        public void Initialize()
        {
            // Initialize values on server
            if (IsServer)
            {
                networkCurrentAmmo.Value = maxAmmo;
                networkIsReloading.Value = false;
                networkReloadEndTime.Value = 0;
            }
            
            // Only owner handles input
            if (IsOwner)
            {
                SetupInput();
                lookAtPoint?.Init();
            }
        }
        
        #region Action callback
        public Action OnDropWeapon = null;
        
        public void DropWeapon()
        {
            OnDropWeapon?.Invoke();
        }
        #endregion
    }
}

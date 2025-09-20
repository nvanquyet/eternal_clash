using System;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Combat.Gun
{
  public class GunMagazineComponent : NetworkBehaviour, IAmmoSystem
    {
        [Header("Ammo Configuration")]
        [SerializeField] private int maxAmmo = 30;
        [SerializeField] private int startingAmmo = -1; // -1 = use maxAmmo
        [SerializeField] private bool unlimitedAmmo = false;
        
        [Header("Ammo Behavior")]
        [SerializeField] private bool autoRefillOnEmpty = false;
        [SerializeField] private float autoRefillDelay = 1f;
        
        private NetworkVariable<int> networkCurrentAmmo = new NetworkVariable<int>(
            writePerm: NetworkVariableWritePermission.Server);
            
        public bool IsInitialized { get; private set; }
        public int CurrentAmmo => networkCurrentAmmo.Value;
        public int MaxAmmo => maxAmmo;
        public bool HasAmmo => unlimitedAmmo || networkCurrentAmmo.Value > 0;
        public bool IsEmpty => !unlimitedAmmo && networkCurrentAmmo.Value == 0;
        public bool IsFull => unlimitedAmmo || networkCurrentAmmo.Value >= maxAmmo;
        public bool IsUnlimited => unlimitedAmmo;
        
        public event Action<int, int> OnAmmoChanged;
        public event Action OnAmmoEmpty;
        
        private Coroutine autoRefillCoroutine;
        
        public void Initialize()
        {
            if (IsInitialized) return;
            
            if (IsServer)
            {
                int initialAmmo = unlimitedAmmo ? maxAmmo : 
                    (startingAmmo < 0 ? maxAmmo : Mathf.Min(startingAmmo, maxAmmo));
                networkCurrentAmmo.Value = initialAmmo;
            }
            
            IsInitialized = true;
        }
        
        public void Cleanup()
        {
            if (autoRefillCoroutine != null)
            {
                StopCoroutine(autoRefillCoroutine);
                autoRefillCoroutine = null;
            }
            
            if (networkCurrentAmmo != null)
                networkCurrentAmmo.OnValueChanged -= OnAmmoValueChanged;
                
            IsInitialized = false;
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            networkCurrentAmmo.OnValueChanged += OnAmmoValueChanged;
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            Cleanup();
        }
        
        private void OnAmmoValueChanged(int previousValue, int newValue)
        {
            OnAmmoChanged?.Invoke(newValue, maxAmmo);
            
            if (newValue == 0 && previousValue > 0)
            {
                OnAmmoEmpty?.Invoke();
                
                if (autoRefillOnEmpty && IsServer)
                {
                    if (autoRefillCoroutine != null) StopCoroutine(autoRefillCoroutine);
                    autoRefillCoroutine = StartCoroutine(AutoRefillAfterDelay());
                }
            }
        }
        
        private System.Collections.IEnumerator AutoRefillAfterDelay()
        {
            yield return new WaitForSeconds(autoRefillDelay);
            RefillAmmo();
            autoRefillCoroutine = null;
        }
        
        public bool TryConsumeAmmo()
        {
            if (!IsServer || (!HasAmmo && !unlimitedAmmo)) return false;
            
            if (!unlimitedAmmo)
                networkCurrentAmmo.Value = Mathf.Max(0, networkCurrentAmmo.Value - 1);
                
            return true;
        }
        
        public void RestoreAmmo(int amount)
        {
            if (!IsServer || unlimitedAmmo) return;
            networkCurrentAmmo.Value = Mathf.Min(networkCurrentAmmo.Value + amount, maxAmmo);
        }
        
        public void RefillAmmo()
        {
            if (!IsServer || unlimitedAmmo) return;
            networkCurrentAmmo.Value = maxAmmo;
        }
        
        public void SetMaxAmmo(int newMaxAmmo)
        {
            if (!IsServer) return;
            
            maxAmmo = newMaxAmmo;
            if (!unlimitedAmmo && networkCurrentAmmo.Value > maxAmmo)
                networkCurrentAmmo.Value = maxAmmo;
        }
        
        public void SetUnlimitedAmmo(bool unlimited)
        {
            if (!IsServer) return;
            
            unlimitedAmmo = unlimited;
            if (unlimited)
                networkCurrentAmmo.Value = maxAmmo;
        }
    }
}
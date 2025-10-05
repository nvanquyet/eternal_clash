using UnityEngine;
using _GAME.Scripts.HideAndSeek.Player;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using _GAME.Scripts.HideAndSeek.Combat.Gun;
using Unity.Netcode;

namespace _GAME.Scripts.Player
{
    /// <summary>
    /// Bridge giữa PlayerEquipment và UI system.
    /// Chỉ chạy trên local player (IsOwner).
    /// </summary>
    [RequireComponent(typeof(PlayerEquipment))]
    public class PlayerWeaponUIBridge : NetworkBehaviour
    {
        private PlayerEquipment playerEquipment;
        private GunMagazineComponent currentMagazine;
        private MobileInputBridge mobileInput;
        
        void Awake()
        {
            playerEquipment = GetComponent<PlayerEquipment>();
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            // ✅ CHỈ chạy cho local player
            if (!IsOwner) return;
            
            // Get UI reference
            mobileInput = transform.root.GetComponentInChildren<MobileInputBridge>();
            
            // Subscribe equipment events
            playerEquipment.OnWeaponEquipped += OnWeaponEquipped;
            playerEquipment.OnWeaponUnequipped += OnWeaponUnequipped;
            
            // Init nếu đã có weapon
            var currentWeapon = playerEquipment.GetCurrentWeapon();
            if (currentWeapon != null)
            {
                OnWeaponEquipped(currentWeapon);
            }
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            
            if (!IsOwner) return;
            
            // Cleanup
            if (playerEquipment != null)
            {
                playerEquipment.OnWeaponEquipped -= OnWeaponEquipped;
                playerEquipment.OnWeaponUnequipped -= OnWeaponUnequipped;
            }
            
            UnsubscribeFromMagazine();
        }
        
        private void OnWeaponEquipped(WeaponInteraction weapon)
        {
            if (weapon == null || mobileInput == null) return;
            
            // Unsubscribe old magazine
            UnsubscribeFromMagazine();
            
            // Get magazine component
            currentMagazine = weapon.GetComponent<GunMagazineComponent>();
            
            if (currentMagazine != null)
            {
                // ✅ Subscribe ammo changes
                currentMagazine.OnAmmoChanged += OnAmmoChanged;
                
                // ✅ Show buttons với maxAmmo
                mobileInput.ActiveShootButton(true, currentMagazine.MaxAmmo);
                
                // ✅ Update ammo display
                mobileInput.ShowAmmo(currentMagazine.CurrentAmmo, currentMagazine.MaxAmmo);
            }
            else
            {
                // Weapon không phải gun (melee/magic)
                // Có thể show buttons khác hoặc hide shoot button
                mobileInput.ActiveShootButton(false);
            }
        }
        
        private void OnWeaponUnequipped(WeaponInteraction weapon)
        {
            UnsubscribeFromMagazine();
            
            if (mobileInput != null)
            {
                // ✅ Hide shoot/reload buttons
                mobileInput.ActiveShootButton(false);
            }
        }
        
        private void OnAmmoChanged(int currentAmmo, int maxAmmo)
        {
            if (mobileInput != null)
            {
                // ✅ Update ammo display real-time
                mobileInput.ShowAmmo(currentAmmo, maxAmmo);
            }
        }
        
        private void UnsubscribeFromMagazine()
        {
            if (currentMagazine != null)
            {
                currentMagazine.OnAmmoChanged -= OnAmmoChanged;
                currentMagazine = null;
            }
        }
    }
}
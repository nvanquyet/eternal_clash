using _GAME.Scripts.HideAndSeek.Combat.Base;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player
{
    public class PlayerEquipment : NetworkBehaviour
    {
        [SerializeField] private Vector3 weaponHoldPosition = Vector3.right;
        [SerializeField] private Vector3 weaponHoldRotation = Vector3.zero;
        private NetworkVariable<NetworkBehaviourReference> currentGunRef =
            new NetworkVariable<NetworkBehaviourReference>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            currentGunRef.OnValueChanged += OnGunRefChanged;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            currentGunRef.OnValueChanged -= OnGunRefChanged;
        }

        private void OnGunRefChanged(NetworkBehaviourReference prevRef, NetworkBehaviourReference newRef)
        {
            // Xử lý weapon cũ - chỉ unparent, không despawn
            if (prevRef.TryGet(out AGun prevGun))
            {
                UnequipWeapon(prevGun);
            }

            // Xử lý weapon mới
            if (newRef.TryGet(out AGun newGun))
            {
                EquipWeapon(newGun);
            }
        }

        private void EquipWeapon(AGun weapon)
        {
            // Set parent using NetworkObject parenting (vì weaponHoldPoint là NetworkObject)
            weapon.NetworkObject.TrySetParent(this.transform.parent);
            
            weapon.transform.localPosition = weaponHoldPosition;
            weapon.transform.localRotation = Quaternion.Euler(weaponHoldRotation);

            // Initialize weapon (chỉ owner)
            if (IsOwner)
            {
                weapon.Initialize();
            }

            Debug.Log($"[PlayerEquipment] Weapon equipped: {weapon.name}");
        }

        private void UnequipWeapon(AGun weapon)
        {
            if (weapon == null) return;

            // Remove parent - weapon trở thành independent NetworkObject
            weapon.NetworkObject.TrySetParent((Transform)null);
            weapon.DropWeapon();
            Debug.Log($"[PlayerEquipment] Weapon unequipped: {weapon.name}");
        }

        // API public cho client gọi
        public void SetCurrentGun(AGun gun)
        {
            if (!IsOwner) return;
            RequestSetGunServerRpc(gun ? gun.NetworkObject : default(NetworkObjectReference));
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSetGunServerRpc(NetworkObjectReference gunObjRef)
        {
            // Xử lý weapon cũ - KHÔNG despawn, chỉ clear reference
            if (currentGunRef.Value.TryGet(out AGun prevGun))
            {
                Debug.Log($"[PlayerEquipment Server] Removing previous weapon: {prevGun.name}");
                // Weapon cũ sẽ được unparent trong OnGunRefChanged
            }

            // Set weapon mới
            if (gunObjRef.TryGet(out NetworkObject gunNob))
            {
                var newGun = gunNob.GetComponent<AGun>();
                if (newGun != null)
                {
                    // Ensure weapon is spawned
                    if (!gunNob.IsSpawned)
                    {
                        gunNob.Spawn(true);
                    }

                    // Update NetworkVariable - OnGunRefChanged sẽ xử lý parenting
                    currentGunRef.Value = new NetworkBehaviourReference(newGun);

                    Debug.Log($"[PlayerEquipment Server] Set new weapon: {newGun.name}");

                    // Notify clients for effects/sounds
                    NotifyEquippedClientRpc(gunNob);
                }
            }
            else
            {
                // Clear weapon reference
                currentGunRef.Value = default;
                Debug.Log("[PlayerEquipment Server] Weapon cleared");
            }
        }

        [ClientRpc]
        private void NotifyEquippedClientRpc(NetworkObjectReference gunObjRef)
        {
            // Play sound/animation effects khi equip weapon
            Debug.Log("[PlayerEquipment] Weapon equipped - playing effects");
        }

        // Method để clear weapon khỏi equipment (không despawn)
        public void ClearCurrentWeapon()
        {
            if (!IsOwner) return;
            RequestSetGunServerRpc(default);
        }

        // Helper method để lấy current weapon
        public AGun GetCurrentWeapon()
        {
            return currentGunRef.Value.TryGet(out AGun gun) ? gun : null;
        }

        // Helper method để check có weapon không
        public bool HasWeapon()
        {
            return currentGunRef.Value.TryGet(out _);
        }
    }
}
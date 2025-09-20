using _GAME.Scripts.HideAndSeek.Combat.Base;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player
{
    public class PlayerEquipment : NetworkBehaviour
    {
        [SerializeField] private Vector3 weaponHoldPosition = Vector3.right;
        [SerializeField] private Vector3 weaponHoldRotation = Vector3.zero;

        private NetworkVariable<NetworkBehaviourReference> currentWeaponRef =
            new NetworkVariable<NetworkBehaviourReference>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            currentWeaponRef.OnValueChanged += OnWeaponRefChanged;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            currentWeaponRef.OnValueChanged -= OnWeaponRefChanged;
        }

        private void OnWeaponRefChanged(NetworkBehaviourReference prevRef, NetworkBehaviourReference newRef)
        {
            if (!IsServer) return; // parenting và drop chỉ làm ở server

            if (prevRef.TryGet(out WeaponInteraction prevWeapon))
                UnequipWeapon(prevWeapon);

            if (newRef.TryGet(out WeaponInteraction newWeapon))
                EquipWeapon(newWeapon);
        }

        private void EquipWeapon(WeaponInteraction weapon)
        {
            // Set parent using NetworkObject parenting (vì weaponHoldPoint là NetworkObject)
            //Try get net object component
            if (weapon == null) return;
            Debug.Log($"[PlayerEquipment] Equipping weapon: {weapon.name}");
            if (gameObject.TryGetComponent<NetworkObject>(out var netObj))
            {
                weapon.NetworkObject.TrySetParent(this.transform);
            }
            else
            {
                weapon.NetworkObject.TrySetParent(this.transform.parent);
            }

            weapon.transform.localPosition = weaponHoldPosition;
            weapon.transform.localRotation = Quaternion.Euler(weaponHoldRotation);

            Debug.Log($"[PlayerEquipment] Weapon equipped: {weapon.name}");
            if (weapon.CurrentState != WeaponState.Equipped)
                weapon.ShowWeapon();
        }

        private void UnequipWeapon(WeaponInteraction weapon)
        {
            if (weapon == null) return;

            // Remove parent - weapon trở thành independent NetworkObject
            weapon.NetworkObject.TrySetParent((Transform)null);
            weapon.DropWeapon();
            Debug.Log($"[PlayerEquipment] Weapon unequipped: {weapon.name}");
        }

        // API public cho client gọi
        public void SetCurrentWeapon(WeaponInteraction weapon)
        {
            if (!IsOwner) return;
            Debug.Log($"[PlayerEquipment] Requesting to set weapon: {(weapon ? weapon.name : "None")}");
            RequestSetWeaponServerRpc(weapon ? weapon.NetworkObject : default(NetworkObjectReference));
        }
        
        public void SetCurrentWeaponServer(WeaponInteraction weapon)
        {
            currentWeaponRef.Value = weapon ? new NetworkBehaviourReference(weapon) : default;
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSetWeaponServerRpc(NetworkObjectReference weaponObjRef)
        {
            // Tháo súng cũ (DÙNG KIỂU ĐÚNG)
            if (currentWeaponRef.Value.TryGet(out WeaponInteraction prevWeapon))
                UnequipWeapon(prevWeapon);

            if (weaponObjRef.TryGet(out NetworkObject weaponNob))
            {
                var newWeapon = weaponNob.GetComponent<WeaponInteraction>();
                if (newWeapon != null)
                {
                    // bảo đảm ownership đúng chủ
                    if (weaponNob.OwnerClientId != OwnerClientId)
                        weaponNob.ChangeOwnership(OwnerClientId);

                    // set NV (OnWeaponRefChanged server sẽ parent + show)
                    currentWeaponRef.Value = new NetworkBehaviourReference(newWeapon);

                    NotifyEquippedClientRpc(weaponObjRef);
                }
            }
            else
            {
                currentWeaponRef.Value = default;
            }
        }

        [ClientRpc]
        private void NotifyEquippedClientRpc(NetworkObjectReference weaponObjRef)
        {
            // Play sound/animation effects khi equip weapon
            Debug.Log("[PlayerEquipment] Weapon equipped - playing effects");
        }

        // Method để clear weapon khỏi equipment (không despawn)
        public void ClearCurrentWeapon()
        {
            if (!IsOwner) return;
            RequestSetWeaponServerRpc(default);
        }

        // Helper method để lấy current weapon
        public WeaponInteraction GetCurrentWeapon()
        {
            return currentWeaponRef.Value.TryGet(out WeaponInteraction weapon) ? weapon : null;
        }

        // Helper method để check có weapon không
        public bool HasWeapon()
        {
            return currentWeaponRef.Value.TryGet(out _);
        }
    }
}
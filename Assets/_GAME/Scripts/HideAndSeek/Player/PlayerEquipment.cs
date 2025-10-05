using System;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using _GAME.Scripts.HideAndSeek.Player.Rig;
using _GAME.Scripts.Player;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player
{
    public class PlayerEquipment : NetworkBehaviour
    {
        private PlayerRigCtrl _playerRigCtrl;
        private PlayerRigCtrl PlayerRigCtrl
        {
            get
            {
                if (_playerRigCtrl != null) return _playerRigCtrl;

                // Fallbacks an toàn hơn
                _playerRigCtrl = transform.parent?.GetComponentInChildren<PlayerRigCtrl>()
                                ?? transform.GetComponentInParent<PlayerRigCtrl>()
                                ?? transform.root.GetComponentInChildren<PlayerRigCtrl>(true);

                if (_playerRigCtrl == null)
                    Debug.LogWarning("[PlayerEquipment] PlayerRigCtrl not found via fallbacks.");

                return _playerRigCtrl;
            }
        }
        
        public event Action<WeaponInteraction> OnWeaponEquipped;
        public event Action<WeaponInteraction> OnWeaponUnequipped;
        
        private NetworkVariable<NetworkBehaviourReference> currentWeaponRef =
            new NetworkVariable<NetworkBehaviourReference>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        
        public WeaponInteraction CurrentWeaponRef =>
            currentWeaponRef.Value.TryGet(out WeaponInteraction weapon) ? weapon : null;

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
            // Local visual chạy ở mọi máy
            if (prevRef.TryGet(out WeaponInteraction prevWeapon))
            {
                UnEquipWeapon(prevWeapon);
                OnWeaponUnequipped?.Invoke(prevWeapon); 
            }



            if (newRef.TryGet(out WeaponInteraction newWeapon))
            {
                EquipWeapon(newWeapon);
                OnWeaponEquipped?.Invoke(newWeapon);
            }
        }
        
        private void EquipWeapon(WeaponInteraction weapon)
        { 
            if (weapon == null) return;
            
            // ✅ Server: ownership + parenting
            if (IsServer)
            {
                if (weapon.NetworkObject.OwnerClientId != OwnerClientId)
                    weapon.NetworkObject.ChangeOwnership(OwnerClientId);
                weapon.NetworkObject.TrySetParent(this.NetworkObject);

                if (weapon.CurrentState != WeaponState.Equipped)
                    weapon.ShowWeapon();

                // Snap local pose vào anchor trên mọi máy
                //ApplyHoldPoseClientRpc(new NetworkObjectReference(weapon.NetworkObject));
            }

            // ✅ Local (mọi máy): rig/visual; hook OnPreFire chỉ cho owner
            if (PlayerRigCtrl != null)
                PlayerRigCtrl.PickUpWeapon(weapon.RigSetup);

            if (IsOwner && weapon.AttackComponent != null)
            {
                weapon.AttackComponent.OnPreFire -= OnPreFireLocal;
                weapon.AttackComponent.OnPreFire += OnPreFireLocal;
            }
            
            // Chủ động “đánh thức” phía owner (RPC chỉ tới owner)
            var rpcParams = new ClientRpcParams {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };
            NotifyEquippedOwnerClientRpc(new NetworkObjectReference(weapon.NetworkObject), rpcParams);
        }
        
        [ClientRpc]
        private void NotifyEquippedOwnerClientRpc(NetworkObjectReference wepRef, ClientRpcParams rpcParams = default)
        {
            if (!wepRef.TryGet(out var wepNO)) return;

            var wI = wepNO.GetComponentInChildren<WeaponInteraction>();
            if (wI != null && wI.IsEquipped)
            {
                // đảm bảo đã register
                wI.Input.EnableInput();
            }
        }

        private void OnPreFireLocal()
        {
            if (PlayerRigCtrl != null)
                PlayerRigCtrl.EnableAimingRig(true);
        }

        private void UnEquipWeapon(WeaponInteraction weapon)
        {
            if (weapon == null) return;

            // ✅ Server: chỉ gọi DropWeapon() (nó tự TrySetParent(null) + state)
            if (IsServer)
            {
                weapon.DropWeapon();
            }

            // ✅ Local (owner): gỡ hook
            if (IsOwner && weapon.AttackComponent != null)
                weapon.AttackComponent.OnPreFire -= OnPreFireLocal;
        }

        // API client
        public void SetCurrentWeapon(WeaponInteraction weapon)
        {
            if (!IsOwner) return;
            RequestSetWeaponServerRpc(weapon ? weapon.NetworkObject : default(NetworkObjectReference));
        }
        
        // API server (được gọi từ WeaponInteraction.RequestPickupServerRpc)
        public void SetCurrentWeaponServer(WeaponInteraction weapon)
        {
            currentWeaponRef.Value = weapon ? new NetworkBehaviourReference(weapon) : default;
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSetWeaponServerRpc(NetworkObjectReference weaponObjRef)
        {
            // Tháo vũ khí cũ
            if (currentWeaponRef.Value.TryGet(out WeaponInteraction prevWeapon))
                UnEquipWeapon(prevWeapon);

            if (weaponObjRef.TryGet(out NetworkObject weaponNob))
            {
                var newWeapon = weaponNob.GetComponent<WeaponInteraction>();
                if (newWeapon != null)
                {
                    if (weaponNob.OwnerClientId != OwnerClientId)
                        weaponNob.ChangeOwnership(OwnerClientId);

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
            Debug.Log("[PlayerEquipment] Weapon equipped - playing effects");
        }

        [ClientRpc]
        private void ApplyHoldPoseClientRpc(NetworkObjectReference weaponRef)
        {
            if (!weaponRef.TryGet(out NetworkObject wno)) return;

            var weapon = wno.GetComponent<WeaponInteraction>();
            if (weapon == null || PlayerRigCtrl == null) return;
        }

        // Helpers
        public void ClearCurrentWeapon()
        {
            if (!IsOwner) return;
            RequestSetWeaponServerRpc(default);
        }

        public WeaponInteraction GetCurrentWeapon()
        {
            return currentWeaponRef.Value.TryGet(out WeaponInteraction weapon) ? weapon : null;
        }

        public bool HasWeapon()
        {
            return currentWeaponRef.Value.TryGet(out _);
        }

        public void RefeshEquipableItemsForModel()
        {
            var weapon = GetCurrentWeapon();
            if (weapon != null)
            {
                weapon.RefreshEquipModel();
                weapon.gameObject.SetActive(false);
            }
        }

        public void ReEquipWeapon()
        {
            var weapon = GetCurrentWeapon();
            if (weapon == null) return;

            weapon.gameObject.SetActive(true);
            PlayerRigCtrl?.PickUpWeapon(weapon.RigSetup);
        }
    }
}

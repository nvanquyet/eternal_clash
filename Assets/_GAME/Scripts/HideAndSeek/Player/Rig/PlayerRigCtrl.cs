using System;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using _GAME.Scripts.Player;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player.Rig
{
    [Serializable]
    public struct WeaponRig
    {
        public Transform weaponTransform;
        public Transform rightHandGrip;
        public Transform leftHandGrip;

        public Vector3 aimingOffset;  // Offset for aiming position
        public Vector3 holdingOffset; // Offset for holding position
    }

    // Enum để đồng bộ trạng thái rig
    public enum RigState : byte
    {
        None = 0,
        Holding = 1,
        Aiming = 2
    }

    public class PlayerRigCtrl : NetworkBehaviour
    {
        [SerializeField] private PlayerCamera playerCamera;

        // Các thành phần rig cụ thể của bạn – giả định có class PlayerRig riêng
        private PlayerRig playerRig;
        private PlayerEquipment playerEquipment;

        private PlayerRig PlayerRig
        {
            get
            {
                if (playerRig == null)
                {
                    playerRig = GetComponentInChildren<PlayerRig>();
                    if (playerRig != null)
                    {
                        playerRig.SetupConstraint(playerCamera ? playerCamera.AimPoint : null);
                    }
                    else
                    {
                        Debug.LogWarning("[PlayerRigCtrl] PlayerRig not found.");
                    }
                }
                return playerRig;
            }
        }

        private PlayerEquipment PlayerEquipment
        {
            get
            {
                if (playerEquipment == null)
                    playerEquipment = transform.root.GetComponentInChildren<PlayerEquipment>();
                
                return playerEquipment;
            }
        }

        // NetworkVariable để đồng bộ trạng thái rig
        private NetworkVariable<RigState> currentRigState =
            new NetworkVariable<RigState>(RigState.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            currentRigState.OnValueChanged += OnRigStateChanged;
            ApplyRigState(currentRigState.Value);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            currentRigState.OnValueChanged -= OnRigStateChanged;
        }

        private void OnRigStateChanged(RigState oldState, RigState newState)
        {
            Debug.Log($"[PlayerRigCtrl] Rig state changed from {oldState} to {newState}");
            ApplyRigState(newState);
        }
        
        private void ApplyRigState(RigState state)
        {
            // Owner đã chạy local prediction → non-owner mới cần apply
            if (IsOwner) return;
            if (PlayerRig == null) return;

            switch (state)
            {
                case RigState.None:
                    PlayerRig.DisableRig();
                    break;

                case RigState.Holding:
                {
                    var w = PlayerEquipment?.CurrentWeaponRef;
                    if (w != null)
                    {
                        PlayerRig.EnableHandsRig(true);
                        PlayerRig.EnableAimingRig(false);
                        PlayerRig.SetupWeaponRig(w.RigSetup);
                    }
                    else
                    {
                        Debug.LogWarning("[PlayerRigCtrl] RigState=Holding but no weapon found");
                        PlayerRig.DisableRig();
                    }
                    break;
                }

                case RigState.Aiming:
                {
                    var w = PlayerEquipment?.CurrentWeaponRef;
                    if (w != null)
                    {
                        if (playerCamera) playerCamera.SyncAimingPoint();
                        PlayerRig.EnableHandsRig(true);
                        PlayerRig.EnableAimingRig(true);
                    }
                    else
                    {
                        PlayerRig.DisableRig();
                    }
                    break;
                }
            }
        }

        private void DisableAimingFromServer()
        {
            if (IsOwner && currentRigState.Value == RigState.Aiming)
            {
                // Local prediction revert
                EnableAimingRig(false);
            }
        }

        // Public API — chỉ owner gọi
        public void DropWeapon()
        {
            if (!IsOwner)
            {
                Debug.LogWarning("[PlayerRigCtrl] DropWeapon called by non-owner");
                return;
            }

            currentRigState.Value = RigState.None;
            PlayerRig?.ClearWeaponRig();
        }

        public void EnableAimingRig(bool isEnable)
        {
            if (!IsOwner)
            {
                Debug.LogWarning("[PlayerRigCtrl] EnableAimingRig called by non-owner");
                return;
            }

            if (isEnable && currentRigState.Value == RigState.Holding)
            {
                PlayerRig?.EnableAimingRig(true);
                currentRigState.Value = RigState.Aiming;

                // Cho phép aim ngắn rồi tự tắt (tùy gameplay)
                Invoke(nameof(DisableAimingFromServer), 0.12f);
            }
            else if (!isEnable && currentRigState.Value == RigState.Aiming)
            {
                PlayerRig?.EnableAimingRig(false);
                currentRigState.Value = RigState.Holding;
            }
        }

        // Compatibility method cho code cũ
        public void PickUpWeapon(WeaponRig weaponRig)
        {
            if (!IsOwner)
            {
                Debug.LogWarning("[PlayerRigCtrl] PickUpWeapon (legacy) called by non-owner");
                return;
            }

            PlayerRig?.SetupWeaponRig(weaponRig);
            currentRigState.Value = RigState.Holding;
        }

        // Getter helpers
        public RigState GetCurrentRigState() => currentRigState.Value;
        public bool IsAiming() => currentRigState.Value == RigState.Aiming;

        // ====== Anchor helpers cho Equipment ======
        // Tùy dự án, bạn sửa cho trỏ đúng tay cầm. Ở đây trả về weaponRig.weaponTransform nếu có.
        public Transform GetWeaponHoldPoint(int index = 0)
        {
            // Ưu tiên anchor từ weapon hiện tại nếu đã setup
            var weapon = PlayerEquipment?.CurrentWeaponRef;
            if (weapon != null && weapon.RigSetup.weaponTransform != null)
                return weapon.RigSetup.weaponTransform;

            // Fallback: trả về transform của PlayerRig (hoặc camera) để không null
            if (PlayerRig != null && PlayerRig.transform != null)
                return PlayerRig.transform;

            return transform; // cùng lắm trả về root
        }
    }
}

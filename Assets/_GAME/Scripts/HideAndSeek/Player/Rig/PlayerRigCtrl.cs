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

        public Vector3 aimingOffset; // Offset for aiming position
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
        [SerializeField] private PlayerRig playerRig;
        [SerializeField] private PlayerEquipment playerEquipment;
        [SerializeField] private PlayerCamera playerCamera;
        private PlayerRig PlayerRig
        {
            get
            {
                if (playerRig == null)
                {
                    playerRig = GetComponentInChildren<PlayerRig>();
                    if (playerRig != null)
                    {
                        playerRig.SetupConstraint(playerCamera.AimPoint);
                    }
                }

                return playerRig;
            }
        }

        #region Unity Life Circle

        private void OnValidate()
        {
            playerEquipment = transform.parent.GetComponentInChildren<PlayerEquipment>();
        }

        #endregion

        // NetworkVariable để đồng bộ trạng thái rig
        private NetworkVariable<RigState> currentRigState = new NetworkVariable<RigState>(
            RigState.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        

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
            if (IsOwner) return;
            if (PlayerRig == null) return;

            switch (state)
            {
                case RigState.None:
                    PlayerRig.DisableRig();
                    break;

                case RigState.Holding:
                    if (playerEquipment && playerEquipment.CurrentWeaponRef)
                    {
                        PlayerRig.EnableHandsRig(true);
                        PlayerRig.EnableAimingRig(false);
                        PlayerRig.SetupWeaponRig(playerEquipment.CurrentWeaponRef.RigSetup);
                    }else
                    {
                        Debug.LogWarning("[PlayerRigCtrl] RigState is Holding but no weapon found");
                        PlayerRig.DisableRig();
                        // Auto fix state
                        if (IsOwner) currentRigState.Value = RigState.None;
                    }
                    break;

                case RigState.Aiming:
                    if (playerEquipment?.CurrentWeaponRef != null)
                    {
                        playerCamera.SyncAimingPoint();
                        PlayerRig.EnableHandsRig(true);
                        PlayerRig.EnableAimingRig(true);
                    }
                    else
                    {
                        if (IsOwner) currentRigState.Value = RigState.None;
                    }
                    break;
            }
        }

        private void DisableAimingFromServer()
        {
            if (IsOwner && currentRigState.Value == RigState.Aiming)
            {
                //Local prediction
                EnableAimingRig(false);
            }
        }

        // Public API methods - chỉ owner có thể gọi
        public void DropWeapon()
        {
            if (!IsOwner)
            {
                Debug.LogWarning("[PlayerRigCtrl] DropWeapon called by non-owner");
                return;
            }

            Debug.Log($"[PlayerRigCtrl] Owner dropping weapon");
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
                Debug.Log("[PlayerRigCtrl] Enabling aiming rig");
                //Local prediction
                PlayerRig?.EnableAimingRig(true);
                currentRigState.Value = RigState.Aiming;
                Invoke(nameof(DisableAimingFromServer), 0.12f);
            }
            else if (!isEnable && currentRigState.Value == RigState.Aiming)
            {
                Debug.Log("[PlayerRigCtrl] Disabling aiming rig");
                PlayerRig?.EnableAimingRig(false);
                currentRigState.Value = RigState.Holding;
            }
        }

        // Compatibility method for existing code
        public void PickUpWeapon(WeaponRig weaponRig)
        {
            if (!IsOwner)
            {
                Debug.LogWarning("[PlayerRigCtrl] PickUpWeapon (legacy) called by non-owner");
                return;
            }

            Debug.Log($"[PlayerRigCtrl] Owner picking up weapon (legacy method)");
            PlayerRig?.SetupWeaponRig(weaponRig);
            currentRigState.Value = RigState.Holding;
        }

        // Getter methods
        public RigState GetCurrentRigState()
        {
            return currentRigState.Value;
        }

        private bool IsHoldingWeapon()
        {
            return currentRigState.Value != RigState.None;
        }

        public bool IsAiming()
        {
            return currentRigState.Value == RigState.Aiming;
        }
    }
}
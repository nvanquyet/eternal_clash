using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace _GAME.Scripts.Player
{
    public class PlayerCamera : NetworkBehaviour
    {
        [SerializeField] private GameObject mainPlayerCam;
        [SerializeField] private Transform aimPoint;
        
        public Transform AimPoint => aimPoint;
        
        private readonly NetworkVariable<Vector3> _aimPosition = new NetworkVariable<Vector3>(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        
        
        private bool IsLocalOwner => IsOwner && IsClient;
        private Transform CameraTransform => mainPlayerCam.transform;


        private Coroutine _syncCoroutine;
        private readonly float _delayTime = 0.1f;
        
        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                StartCoroutine(IESyncAimPointRoutine());
            }
        }
        public override void OnNetworkDespawn()
        {
            if (_syncCoroutine != null)
            {
                StopCoroutine(_syncCoroutine);
                _syncCoroutine = null;
            }
        }

        private IEnumerator IESyncAimPointRoutine()
        {
            if(!IsOwner) yield break;
            var wait = new WaitForSeconds(_delayTime);
            while (true)
            {
                SyncAimingPointValue();
                yield return wait;
            }
        }


        private void SyncAimingPointValue()
        {
            if(!IsOwner) return;
            _aimPosition.Value = AimPoint.position;
        }

        public void SyncAimingPoint()
        {
            if(IsOwner) return;
            AimPoint.transform.position = _aimPosition.Value;
        }
        
        
        
        public Vector3 GetCameraForward()
        {
            if (IsLocalOwner && CameraTransform != null)
            {
                return CameraTransform.forward;
            }

            // Non-owners: fallback to transform forward
            return transform.forward;
        }

        public Vector3 GetCameraRight()
        {
            if (IsLocalOwner && CameraTransform != null)
            {
                return CameraTransform.right;
            }

            // Non-owners: fallback to transform right
            return transform.right;
        }
        
        public void ActiveCamera(bool isActive = true)
        {
            if (mainPlayerCam != null)
            {
                mainPlayerCam.SetActive(isActive);
            }
        }
    }
}

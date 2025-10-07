using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Player
{
    public class PlayerCamera : NetworkBehaviour
    {
        [SerializeField] private CinemachineCamera mainCamera;
        [SerializeField] private Transform aimPoint;
        [SerializeField] private Transform lookAtPoint;
        [SerializeField] private Transform lookAtAimPoint;
        
        
        [Header("Aiming Settings")]
        [SerializeField] private float normalFOV = 60f;
        [SerializeField] private float aimingFOV = 40f;
        [SerializeField] private float zoomSpeed = 5f;
        
        [Header("Camera Distance (Optional)")]
        [SerializeField] private bool adjustDistanceOnAim = true;
        [SerializeField] private float normalDistance = 5f;
        [SerializeField] private float aimingDistance = 3f;
        
        public Transform AimPoint => aimPoint;
        
        private readonly NetworkVariable<Vector3> _aimPosition = new NetworkVariable<Vector3>(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        
        private bool IsLocalOwner => IsOwner && IsClient;
        private Transform CameraTransform => mainCamera.transform;
        private CinemachineCamera MainCamera => mainCamera;

        private Coroutine _syncCoroutine;
        private Coroutine _zoomCoroutine;
        private readonly float _delayTime = 0.1f;
        
        private float _originalFOV;
        private float _originalDistance;
        private CinemachineFollow _followComponent;
        private CinemachineRotationComposer _rotationComposer;
        private bool _isAiming = false;

        private void Awake()
        {
            // Lưu giá trị FOV gốc
            if (mainCamera != null)
            {
                _originalFOV = mainCamera.Lens.FieldOfView;
                normalFOV = _originalFOV; // Sử dụng giá trị hiện tại làm mặc định
                
                // Lấy Follow component nếu có
                _followComponent = mainCamera.GetComponent<CinemachineFollow>();
                if (_followComponent != null)
                {
                    _originalDistance = _followComponent.FollowOffset.z;
                    normalDistance = Mathf.Abs(_originalDistance);
                }
                
                // Lấy Rotation Composer (LookAt component)
                _rotationComposer = mainCamera.GetComponent<CinemachineRotationComposer>();
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                StartCoroutine(IESyncAimPointRoutine());
                SetAimingMode(false); // Đảm bảo bắt đầu ở chế độ không aiming
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_syncCoroutine != null)
            {
                StopCoroutine(_syncCoroutine);
                _syncCoroutine = null;
            }
            
            if (_zoomCoroutine != null)
            {
                StopCoroutine(_zoomCoroutine);
                _zoomCoroutine = null;
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

        #region Camera Direction Methods

        public Vector3 GetCameraForward()
        {
            if (IsLocalOwner && CameraTransform != null)
            {
                return CameraTransform.forward;
            }
            return transform.forward;
        }

        public Vector3 GetCameraRight()
        {
            if (IsLocalOwner && CameraTransform != null)
            {
                return CameraTransform.right;
            }
            return transform.right;
        }

        #endregion

        #region Aiming Mode

        /// <summary>
        /// Bật/tắt chế độ aiming với zoom và điều chỉnh khoảng cách
        /// </summary>
        public void SetAimingMode(bool isAiming)
        {
            if (!IsLocalOwner)
            {
                Debug.LogWarning("[PlayerCamera] Only local owner can control aiming mode!");
                return;
            }

            if (mainCamera == null)
            {
                Debug.LogWarning("[PlayerCamera] Main camera is not assigned!");
                return;
            }

            _isAiming = isAiming;

            // Dừng coroutine cũ nếu có
            if (_zoomCoroutine != null)
            {
                StopCoroutine(_zoomCoroutine);
            }

            // Đổi target Follow và LookAt
            Transform targetLookAt = isAiming ? lookAtAimPoint : lookAtPoint;
            
            // Cách 1: Sử dụng Follow Target (nếu dùng CinemachineFollow)
            if (_followComponent != null && targetLookAt != null)
            {
                mainCamera.Follow = targetLookAt;
                Debug.Log($"🎯 [PlayerCamera] Changed Follow target to: {targetLookAt.name}");
            }
            
            // Cách 2: Sử dụng LookAt Target (nếu dùng CinemachineRotationComposer)
            if (_rotationComposer != null && targetLookAt != null)
            {
                mainCamera.LookAt = targetLookAt;
                Debug.Log($"👁️ [PlayerCamera] Changed LookAt target to: {targetLookAt.name}");
            }

            // Bắt đầu transition FOV và distance
            float targetFOV = isAiming ? aimingFOV : normalFOV;
            float targetDistance = isAiming ? aimingDistance : normalDistance;
            
            _zoomCoroutine = StartCoroutine(TransitionCamera(targetFOV, targetDistance));
            
            Debug.Log($"🎯 [PlayerCamera] Aiming mode: {(isAiming ? "ON" : "OFF")} - FOV: {targetFOV}, Distance: {targetDistance}");
        }

        /// <summary>
        /// Smooth transition FOV và distance
        /// </summary>
        private IEnumerator TransitionCamera(float targetFOV, float targetDistance)
        {
            float startFOV = mainCamera.Lens.FieldOfView;
            float startDistance = _followComponent != null ? Mathf.Abs(_followComponent.FollowOffset.z) : 0;
            
            float elapsed = 0f;
            float duration = 1f / zoomSpeed;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                
                // Smooth interpolation với easing
                float smoothT = Mathf.SmoothStep(0, 1, t);

                // Lerp FOV
                float currentFOV = Mathf.Lerp(startFOV, targetFOV, smoothT);
                mainCamera.Lens.FieldOfView = currentFOV;

                // Lerp Distance nếu có follow component
                if (adjustDistanceOnAim && _followComponent != null)
                {
                    float currentDistance = Mathf.Lerp(startDistance, targetDistance, smoothT);
                    Vector3 offset = _followComponent.FollowOffset;
                    offset.z = -currentDistance; // Negative vì Cinemachine dùng -Z
                    _followComponent.FollowOffset = offset;
                }

                yield return null;
            }

            // Đảm bảo đạt chính xác giá trị cuối
            mainCamera.Lens.FieldOfView = targetFOV;
            
            if (adjustDistanceOnAim && _followComponent != null)
            {
                Vector3 finalOffset = _followComponent.FollowOffset;
                finalOffset.z = -targetDistance;
                _followComponent.FollowOffset = finalOffset;
            }

            _zoomCoroutine = null;
        }

        /// <summary>
        /// Kiểm tra có đang aiming không
        /// </summary>
        public bool IsAiming => _isAiming;

        /// <summary>
        /// Set FOV ngay lập tức (không smooth)
        /// </summary>
        public void SetFOVImmediate(float fov)
        {
            if (mainCamera != null)
            {
                mainCamera.Lens.FieldOfView = fov;
            }
        }

        /// <summary>
        /// Reset về giá trị gốc
        /// </summary>
        public void ResetToDefault()
        {
            if (!IsLocalOwner) return;

            _isAiming = false;
            
            if (_zoomCoroutine != null)
            {
                StopCoroutine(_zoomCoroutine);
            }

            // Reset về target bình thường
            if (_followComponent != null && lookAtPoint != null)
            {
                mainCamera.Follow = lookAtPoint;
            }
            
            if (_rotationComposer != null && lookAtPoint != null)
            {
                mainCamera.LookAt = lookAtPoint;
            }

            SetFOVImmediate(_originalFOV);
            
            if (_followComponent != null)
            {
                Vector3 offset = _followComponent.FollowOffset;
                offset.z = _originalDistance;
                _followComponent.FollowOffset = offset;
            }
        }

        #endregion

        #region Enable/Disable Camera

        public void EnableCam()
        {
            mainCamera.gameObject.SetActive(true);
        }

        public void DisableCams()
        {
            mainCamera.gameObject.SetActive(false);
        }

        #endregion
    }
}
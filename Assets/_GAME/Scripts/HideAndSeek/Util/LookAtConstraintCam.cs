using System;
using _GAME.Scripts.HideAndSeek.CameraHnS;
using UnityEngine;
using UnityEngine.Animations;

namespace _GAME.Scripts.HideAndSeek.Util
{
    [RequireComponent(typeof(LookAtConstraint))]
    public class LookAtConstraintCam : MonoBehaviour
    {
        [SerializeField] private LookAtConstraint lookAtConstraint;
#if UNITY_EDITOR
        private void OnValidate()
        {
            if (lookAtConstraint == null) lookAtConstraint = GetComponent<LookAtConstraint>();
        }
#endif
        private void OnEnable()
        {
            Invoke(nameof(SetupConstraint), 0.5f);
        }

        private void SetupConstraint()
        {
            // Setup aim target - với null check cho multiplayer
            var cameraInstance = CameraCustom.Instance;
            if (cameraInstance != null && cameraInstance.AimTarget != null)
            {
                var source = new ConstraintSource
                {
                    sourceTransform = cameraInstance.AimTarget,
                    weight = 1f
                };
                lookAtConstraint.AddSource(source);
            }
            else
            {
                Debug.LogWarning("[PlayerRig] Aim target not found - this is normal for non-local players");
            }
        }
        
    }
}
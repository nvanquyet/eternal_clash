// ===== Example: Rifle Implementation =====
using _GAME.Scripts.HideAndSeek.Combat.Base;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Combat.Weapons
{
    public class AssaultRifle : AGun
    {
        [Header("Rifle Specific")]
        [SerializeField] private UnityEngine.Camera playerCamera;
        [SerializeField] private float aimRange = 100f;
        [SerializeField] private LayerMask aimLayerMask = -1;
        
        protected override void Awake()
        {
            base.Awake();
            
            // Find camera if not assigned
            if (playerCamera == null)
            {
                playerCamera = UnityEngine.Camera.main;
            }
        }
        
        protected override Vector3 GetFireDirection()
        {
            if (playerCamera == null)
                return base.GetFireDirection();
            
            // Raycast from camera to get aim direction
            Ray aimRay = playerCamera.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));
            
            Vector3 targetPoint;
            if (Physics.Raycast(aimRay, out RaycastHit hit, aimRange, aimLayerMask))
            {
                targetPoint = hit.point;
            }
            else
            {
                targetPoint = aimRay.origin + aimRay.direction * aimRange;
            }
            
            return (targetPoint - firePoint.position).normalized;
        }
    }
}
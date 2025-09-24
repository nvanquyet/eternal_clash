using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;

namespace _GAME.Scripts.HideAndSeek.Player.Rig
{
    public class PlayerRig : MonoBehaviour
    {
        [SerializeField] private RigBuilder rigBuilder; 
        [SerializeField] private UnityEngine.Animations.Rigging.Rig handsRig; 
        [SerializeField] private UnityEngine.Animations.Rigging.Rig aimingRig;
        
        [SerializeField] private TwoBoneIKConstraint rightHandIK;
        [SerializeField] private TwoBoneIKConstraint leftHandIK;
        
        [SerializeField] private MultiParentConstraint holdingMultiParentConstraint;
        [SerializeField] private MultiPositionConstraint holdingPositionConstraint;
            
        [SerializeField] private MultiParentConstraint aimingMultiParentConstraint;
        [SerializeField] private MultiPositionConstraint aimingPositionConstraint;
        [SerializeField] private LookAtConstraint aimLookAtConstraint;
        
        private bool isInitialized = false;
        
        public void Start()
        {
            if (isInitialized) return;
            
            //Disable all rigs at the start
            EnableHandsRig(false);
            EnableAimingRig(false);
            
            isInitialized = true;
        }
        

        public void SetupWeaponRig(WeaponRig weaponRig)
        {
            if (weaponRig.weaponTransform == null || weaponRig.rightHandGrip == null || weaponRig.leftHandGrip == null)
            {
                Debug.LogError("[PlayerRig] Invalid weapon rig provided.");
                return;
            }
            
            // Set weapon parent to the correct transform (NetworkObject root)
            weaponRig.weaponTransform.SetParent(this.transform);
            weaponRig.weaponTransform.localPosition = Vector3.zero;
            weaponRig.weaponTransform.localRotation = Quaternion.identity;

            //Set the IK targets for hands
            rightHandIK.data.target = weaponRig.rightHandGrip;
            leftHandIK.data.target = weaponRig.leftHandGrip;

            //Set the constraints
            aimingMultiParentConstraint.data.constrainedObject = weaponRig.weaponTransform;
            holdingMultiParentConstraint.data.constrainedObject = weaponRig.weaponTransform;
            
            aimingPositionConstraint.data.offset = weaponRig.aimingOffset;
            holdingPositionConstraint.data.offset = weaponRig.holdingOffset;
            
            // Enable hands rig
            EnableHandsRig(true);
            EnableAimingRig(false);
            
            // Rebuild rig constraints
            if (rigBuilder != null) rigBuilder.Build();
            
            Debug.Log($"[PlayerRig] Weapon rig setup completed for {weaponRig.weaponTransform.name}");
        }

        public void DisableRig()
        {
            EnableHandsRig(false);
            EnableAimingRig(false);
        }
        
        public void ClearWeaponRig()
        {
            Debug.Log("[PlayerRig] Clearing weapon rig");
            
            //Clear IK targets
            if (rightHandIK != null)
                rightHandIK.data.target = null;
            
            if (leftHandIK != null)
                leftHandIK.data.target = null;

            //Clear constraints
            if (holdingMultiParentConstraint != null)
                holdingMultiParentConstraint.data.constrainedObject = null;
            
            if (aimingMultiParentConstraint != null)
                aimingMultiParentConstraint.data.constrainedObject = null;
            
            // Rebuild constraints after clearing
            if (rigBuilder != null) rigBuilder.Build();
        }

        public void EnableHandsRig(bool isEnable)
        {
            if (handsRig != null)
            {
                handsRig.weight = isEnable ? 1f : 0f;
                Debug.Log($"[PlayerRig] Hands rig {(isEnable ? "enabled" : "disabled")}");
            }
        }
        
        public void EnableAimingRig(bool isEnable)
        {
            if (aimingRig != null)
            {
                aimingRig.weight = isEnable ? 1f : 0f;
            }
        }
        
        
        public void SetupConstraint(Transform aimPoint)
        {
            if (aimPoint != null)
            {
                var source = new ConstraintSource
                {
                    sourceTransform = aimPoint,
                    weight = 1f
                };
                aimLookAtConstraint.AddSource(source);
            }
            else
            {
                Debug.LogWarning("[PlayerRig] Aim target not found - this is normal for non-local players");
            }
        }

        // Utility methods
        public bool IsHandsRigEnabled()
        {
            return handsRig != null && handsRig.weight > 0f;
        }

        private bool IsAimingRigEnabled()
        {
            return aimingRig != null && aimingRig.weight > 0f;
        }
        
        // Debug method
        public void LogRigStatus()
        {
            Debug.Log($"[PlayerRig] Status - Hands: {IsHandsRigEnabled()}, Aiming: {IsAimingRigEnabled()}, " +
                     $"Initialized: {isInitialized}");
        }

        // Validation method
        private void OnValidate()
        {
            if (rigBuilder == null)
                rigBuilder = GetComponent<RigBuilder>();
                
            if (handsRig == null || aimingRig == null)
            {
                Debug.LogWarning("[PlayerRig] Missing rig references! Please assign handsRig and aimingRig.");
            }
        }
        
        
    }
}
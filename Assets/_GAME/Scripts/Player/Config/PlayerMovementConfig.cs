using UnityEngine;

namespace _GAME.Scripts.Player.Config
{
    [CreateAssetMenu(fileName = "PlayerMovementConfig", menuName = "Config/Player Movement Config")]
    public class PlayerMovementConfig : ScriptableObject
    {
        [Header("Movement Settings")]
        [SerializeField] private float walkSpeed = 2f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float jumpForce = 5f;

        
        [Header("Dash Config")] 
        [SerializeField] private PlayerDashConfig dashConfig;
        
        [Header("Air Control")]
        [Tooltip("Air control multiplier for jumping state (0-1)")]
        [Range(0f, 1f)]
        [SerializeField] private float jumpAirControlMultiplier = 0.8f;
         
        [Tooltip("Air control multiplier for falling state (0-1)")]
        [Range(0f, 1f)]
        [SerializeField] private float fallAirControlMultiplier = 0.6f;
        
        [Tooltip("Should air control use the speed from previous ground state?")]
        [SerializeField] private bool useLastGroundSpeedForAirControl = true;
        
        [Header("Rotation")]
        [SerializeField] private float rotationSpeed = 10f;
        
        [Header("Physics Settings")]
        [SerializeField] private float gravity = 9.81f;

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed; 
        public float JumpForce => jumpForce;
        public float Gravity => gravity;
        public float JumpAirControlMultiplier => jumpAirControlMultiplier;
        public float FallAirControlMultiplier => fallAirControlMultiplier;
        public float RotationSpeed => rotationSpeed;
        public bool UseLastGroundSpeedForAirControl => useLastGroundSpeedForAirControl;

        public PlayerDashConfig DashConfig => dashConfig;

    }
}
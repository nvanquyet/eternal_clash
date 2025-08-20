using UnityEngine;

namespace _GAME.Scripts.Player.Config
{
    [CreateAssetMenu(fileName = "PlayerDashConfig", menuName = "Player/Dash Config")]
    public class PlayerDashConfig : ScriptableObject
    {
        [Header("Input")]
        public KeyCode DashKeyCode = KeyCode.LeftControl;

        [Header("Ground Dash")]
        [Tooltip("Speed of ground dash")]
        public float GroundDashSpeed = 25f;
        
        [Tooltip("Duration of ground dash in seconds")]
        public float GroundDashDuration = 0.3f;
        
        [Tooltip("Should ground dash ignore gravity?")]
        public bool GroundDashIgnoreGravity = true;

        [Header("Air Dash")]
        [Tooltip("Speed of air dash")]
        public float AirDashSpeed = 20f;
        
        [Tooltip("Duration of air dash in seconds")]
        public float AirDashDuration = 0.25f;
        
        [Tooltip("Should air dash reset Y velocity?")]
        public bool AirDashResetYVelocity = true;
        
        [Tooltip("Y velocity applied during air dash (0 = horizontal only)")]
        public float AirDashYVelocity = 0f;

        [Header("General Settings")]
        [Tooltip("Cooldown between dashes in seconds")]
        public float DashCooldown = 1.5f;
        
        [Tooltip("Maximum number of air dashes before landing")]
        public int MaxAirDashes = 1;
        
        [Tooltip("Should dash direction use input or forward?")]
        public bool UseDashInputDirection = true;
        
        [Header("Animation Curve")]
        [Tooltip("Speed curve for smooth dash (0-1 over duration)")]
        public AnimationCurve DashSpeedCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        
        [Header("Effects")]
        [Tooltip("Should freeze player briefly at start of dash?")]
        public bool DashStartFreeze = true;
        
        [Tooltip("Duration of start freeze in seconds")]
        public float DashStartFreezeDuration = 0.05f;
    }
}
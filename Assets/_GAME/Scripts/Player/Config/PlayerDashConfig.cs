using UnityEngine;

namespace _GAME.Scripts.Player.Config
{
    [CreateAssetMenu(fileName = "PlayerDashConfig", menuName = "Config/Player Dash Config")]
    public class PlayerDashConfig : ScriptableObject
    {
        [Header("Dash Settings")]
        [SerializeField] private float dashSpeed = 10f;
        [SerializeField] private float dashDuration = 0.5f;
        [SerializeField] private float dashCooldown = 1f;
        [SerializeField] private float dashDistance = 5f;

        [Header("Physics Settings")]
        [SerializeField] private float dashGravity = 2f;
        [SerializeField] private float dashAngle = 45f;
        [SerializeField] private float dashFriction = 0.5f;

        [Header("Input Settings")]
        [SerializeField] private KeyCode dashKeyCode = KeyCode.E;

        // Public properties for accessing private fields
        public float DashSpeed => dashSpeed;
        public float DashDuration => dashDuration;
        public float DashCooldown => dashCooldown;
        public float DashDistance => dashDistance;
        public float DashGravity => dashGravity;
        public float DashAngle => dashAngle;
        public float DashFriction => dashFriction;
        public KeyCode DashKeyCode => dashKeyCode;
    }
}
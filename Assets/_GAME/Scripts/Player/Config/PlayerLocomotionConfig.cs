using UnityEngine;
namespace Player.Locomotion
{
    [CreateAssetMenu(fileName = "PlayerLocomotionConfig", menuName = "Config/Player Locomotion Config")]
    public class PlayerLocomotionConfig : ScriptableObject
    {
        [Header("Movement Settings")]
        [SerializeField] private float walkSpeed = 2f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float jumpForce = 5f;

        [Header("Physics Settings")]
        [SerializeField] private float gravity = 9.81f;


        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public float JumpForce => jumpForce;
        public float Gravity => gravity;

    }
}
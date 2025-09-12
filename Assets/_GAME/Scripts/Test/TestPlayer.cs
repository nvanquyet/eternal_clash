using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.Test
{
    public class TestPlayer : NetworkBehaviour
    {
        #region Movement

        [SerializeField] private float moveSpeed = 5f; // tốc độ di chuyển
        
        private Vector2 _moveInput;

        // Được PlayerInput gọi tự động
        public void OnMove(InputValue value)
        {
            _moveInput = value.Get<Vector2>();
        }

        private void Update()
        {
            if (!IsOwner) return; // Chỉ owner mới được di chuyển
            Vector3 dir = new Vector3(_moveInput.x, 0, _moveInput.y);
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            transform.Translate(dir * moveSpeed * Time.deltaTime, Space.World);
        }

        #endregion
    }
}

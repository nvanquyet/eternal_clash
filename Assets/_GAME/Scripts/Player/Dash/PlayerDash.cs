using UnityEngine;
using _GAME.Scripts.Player.Config;

namespace _GAME.Scripts.Player
{
    public class PlayerDash
    {
        private readonly Transform _transform;
        private readonly PlayerDashConfig _dashConfig;
        private readonly CharacterController _characterController;

        private bool _isDashing;
        private float _dashTimer;
        private float _dashCooldownTimer;

        private Vector3 _dashDirection;
        
        public PlayerDash(PlayerDashConfig dashConfig, CharacterController characterController)
        {
            _dashConfig = dashConfig;
            _characterController = characterController;
            _transform = characterController ? _characterController.transform : null;
        }

        public void OnUpdate()
        {
            HandleDashInput();
            UpdateDash();
        }

        private void HandleDashInput()
        {
            if (_isDashing || _dashCooldownTimer > 0) return;

            if (Input.GetKeyDown(_dashConfig.DashKeyCode))
            {
                StartDash();
            }
        }

        private void StartDash()
        {
            _isDashing = true;
            _dashTimer = _dashConfig.DashDuration;
            _dashCooldownTimer = _dashConfig.DashCooldown;

            _dashDirection = _transform ? _transform.forward : Vector3.forward;
        }

        private void UpdateDash()
        {
            if (_isDashing)
            {
                // Thực hiện Dash
                Vector3 dashVelocity = _dashDirection * _dashConfig.DashSpeed;
                _characterController.Move(dashVelocity * Time.deltaTime);

                _dashTimer -= Time.deltaTime;
                if (_dashTimer <= 0)
                {
                    _isDashing = false;
                }
            }
            else
            {
                if (_dashCooldownTimer > 0)
                {
                    _dashCooldownTimer -= Time.deltaTime;
                }
            }
        }
    }
}
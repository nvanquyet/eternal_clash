using _GAME.Scripts.Player.Config;
using _GAME.Scripts.Player.Enum;
using _GAME.Scripts.Player.Locomotion.States;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion
{
    public class PlayerLocomotion
    {
        private readonly Transform _playerTransform;
        private readonly PlayerMovementConfig _config;
        private readonly CharacterController _characterController;
        private readonly Animator _animator;
        private readonly PlayerController _playerController;

        // Internal state
        private Vector3 _velocity;
        private Vector3 _inputDirection;
        private ALocomotionState _currentState;
        
        // Air control
        private float _lastGroundSpeed;
        private bool _wasSprintingBeforeAirborne;
        
        // Dash system
        private float _dashCooldown = 0f;
        private int _airDashesUsed = 0;
        private bool _lastFrameGrounded = true;

        #region Properties
        public CharacterController CharacterController => _characterController;
        public PlayerMovementConfig Config => _config;
        public ALocomotionState CurrentState => _currentState;
        public Vector3 Velocity => _velocity;
        public Vector3 InputDirection => _inputDirection;
        public bool IsGrounded => _characterController != null && _characterController.isGrounded;
        public float LastGroundSpeed => _lastGroundSpeed;
        public bool WasSprintingBeforeAirborne => _wasSprintingBeforeAirborne;
        
        public float DashCooldown => _dashCooldown;
        public int AirDashesUsed => _airDashesUsed;
        public int AirDashesRemaining => Mathf.Max(0, _config.DashConfig.MaxAirDashes - _airDashesUsed);
        #endregion

        public PlayerLocomotion(PlayerMovementConfig config, CharacterController characterController, 
            Animator animator, PlayerController playerController)
        {
            _playerTransform = characterController.transform;
            _config = config;
            _characterController = characterController;
            _animator = animator;
            _playerController = playerController;
            
            SetState(new IdleMotion());
        }

        #region Update Methods
        public void OnUpdate(PlayerInputData inputData, Vector3 forward, Vector3 right)
        {
            // Chỉ owner/server xử lý logic
            if (!_playerController.IsOwner && !_playerController.IsServer) return;
            
            UpdateDashSystem();
            _currentState?.ProcessInput(inputData, this, forward, right);
        }
        
        public void OnUpdate(PlayerInputData inputData)
        {
            // Prediction: owner xử lý logic
            if (!_playerController.IsOwner) return;
            UpdateDashSystem();
            _currentState?.ProcessInput(inputData, this);
        }

        public void OnFixedUpdate(PlayerInputData inputData)
        {
            // Chỉ server xử lý physics
            //if (!_playerController.IsServer) return;
            _currentState?.OnFixedUpdate(inputData, this);
            ApplyGravity();
            
            // CHỈ server move CharacterController
            if (_characterController != null)
            {
                _characterController.Move(_velocity * Time.fixedDeltaTime);
            }
        }
        #endregion 
        #region State Management
        public void SetState(ALocomotionState newState)
        {
            if (newState == null || IsSameStateType(newState)) return;

            _currentState?.OnExit(this);
            _currentState = newState;
            _currentState.OnEnter(this);
        }

        private bool IsSameStateType(ALocomotionState newState)
        {
            return _currentState?.GetType() == newState?.GetType();
        }
        #endregion

        #region Movement Logic
        public void SetVelocity(Vector3 velocity)
        {
            _velocity = velocity;
        }

        public void ApplyVerticalVelocity(float yVelocity)
        {
            _velocity.y = yVelocity;
        }

        public void ApplyMovement(Vector3 inputDirection, float speed)
        {
            _inputDirection = inputDirection * speed;

            // Track ground speed
            if (IsGrounded && inputDirection.magnitude > 0)
            {
                _lastGroundSpeed = speed;
                _wasSprintingBeforeAirborne = speed >= _config.RunSpeed;
            }

            if (inputDirection.magnitude > 0)
            {
                // Camera relative movement
                Vector3 camForward = _playerController.GetCameraForward();
                Vector3 camRight = _playerController.GetCameraRight();
                
                Vector3 moveDir = Vector3.Normalize(camForward * inputDirection.z + camRight * inputDirection.x);
                moveDir.y = 0;

                _velocity.x = moveDir.x * speed;
                _velocity.z = moveDir.z * speed;

                // Rotation
                if (moveDir != Vector3.zero)
                {
                    Quaternion toRotation = Quaternion.LookRotation(moveDir);
                    _playerTransform.rotation = Quaternion.Slerp(
                        _playerTransform.rotation, toRotation, _config.RotationSpeed * Time.deltaTime);
                    // Chỉ xoay khi là server (loại bỏ xoay client-prediction)
                    // if (_playerController.IsServer)
                    // {
                    //     _playerTransform.rotation = Quaternion.Slerp(
                    //         _playerTransform.rotation, toRotation, _config.RotationSpeed * Time.deltaTime);
                    // }
                }
            }
            else
            {
                _velocity.x = 0;
                _velocity.z = 0;
            }
        }

        public void ApplyAirMovement(Vector3 inputDirection, float baseSpeed, float airControlMultiplier)
        {
            _inputDirection = inputDirection * baseSpeed * airControlMultiplier;

            if (inputDirection.magnitude > 0)
            {
                Vector3 camForward = _playerController.GetCameraForward();
                Vector3 camRight = _playerController.GetCameraRight();
                
                Vector3 moveDir = Vector3.Normalize(camForward * inputDirection.z + camRight * inputDirection.x);
                moveDir.y = 0;

                float finalSpeed = baseSpeed * airControlMultiplier;
                
                _velocity.x = Mathf.Lerp(_velocity.x, moveDir.x * finalSpeed, Time.fixedDeltaTime * 5f);
                _velocity.z = Mathf.Lerp(_velocity.z, moveDir.z * finalSpeed, Time.fixedDeltaTime * 5f);

                // Slower air rotation
                if (moveDir != Vector3.zero)
                {
                    Quaternion toRotation = Quaternion.LookRotation(moveDir);
                    _playerTransform.rotation = Quaternion.Slerp(_playerTransform.rotation, toRotation, 
                        _config.RotationSpeed * 0.5f * Time.deltaTime);
                }
            }
        }

        private void ApplyGravity()
        {
            if (!IsGrounded)
            {
                _velocity.y += Physics.gravity.y * Time.fixedDeltaTime;
            }
        }
        #endregion

        #region Dash System

        private void UpdateDashSystem()
        {
            if (_dashCooldown > 0)
                _dashCooldown -= Time.deltaTime;

            bool currentlyGrounded = IsGrounded;
            if (currentlyGrounded && !_lastFrameGrounded)
            {
                _airDashesUsed = 0;
            }

            _lastFrameGrounded = currentlyGrounded;
        }

        public bool CanGroundDash() => _dashCooldown <= 0 && IsGrounded;
        public bool CanAirDash() => _dashCooldown <= 0 && !IsGrounded && _airDashesUsed < _config.DashConfig.MaxAirDashes;
        public bool CanDash() => CanGroundDash() || CanAirDash();

        public void StartDashCooldown() => _dashCooldown = _config.DashConfig.DashCooldown;
        public void ConsumeAirDash() => _airDashesUsed++;
        public void ResetAirDashes() => _airDashesUsed = 0;
        public void ResetDashCooldown() => _dashCooldown = 0f;
        #endregion
    }
}
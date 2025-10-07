using _GAME.Scripts.HideAndSeek;
using _GAME.Scripts.Player.Config;
using _GAME.Scripts.Player.Enum;
using _GAME.Scripts.Player.Locomotion.States;
using QFSW.QC.Actions;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion
{
    public class PlayerLocomotion
    {
        private readonly Transform _playerTransform;
        private readonly PlayerMovementConfig _config;
        private readonly CharacterController _characterController;
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

        // Freeze controls
        private bool _isFreezeMovement = false;
        private bool _isFreezeRotate = false;
        
        // Aiming mode
        private bool _isAimingMode = false;

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
        
        public bool IsFreezeMovement => _isFreezeMovement;
        public bool IsFreezeRotate => _isFreezeRotate;
        public bool IsAimingMode => _isAimingMode;
        #endregion

        public PlayerLocomotion(PlayerMovementConfig config, CharacterController characterController, PlayerController playerController)
        {
            _playerTransform = characterController.transform;
            _config = config;
            _characterController = characterController;
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
            // Track ground speed
            if (IsGrounded && inputDirection.magnitude > 0)
            {
                _lastGroundSpeed = speed;
                _wasSprintingBeforeAirborne = speed >= _config.RunSpeed;
            }

            if (inputDirection.magnitude > 0)
            {
                // Camera relative movement
                Vector3 camForward = _playerController.PlayerCamera.GetCameraForward();
                Vector3 camRight = _playerController.PlayerCamera.GetCameraRight();
                
                Vector3 moveDir = Vector3.Normalize(camForward * inputDirection.z + camRight * inputDirection.x);
                moveDir.y = 0;

                _velocity.x = moveDir.x * ValidateSpeed(speed);
                _velocity.z = moveDir.z * ValidateSpeed(speed);

                // ⭐ QUAN TRỌNG: Chỉ rotate khi KHÔNG đang aiming và không bị freeze
                if (!_isAimingMode && !_isFreezeRotate && moveDir != Vector3.zero)
                {
                    RotateTowards(moveDir, _config.RotationSpeed);
                }
                // Khi aiming, PlayerController sẽ handle rotation theo camera
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
                Vector3 camForward = _playerController.PlayerCamera.GetCameraForward();
                Vector3 camRight = _playerController.PlayerCamera.GetCameraRight();
                
                Vector3 moveDir = Vector3.Normalize(camForward * inputDirection.z + camRight * inputDirection.x);
                moveDir.y = 0;

                float finalSpeed = baseSpeed * airControlMultiplier;
                
                _velocity.x = Mathf.Lerp(_velocity.x, moveDir.x * finalSpeed, Time.fixedDeltaTime * 5f);
                _velocity.z = Mathf.Lerp(_velocity.z, moveDir.z * finalSpeed, Time.fixedDeltaTime * 5f);

                // ⭐ Slower air rotation - cũng bỏ qua khi aiming
                if (!_isAimingMode && !_isFreezeRotate && moveDir != Vector3.zero)
                {
                    RotateTowards(moveDir, _config.RotationSpeed * 0.5f);
                }
            }
        }

        /// <summary>
        /// Helper method để rotate character
        /// </summary>
        private void RotateTowards(Vector3 direction, float rotationSpeed)
        {
            if (direction == Vector3.zero) return;
            
            Quaternion toRotation = Quaternion.LookRotation(direction);
            _playerTransform.rotation = Quaternion.Slerp(
                _playerTransform.rotation, 
                toRotation, 
                rotationSpeed * Time.deltaTime
            );
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
        public bool CanDash() => !IsFreezeMovement && (CanGroundDash() || CanAirDash());

        public void StartDashCooldown() => _dashCooldown = _config.DashConfig.DashCooldown;
        public void ConsumeAirDash() => _airDashesUsed++;
        public void ResetAirDashes() => _airDashesUsed = 0;
        public void ResetDashCooldown() => _dashCooldown = 0f;
        #endregion
        
        #region Freeze Controls
        
        /// <summary>
        /// Freeze/unfreeze movement
        /// </summary>
        public void SetFreezeMovement(bool freeze)
        {
            _isFreezeMovement = freeze;
            Debug.Log($"❄️ [PlayerLocomotion] Movement {(freeze ? "FROZEN" : "UNFROZEN")}");
        }
        
        /// <summary>
        /// Freeze/unfreeze rotation
        /// </summary>
        public void SetFreezeRotate(bool freeze)
        {
            _isFreezeRotate = freeze;
            Debug.Log($"🔒 [PlayerLocomotion] Rotation {(freeze ? "LOCKED" : "UNLOCKED")}");
        }
        
        /// <summary>
        /// Validate speed - return 0 nếu movement bị freeze
        /// </summary>
        private float ValidateSpeed(float speed)
        {
            if (_isFreezeMovement) return 0f;
            
            // Có thể giảm tốc độ khi aiming
            if (_isAimingMode)
            {
                return speed * 0.7f; // Giảm 30% tốc độ khi aiming
            }
            
            return speed;
        }
        
        #endregion

        #region Aiming Mode
        
        /// <summary>
        /// Bật/tắt aiming mode
        /// Khi bật: không rotate character khi di chuyển
        /// </summary>
        public void SetAimingMode(bool isAiming)
        {
            _isAimingMode = isAiming;
            
            // Optional: có thể tự động freeze rotation khi aiming
            // _isFreezeRotate = isAiming;
            
            Debug.Log($"🎯 [PlayerLocomotion] Aiming mode: {(isAiming ? "ENABLED" : "DISABLED")}");
        }
        
        #endregion
    }
}
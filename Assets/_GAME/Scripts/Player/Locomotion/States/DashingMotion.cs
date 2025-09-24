using _GAME.Scripts.Player.Config;
using _GAME.Scripts.Player.Enum;
using UnityEngine;

namespace _GAME.Scripts.Player.Locomotion.States
{
    // Non-static Dashing State for proper networking
    public class DashingMotion : ALocomotionState
    {
        public enum DashType
        {
            Ground,
            Air
        }

        public override LocomotionState LocomotionState => LocomotionState.Dashing;

        // Dash configuration and state
        private readonly DashType _dashType;
        private readonly PlayerDashConfig _dashConfig;
        private readonly Vector3 _dashDirection;
        private readonly float _dashDuration;
        private readonly float _dashSpeed;

        // Runtime state
        private float _dashTimer;
        private float _dashStartFreezeTimer;
        private float _dashNormalizedTime;

        #region Properties
        public DashType CurrentDashType => _dashType;
        public float DashProgress => _dashNormalizedTime;
        public Vector3 DashVelocity => _dashDirection * GetCurrentDashSpeed();
        public bool IsInStartFreeze => _dashStartFreezeTimer > 0;
        #endregion

        // Constructor for creating dash state
        public DashingMotion(DashType dashType, PlayerDashConfig dashConfig, Vector3 dashDirection)
        {
            _dashType = dashType;
            _dashConfig = dashConfig;
            _dashDirection = dashDirection.normalized;
            _dashDuration = dashType == DashType.Ground ? dashConfig.GroundDashDuration : dashConfig.AirDashDuration;
            _dashSpeed = dashType == DashType.Ground ? dashConfig.GroundDashSpeed : dashConfig.AirDashSpeed;
        }

        // Static method to attempt dash (call from any state)
        public static bool TryStartDash(PlayerInputData input, PlayerLocomotion locomotion)
        {
            // Must press dash
            if (!input.dashPressed) return false;

            // Only allow dash in movement states
            var state = locomotion.CurrentState.LocomotionState;
            bool stateOk = state == LocomotionState.Walking ||
                          state == LocomotionState.Running ||
                          state == LocomotionState.Jumping ||
                          state == LocomotionState.Falling;

            if (!stateOk) return false;

            // Must have movement intent: input or horizontal speed
            Vector2 horizVel = new Vector2(locomotion.Velocity.x, locomotion.Velocity.z);
            bool hasMoveIntent = input.moveInput.sqrMagnitude > 0.01f || horizVel.sqrMagnitude > 0.01f;

            if (!hasMoveIntent) return false;

            // Check dash availability
            if (!locomotion.CanDash()) return false;

            PlayerDashConfig dashConfig = locomotion.Config.DashConfig;
            Vector3 dashDirection = CalculateDashDirection(input, locomotion.CharacterController.transform, 
                dashConfig, locomotion);

            if (locomotion.CanGroundDash())
            {
                StartDash(DashType.Ground, dashDirection, locomotion, dashConfig);
                return true;
            }
            else if (locomotion.CanAirDash())
            {
                StartDash(DashType.Air, dashDirection, locomotion, dashConfig);
                locomotion.ConsumeAirDash();
                return true;
            }

            return false;
        }
        
        private static void StartDash(DashType dashType, Vector3 direction, PlayerLocomotion locomotion,
            PlayerDashConfig config)
        {
            // Set cooldown
            locomotion.StartDashCooldown();

            // Create and transition to dash state
            var dashState = new DashingMotion(dashType, config, direction);
            locomotion.SetState(dashState);
        }

        private static Vector3 CalculateDashDirection(PlayerInputData input, Transform playerTransform,
            PlayerDashConfig config, PlayerLocomotion locomotion)
        {
            Vector3 direction;

            if (config.UseDashInputDirection && input.moveInput.magnitude > 0.1f)
            {
                // Get PlayerController reference safely
                PlayerController playerController = locomotion.CharacterController.GetComponentInParent<PlayerController>();
                if (playerController != null)
                {
                    Vector3 camForward = playerController.PlayerCamera.GetCameraForward();
                    Vector3 camRight = playerController.PlayerCamera.GetCameraRight();
                    
                    direction = Vector3.Normalize(camForward * input.moveInput.y + camRight * input.moveInput.x);
                    direction.y = 0; // Keep horizontal
                }
                else
                {
                    // Fallback to transform direction
                    direction = new Vector3(input.moveInput.x, 0, input.moveInput.y).normalized;
                }
            }
            else
            {
                // Use forward direction
                direction = playerTransform.forward;
            }

            return direction;
        }

        public override void OnEnter(PlayerLocomotion locomotion)
        {
            _dashTimer = _dashDuration;
            _dashNormalizedTime = 0f;

            // Set freeze timer
            if (_dashConfig.DashStartFreeze)
            {
                _dashStartFreezeTimer = _dashConfig.DashStartFreezeDuration;
            }

            // Handle air dash Y velocity reset
            if (_dashType == DashType.Air && _dashConfig.AirDashResetYVelocity)
            {
                Vector3 currentVel = locomotion.Velocity;
                locomotion.SetVelocity(new Vector3(currentVel.x, _dashConfig.AirDashYVelocity, currentVel.z));
            }
        }

        public override void ProcessInput(PlayerInputData input, PlayerLocomotion locomotion)
        {
            // Dash state doesn't process input - it's committed to the dash
        }

        public override void OnFixedUpdate(PlayerInputData input, PlayerLocomotion locomotion)
        {
            UpdateTimers();

            // Check if dash finished
            if (_dashTimer <= 0)
            {
                EndDash(input, locomotion);
                return;
            }

            // Apply dash movement
            ApplyDashMovement(locomotion);
        }

        private void UpdateTimers()
        {
            // Update dash timer
            if (_dashTimer > 0)
                _dashTimer -= Time.fixedDeltaTime;

            // Update freeze timer  
            if (_dashStartFreezeTimer > 0)
                _dashStartFreezeTimer -= Time.fixedDeltaTime;

            // Update normalized time
            _dashNormalizedTime = 1f - (_dashTimer / _dashDuration);
        }

        private void ApplyDashMovement(PlayerLocomotion locomotion)
        {
            if (_dashStartFreezeTimer > 0) return;

            float currentSpeed = GetCurrentDashSpeed();
            Vector3 dashMovement = _dashDirection * currentSpeed * Time.fixedDeltaTime;

            // Air dash: only interfere with Y if config requires it
            if (_dashType == DashType.Air)
            {
                if (_dashConfig.AirDashResetYVelocity)
                {
                    var cur = locomotion.Velocity;
                    locomotion.SetVelocity(new Vector3(cur.x, _dashConfig.AirDashYVelocity, cur.z));
                }
                // Don't set horizontal velocity for air dash to keep animation stable
            }
            else // Ground dash
            {
                // Ground dash: ensure y=0 when on ground
                var cur = locomotion.Velocity;
                locomotion.SetVelocity(new Vector3(cur.x, 0f, cur.z));
            }

            // Move instantly using CC.Move to still have dash force in the world
            locomotion.CharacterController.Move(dashMovement);
        }

        private float GetCurrentDashSpeed()
        {
            // Apply animation curve for smooth speed progression
            float curveMultiplier = _dashConfig.DashSpeedCurve.Evaluate(_dashNormalizedTime);
            return _dashSpeed * curveMultiplier;
        }

        private void EndDash(PlayerInputData input, PlayerLocomotion locomotion)
        {
            // Determine next state based on current conditions
            if (locomotion.IsGrounded)
            {
                // Landed - check for movement input
                if (input.moveInput.magnitude > 0.1f)
                {
                    var nextState = input.sprintHeld ? new RunningMotion() : new WalkingMotion();
                    TransitionTo(locomotion, nextState);
                }
                else
                {
                    TransitionTo(locomotion, new IdleMotion());
                }
            }
            else
            {
                // Still airborne - go to falling state
                TransitionTo(locomotion, new FallingMotion());
            }
        }

        public override void OnExit(PlayerLocomotion locomotion)
        {
            // Reset any dash-specific effects if needed
            base.OnExit(locomotion);
        }
    }
}
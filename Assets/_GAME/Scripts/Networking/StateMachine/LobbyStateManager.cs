using System;
using System.Threading.Tasks;
using _GAME.Scripts.Networking.Relay;
using GAME.Scripts.DesignPattern;
using UnityEngine;

namespace _GAME.Scripts.Networking.StateMachine
{
    /// <summary>
    /// Tối ưu hóa LobbyStateManager với better state tracking và validation
    /// </summary>
    public class LobbyStateManager : MonoBehaviour
    {
        #region Fields & Properties

        private LobbyStateMachine _stateMachine;
        private LobbyState _previousState = LobbyState.Default;
        private DateTime _stateEntryTime;
        private float _stateTimeoutDuration = 60f; // Default timeout
        
        [Header("State Configuration")]
        [SerializeField] private bool enableStateLogging = true;
        [SerializeField] private bool enableStateTimeout = true;
        [SerializeField] private float defaultTimeoutSeconds = 60f;

        public LobbyState CurrentState => _stateMachine?.CurrentState ?? LobbyState.Default;
        public LobbyState PreviousState => _previousState;
        public ILobbyState CurrentStateInstance => _stateMachine?.CurrentStateInstance;
        public float TimeInCurrentState => (float)(DateTime.UtcNow - _stateEntryTime).TotalSeconds;
        public bool IsInTransitionalState => CurrentState.IsTransitional();

        // Events
        public event Action<LobbyState, LobbyState> OnStateChanged;
        public event Action<LobbyState> OnStateTimeout;

        #endregion

        #region Unity Lifecycle
        
        private void Awake()
        {
            _stateMachine = new LobbyStateMachine(this);
            _stateMachine.OnStateChanged += HandleStateChanged;
            _stateEntryTime = DateTime.UtcNow;
            
            // Initialize timeout checking
            if (enableStateTimeout)
            {
                InvokeRepeating(nameof(CheckStateTimeout), 1f, 1f);
            }

            Debug.Log("[LobbyStateManager] Initialized with state: " + CurrentState);
        }

        private void OnDestroy()
        {
            if (_stateMachine != null)
            {
                _stateMachine.OnStateChanged -= HandleStateChanged;
                _stateMachine.Dispose();
                _stateMachine = null;
            }

            OnStateChanged = null;
            OnStateTimeout = null;
        }

        #endregion

        #region State Transition Methods

        /// <summary>
        /// Thử chuyển đổi state với validation
        /// </summary>
        public bool TryTransition(LobbyState targetState, object context = null)
        {
            if (!ValidateTransition(targetState))
            {
                return false;
            }

            return _stateMachine.TransitionTo(targetState);
        }

        /// <summary>
        /// Chuyển đổi state bất đồng bộ với context
        /// </summary>
        public async Task<bool> TryTransitionAsync(LobbyState targetState, object context = null)
        {
            if (!ValidateTransition(targetState))
            {
                return false;
            }

            return await _stateMachine.TransitionToAsync(targetState, context);
        }

        /// <summary>
        /// Force chuyển state mà không cần validation (dùng cho recovery)
        /// </summary>
        public bool ForceTransition(LobbyState targetState, object context = null)
        {
            if (enableStateLogging)
            {
                Debug.LogWarning($"[LobbyStateManager] Force transition: {CurrentState} -> {targetState}");
            }

            return _stateMachine.TransitionTo(targetState);
        }

        /// <summary>
        /// Chuyển về idle state một cách an toàn
        /// </summary>
        public async Task<bool> SafeReturnToDefaultAsync(string reason = null)
        {
            try
            {
                if (enableStateLogging && !string.IsNullOrEmpty(reason))
                {
                    Debug.Log($"[LobbyStateManager] Safe return to idle: {reason}");
                }

                // Cleanup current operations
                await CleanupCurrentState();

                // Force transition to idle
                return ForceTransition(LobbyState.Default);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyStateManager] SafeReturnToIdleAsync failed: {ex.Message}");
                return ForceTransition(LobbyState.Failed);
            }
        }

        #endregion

        #region Validation & Utilities

        /// <summary>
        /// Validate transition logic
        /// </summary>
        private bool ValidateTransition(LobbyState targetState)
        {
            if (_stateMachine == null)
            {
                Debug.LogError("[LobbyStateManager] StateMachine not initialized");
                return false;
            }

            // Check if current state allows this transition
            var currentStateInstance = _stateMachine.CurrentStateInstance;
            if (currentStateInstance != null && !currentStateInstance.CanTransitionTo(targetState))
            {
                if (enableStateLogging)
                {
                    Debug.LogWarning($"[LobbyStateManager] Invalid transition blocked: {CurrentState} -> {targetState}");
                }
                return false;
            }

            // Additional business logic validation
            return ValidateBusinessLogic(CurrentState, targetState);
        }

        /// <summary>
        /// Business logic validation for transitions
        /// </summary>
        private bool ValidateBusinessLogic(LobbyState fromState, LobbyState toState)
        {
            // Example validations
            switch (toState)
            {
                case LobbyState.LobbyActive:
                    if (!LobbyManager.Instance.IsInLobby)
                    {
                        Debug.LogWarning("[LobbyStateManager] Cannot be active without lobby");
                        return false;
                    }
                    break;
            }

            return true;
        }

        /// <summary>
        /// Cleanup operations khi cần thiết
        /// </summary>
        private async Task CleanupCurrentState()
        {
            try
            {
                //Todo: Cleanup
                await Task.Delay(100); // Brief delay for cleanup
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyStateManager] Cleanup failed: {ex.Message}");
            }
        }

        #endregion

        #region State Monitoring & Timeout

        /// <summary>
        /// Kiểm tra timeout cho states
        /// </summary>
        private void CheckStateTimeout()
        {
            if (!enableStateTimeout || !IsInTransitionalState) 
                return;

            var timeoutDuration = GetTimeoutForState(CurrentState);
            if (TimeInCurrentState > timeoutDuration)
            {
                if (enableStateLogging)
                {
                    Debug.LogWarning($"[LobbyStateManager] State timeout: {CurrentState} ({TimeInCurrentState:F1}s)");
                }

                OnStateTimeout?.Invoke(CurrentState);
                HandleStateTimeout(CurrentState);
            }
        }

        /// <summary>
        /// Lấy timeout duration cho từng state
        /// </summary>
        private float GetTimeoutForState(LobbyState state)
        {
            return state switch
            {
                LobbyState.CreatingLobby => 30f,
                LobbyState.JoiningLobby => 30f,
                LobbyState.LeavingLobby => 15f,
                LobbyState.RemovingLobby => 15f,
                _ => defaultTimeoutSeconds
            };
        }

        /// <summary>
        /// Handle timeout cho các states
        /// </summary>
        private void HandleStateTimeout(LobbyState timedOutState)
        {
            switch (timedOutState)
            {
                case LobbyState.CreatingLobby:
                case LobbyState.JoiningLobby:
                    // Return to idle on lobby operation timeout
                    _ = SafeReturnToDefaultAsync("Lobby operation timeout");
                    break;

                case LobbyState.LeavingLobby:
                case LobbyState.RemovingLobby:
                    // Force to idle if leaving takes too long
                    ForceTransition(LobbyState.Default);
                    break;

                default:
                    // Generic timeout handling
                    TryTransition(LobbyState.Failed);
                    break;
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handle state change events
        /// </summary>
        private void HandleStateChanged(LobbyState oldState, LobbyState newState)
        {
            _previousState = oldState;
            _stateEntryTime = DateTime.UtcNow;

            if (enableStateLogging)
            {
                Debug.Log($"[LobbyStateManager] State changed: {oldState} -> {newState}");
            }

            // // Analytics tracking
            // AnalyticsManager.Instance?.TrackEvent("lobby_state_change", new 
            // {
            //     from_state = oldState.ToString(),
            //     to_state = newState.ToString(),
            //     time_in_previous = _previousState != LobbyState.None ? TimeInCurrentState : 0f
            // });

            // Trigger event
            OnStateChanged?.Invoke(oldState, newState);
        }

        #endregion

        #region Public Utilities

        /// <summary>
        /// Kiểm tra có thể transition tới state không
        /// </summary>
        public bool CanTransitionTo(LobbyState targetState)
        {
            return ValidateTransition(targetState);
        }

        /// <summary>
        /// Lấy thông tin chi tiết về state hiện tại
        /// </summary>
        public LobbyStateInfo GetCurrentStateInfo()
        {
            return new LobbyStateInfo
            {
                CurrentState = CurrentState,
                PreviousState = PreviousState,
                TimeInState = TimeInCurrentState,
                IsTransitional = IsInTransitionalState,
                CanTimeout = enableStateTimeout && IsInTransitionalState,
                TimeoutDuration = GetTimeoutForState(CurrentState),
                DisplayName = CurrentState.GetDisplayName()
            };
        }

        /// <summary>
        /// Subscribe to state change events
        /// </summary>
        public void Subscribe(Action<LobbyState, LobbyState> onChanged)
        {
            OnStateChanged += onChanged;
        }

        /// <summary>
        /// Unsubscribe from state change events
        /// </summary>
        public void Unsubscribe(Action<LobbyState, LobbyState> onChanged)
        {
            OnStateChanged -= onChanged;
        }

        /// <summary>
        /// Emergency reset - chỉ dùng khi system bị stuck
        /// </summary>
        public void EmergencyReset(string reason = "Emergency reset")
        {
            Debug.LogWarning($"[LobbyStateManager] {reason}");
            
            try
            {
                // Cancel all current operations
                RelayHandler.CancelCurrentOperation();
                
                // Force to failed state first, then to idle
                ForceTransition(LobbyState.Failed);
                
                // Schedule return to idle after brief delay
                Invoke(nameof(DelayedReturnToIdle), 1f);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyStateManager] Emergency reset failed: {ex.Message}");
            }
        }

        private void DelayedReturnToIdle()
        {
            ForceTransition(LobbyState.Default);
        }

        #endregion

        #region Debug & Editor

        [ContextMenu("Debug State Info")]
        private void DebugStateInfo()
        {
            var info = GetCurrentStateInfo();
            Debug.Log($"[LobbyStateManager] State Info:" +
                     $"\n  Current: {info.CurrentState} ({info.DisplayName})" +
                     $"\n  Previous: {info.PreviousState}" +
                     $"\n  Time in State: {info.TimeInState:F1}s" +
                     $"\n  Is Transitional: {info.IsTransitional}" +
                     $"\n  Can Timeout: {info.CanTimeout}" +
                     $"\n  Timeout Duration: {info.TimeoutDuration:F1}s");
        }

        [ContextMenu("Force Return to Idle")]
        private void ForceReturnToIdle()
        {
            _ = SafeReturnToDefaultAsync("Manual force return");
        }

        #if UNITY_EDITOR
        private void OnGUI()
        {
            if (!enableStateLogging) return;
            
            GUILayout.BeginArea(new Rect(10, 10, 300, 100));
            GUILayout.Label($"Lobby State: {CurrentState.GetDisplayName()}");
            GUILayout.Label($"Time: {TimeInCurrentState:F1}s");
            if (IsInTransitionalState)
            {
                GUILayout.Label($"Timeout: {GetTimeoutForState(CurrentState):F1}s");
            }
            GUILayout.EndArea();
        }
        #endif

        #endregion
    }

    #region Supporting Types

    /// <summary>
    /// Information về state hiện tại
    /// </summary>
    public struct LobbyStateInfo
    {
        public LobbyState CurrentState;
        public LobbyState PreviousState;
        public float TimeInState;
        public bool IsTransitional;
        public bool CanTimeout;
        public float TimeoutDuration;
        public string DisplayName;
    }

    #endregion
}
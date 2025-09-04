using System;
using System.Threading.Tasks;
using GAME.Scripts.DesignPattern;
using UnityEngine;

namespace _GAME.Scripts.Networking.StateMachine
{
    /// <summary>
    /// Quản lý state của Network với architecture tương tự LobbyStateManager
    /// </summary>
    public class NetworkStateManager
    {
        #region Fields & Properties

        private NetworkStateMachine stateMachine;
        private NetworkState previousState = NetworkState.Default;

        public NetworkState CurrentState => stateMachine?.CurrentState ?? NetworkState.Default;
        private NetworkState PreviousState => previousState;

        public bool IsInitialized => stateMachine != null;
        public bool IsTransitioning { get; private set; }

        // Events
        public event Action<NetworkState, NetworkState> OnStateChanged;
        public event Action<NetworkState> OnStateEntered;
        public event Action<NetworkState> OnStateExited;

        #endregion

        #region Lifecycle

        public void Init()
        {
            if (IsInitialized)
            {
                Debug.LogWarning("[NetworkStateManager] Already initialized");
                return;
            }

            stateMachine = new NetworkStateMachine(this);
            stateMachine.OnStateChanged += HandleStateChanged;
            Debug.Log("[NetworkStateManager] Initialized with state: " + CurrentState);
        }

        public void Clear()
        {
            if (stateMachine != null)
            {
                stateMachine.OnStateChanged -= HandleStateChanged;
                stateMachine.Dispose();
                stateMachine = null;
            }

            // Clear all events
            OnStateChanged = null;
            OnStateEntered = null;
            OnStateExited = null;
            
            previousState = NetworkState.Default;
            IsTransitioning = false;
            
            Debug.Log("[NetworkStateManager] Cleared");
        }

        #endregion

        #region State Transition Methods

        /// <summary>
        /// Thử chuyển đổi state với validation (sync)
        /// </summary>
        public bool TryTransition(NetworkState targetState, object context = null)
        {
            if (IsTransitioning)
            {
                Debug.LogWarning($"[NetworkStateManager] Cannot transition while already transitioning to {targetState}");
                return false;
            }

            if (!ValidateTransition(targetState))
                return false;

            IsTransitioning = true;
            bool result = stateMachine.TransitionTo(targetState);
            IsTransitioning = false;
            
            return result;
        }

        /// <summary>
        /// Chuyển đổi state bất đồng bộ với context
        /// </summary>
        public async Task<bool> TryTransitionAsync(NetworkState targetState, object context = null)
        {
            if (IsTransitioning)
            {
                Debug.LogWarning($"[NetworkStateManager] Cannot transition while already transitioning to {targetState}");
                return false;
            }

            if (!ValidateTransition(targetState))
                return false;

            IsTransitioning = true;
            bool result = await stateMachine.TransitionToAsync(targetState, context);
            IsTransitioning = false;
            
            return result;
        }

        /// <summary>
        /// Force chuyển đổi state không cần validation (chỉ dùng cho emergency cases)
        /// </summary>
        public async Task<bool> ForceTransitionAsync(NetworkState targetState, object context = null)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[NetworkStateManager] StateMachine not initialized");
                return false;
            }

            Debug.LogWarning($"[NetworkStateManager] Force transition to {targetState}");
            
            IsTransitioning = true;
            bool result = await stateMachine.TransitionToAsync(targetState, context);
            IsTransitioning = false;
            
            return result;
        }

        /// <summary>
        /// Chuyển về default state một cách an toàn
        /// </summary>
        public async Task<bool> SafeReturnToDefaultAsync(string reason = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(reason))
                {
                    Debug.Log($"[NetworkStateManager] Safe return to default: {reason}");
                }

                // Cleanup current operations
                await CleanupCurrentState();

                // Force transition to default
                return await ForceTransitionAsync(NetworkState.Default);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkStateManager] SafeReturnToDefaultAsync failed: {ex.Message}");
                return await ForceTransitionAsync(NetworkState.Failed, ex.Message);
            }
        }

        /// <summary>
        /// Reset toàn bộ state manager về trạng thái ban đầu
        /// </summary>
        public async Task ResetAsync()
        {
            Debug.Log("[NetworkStateManager] Resetting...");
            
            // Cleanup và về default
            await SafeReturnToDefaultAsync("Reset requested");
            
            // Clear và reinit
            Clear();
            await Task.Yield(); // Đảm bảo cleanup hoàn tất
            Init();
        }

        #endregion

        #region Validation & Utilities

        /// <summary>
        /// Validate transition logic
        /// </summary>
        private bool ValidateTransition(NetworkState targetState)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[NetworkStateManager] StateMachine not initialized");
                return false;
            }

            if (CurrentState == targetState)
            {
                Debug.LogWarning($"[NetworkStateManager] Already in state: {targetState}");
                return false;
            }

            // Check if current state allows this transition
            var currentStateInstance = stateMachine.CurrentStateInstance;
            bool canTransition = currentStateInstance == null || currentStateInstance.CanTransitionTo(targetState);
            
            if (!canTransition)
            {
                Debug.LogWarning($"[NetworkStateManager] Invalid transition: {CurrentState} → {targetState}");
            }
            
            return canTransition;
        }
        
        /// <summary>
        /// Cleanup operations khi cần thiết
        /// </summary>
        private async Task CleanupCurrentState()
        {
            try
            {
                Debug.Log($"[NetworkStateManager] Cleaning up state: {CurrentState}");
                
                // Cleanup based on current state
                switch (CurrentState)
                {
                    case NetworkState.ClientConnecting:
                        // Cancel connection attempts, cleanup connecting resources
                        break;
                        
                    case NetworkState.Connected:
                        // Graceful disconnect, save state if needed
                        break;
                        
                    case NetworkState.Disconnecting:
                        // Wait for disconnect to complete
                        break;
                        
                    default:
                        // No specific cleanup needed
                        break;
                }

                await Task.Delay(100); // Brief delay for cleanup operations
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkStateManager] Cleanup failed: {ex.Message}");
            }
        }

        #endregion

        #region Query Methods

        /// <summary>
        /// Kiểm tra có thể transition tới state không
        /// </summary>
        public bool CanTransitionTo(NetworkState targetState)
        {
            return ValidateTransition(targetState);
        }

        /// <summary>
        /// Kiểm tra có đang trong một nhóm states cụ thể không
        /// </summary>
        public bool IsInAnyState(params NetworkState[] states)
        {
            foreach (var state in states)
            {
                if (CurrentState == state)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Lấy thông tin chi tiết về state hiện tại
        /// </summary>
        public NetworkStateInfo GetCurrentStateInfo()
        {
            return new NetworkStateInfo
            {
                CurrentState = CurrentState,
                PreviousState = PreviousState,
                DisplayName = CurrentState.GetDisplayName(),
                IsTransitioning = IsTransitioning,
                IsInitialized = IsInitialized
            };
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handle state change events
        /// </summary>
        private void HandleStateChanged(NetworkState oldState, NetworkState newState)
        {
            previousState = oldState;
            
            // Trigger events
            OnStateExited?.Invoke(oldState);
            OnStateEntered?.Invoke(newState);
            OnStateChanged?.Invoke(oldState, newState);
            
            Debug.Log($"[NetworkStateManager] State changed: {oldState} → {newState}");
        }

        #endregion

        #region Debug & Editor

        [ContextMenu("Debug State Info")]
        private void DebugStateInfo()
        {
            var info = GetCurrentStateInfo();
            Debug.Log($"[NetworkStateManager] State Info:" +
                     $"\n  Current: {info.CurrentState} ({info.DisplayName})" +
                     $"\n  Previous: {info.PreviousState}" +
                     $"\n  Is Transitioning: {info.IsTransitioning}" +
                     $"\n  Is Initialized: {info.IsInitialized}");
        }

        [ContextMenu("Force Return to Default")]
        private void ForceReturnToDefault()
        {
            _ = SafeReturnToDefaultAsync("Manual force return");
        }
        
        [ContextMenu("Reset State Manager")]
        private void ResetStateManager()
        {
            _ = ResetAsync();
        }

        #if UNITY_EDITOR
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(320, 10, 300, 120));
            GUILayout.Label($"Network State: {CurrentState.GetDisplayName()}");
            if (IsTransitioning)
                GUILayout.Label("(Transitioning...)");
            GUILayout.Label($"Previous: {PreviousState}");
            GUILayout.EndArea();
        }
        #endif

        #endregion
    }

    #region Supporting Types

    /// <summary>
    /// Information về network state hiện tại
    /// </summary>
    public struct NetworkStateInfo
    {
        public NetworkState CurrentState;
        public NetworkState PreviousState;
        public string DisplayName;
        public bool IsTransitioning;
        public bool IsInitialized;
    }

    #endregion
}
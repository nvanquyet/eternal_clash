// LobbyManager.cs — yêu cầu/khởi tạo LobbyRuntime, bỏ 2 script cũ

using System;
using System.Threading;
using System.Threading.Tasks;
using _GAME.Scripts.Lobbies;
using _GAME.Scripts.Networking.Lobbies;
using _GAME.Scripts.Networking.StateMachine;
using _GAME.Scripts.Networking.Relay;
using GAME.Scripts.DesignPattern;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking
{
    /// <summary>
    /// Improved LobbyManager với thread-safe state management và resource cleanup
    /// (đã chuyển sang dùng LobbyRuntime thay cho LobbyHeartbeat + LobbyUpdater)
    /// </summary>
    [RequireComponent(typeof(LobbyRuntime))]
    public class LobbyManager : SingletonDontDestroy<LobbyManager>
    {
        #region Fields & Components

        private readonly LobbyHandler _lobbyHandler = new();

        [SerializeField] private LobbyRuntime _lobbyRuntime;
        [SerializeField] private LobbyStateManager stateManager;

        [Header("Configuration")] [SerializeField]
        private float heartbeatInterval = 15f;

        [SerializeField] private float updateInterval = 4f;
        [SerializeField] private bool enableHealthChecks = false;

        // Thread-safe operation management
        private readonly object _operationLock = new object();
        private CancellationTokenSource _operationCancellation;
        private volatile bool _isOperationInProgress = false;
        private volatile bool _isInitialized = false;
        private volatile bool _isDisposed = false;

        // Dependency health tracking
        private bool _dependenciesHealthy = false;
        private DateTime _lastHealthCheck = DateTime.MinValue;
        private readonly TimeSpan _healthCheckInterval = TimeSpan.FromSeconds(30);

        #endregion

        #region Properties

        public LobbyHandler LobbyHandler => _lobbyHandler;
        public LobbyRuntime Runtime => _lobbyRuntime;

        private LobbyStateManager StateManager => stateManager;

        // Lobby Properties
        public Lobby CurrentLobby => _lobbyHandler?.CachedLobby;
        public string LobbyId => CurrentLobby?.Id;
        public string LobbyCode => CurrentLobby?.LobbyCode;
        public string HostId => CurrentLobby?.HostId;
        public string RelayJoinCode => CurrentLobby?.GetRelayJoinCode();

        public bool IsInLobby => CurrentLobby != null && !_isDisposed;
        public bool IsHost => IsInLobby && CurrentLobby.HostId == PlayerIdManager.PlayerId;

        // Operation status
        public bool IsOperationInProgress => _isOperationInProgress;
        public bool IsSystemHealthy => _dependenciesHealthy && _isInitialized && !_isDisposed;

        #endregion

        #region Unity Lifecycle

        protected override void OnAwake()
        {
            base.OnAwake();
            InitializeComponents();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _lobbyRuntime ??= GetComponent<LobbyRuntime>();
            stateManager ??= GetComponent<LobbyStateManager>();
        }
#endif

        private void Update()
        {
            if (enableHealthChecks && DateTime.UtcNow - _lastHealthCheck > _healthCheckInterval)
            {
                CheckDependenciesHealth();
            }
        }

        private void InitializeComponents()
        {
            if (_isInitialized || _isDisposed) return;
            try
            {
                _lobbyRuntime ??= GetComponent<LobbyRuntime>();

                if (_lobbyRuntime == null)
                {
                    Debug.LogError("[LobbyManager] LobbyRuntime component missing");
                    return;
                }

                _lobbyHandler.InitializeComponents(_lobbyRuntime);
                _lobbyRuntime.Initialize(heartbeatInterval, updateInterval);

                CheckDependenciesHealth();
                _isInitialized = true;

                Debug.Log("[LobbyManager] Initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Initialization failed: {ex.Message}");
                _isInitialized = false;
            }
        }

        protected override void OnDestroy()
        {
            if (_isDisposed) return;

            _isDisposed = true;

            lock (_operationLock)
            {
                _operationCancellation?.Cancel();
                _operationCancellation?.Dispose();
                _operationCancellation = null;
            }

            try
            {
                _lobbyRuntime?.StopRuntime();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Cleanup error: {ex.Message}");
            }

            base.OnDestroy();
        }

        #endregion

        #region Health Checks

        private void CheckDependenciesHealth()
        {
            try
            {
                _dependenciesHealthy =
                    StateManager != null &&
                    _lobbyHandler != null &&
                    _lobbyRuntime != null &&
                    PlayerIdManager.PlayerId != null;

                _lastHealthCheck = DateTime.UtcNow;

                if (!_dependenciesHealthy)
                {
                    Debug.LogWarning("[LobbyManager] Dependencies health check failed");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Health check error: {ex.Message}");
                _dependenciesHealthy = false;
            }
        }

        #endregion

        #region Thread-Safe Operation Management

        private async Task<OperationResult<T>> ExecuteOperationAsync<T>(
            Func<CancellationToken, Task<OperationResult<T>>> operation)
        {
            if (_isDisposed)
                return OperationResult<T>.Failure("System is disposed");

            if (!ValidateSystemState())
                return OperationResult<T>.Failure("System not ready");

            lock (_operationLock)
            {
                if (_isOperationInProgress)
                    return OperationResult<T>.Failure("Another operation is in progress");

                _isOperationInProgress = true;
                _operationCancellation?.Cancel();
                _operationCancellation?.Dispose();
                _operationCancellation = new CancellationTokenSource();
            }

            try
            {
                var result = await operation(_operationCancellation.Token);
                return result;
            }
            catch (OperationCanceledException)
            {
                return OperationResult<T>.Failure("Operation was cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Operation failed: {ex.Message}");
                return OperationResult<T>.Failure($"Operation failed: {ex.Message}");
            }
            finally
            {
                lock (_operationLock)
                {
                    _isOperationInProgress = false;
                    _operationCancellation?.Dispose();
                    _operationCancellation = null;
                }
            }
        }

        private async Task<OperationResult> ExecuteOperationAsync(
            Func<CancellationToken, Task<OperationResult>> operation)
        {
            var result = await ExecuteOperationAsync<object>(async token =>
            {
                var opResult = await operation(token);
                return opResult.IsSuccess
                    ? OperationResult<object>.Success(null, opResult.Message)
                    : OperationResult<object>.Failure(opResult.ErrorMessage);
            });

            return result.IsSuccess
                ? OperationResult.Success(result.Message)
                : OperationResult.Failure(result.ErrorMessage);
        }

        #endregion

        #region Public API - Host Operations

        public async Task<OperationResult> CreateLobbyAsync(string lobbyName, int maxPlayers,
            CreateLobbyOptions options = null)
        {
            return await ExecuteOperationAsync(async cancellationToken =>
            {
                if (!await StateManager.TryTransitionAsync(Networking.StateMachine.LobbyState.CreatingLobby))
                {
                    return OperationResult.Failure("Cannot start lobby creation");
                }

                try
                {
                    var lobby = await _lobbyHandler.CreateLobbyAsync(lobbyName, maxPlayers, options);
                    if (lobby == null)
                    {
                        await SafeTransitionToFailedAsync("Failed to create lobby");
                        return OperationResult.Failure("Failed to create lobby");
                    }

                    var result = await NetworkController.Instance.StartHostAsync(maxPlayers, cancellationToken);
                    if (!result.IsSuccess)
                    {
                        await SafeTransitionToFailedAsync($"Failed to start host: {result.ErrorMessage}");
                        return OperationResult.Failure($"Failed to start host: {result.ErrorMessage}");
                    }

                    var joinCode = result.JoinCode;
                    var updateSuccess = await LobbyDataExtensions.SetRelayJoinCodeAsync(lobby.Id, joinCode);
                    if (!updateSuccess)
                    {
                        Debug.LogWarning("[LobbyManager] Failed to update lobby with relay code");
                    }

                    if (!await StateManager.TryTransitionAsync(Networking.StateMachine.LobbyState.LobbyActive))
                    {
                        await SafeTransitionToFailedAsync("Failed to activate lobby");
                        return OperationResult.Failure("Failed to activate lobby");
                    }

                    Debug.Log($"[LobbyManager] Lobby created successfully: {joinCode}");
                    LobbyEvents.TriggerRelayHostReady(joinCode);
                    return OperationResult.Success($"Lobby created: {joinCode}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[LobbyManager] Create lobby failed: {ex.Message}");
                    await SafeTransitionToFailedAsync($"Create failed: {ex.Message}");
                    return OperationResult.Failure($"Create failed: {ex.Message}");
                }
            });
        }

        #endregion

        #region Public API - Client Operations

        public async Task<OperationResult> JoinLobbyAsync(string code, string password = null)
        {
            return await ExecuteOperationAsync(async cancellationToken =>
            {
                if (!await StateManager.TryTransitionAsync(Networking.StateMachine.LobbyState.JoiningLobby))
                {
                    return OperationResult.Failure("Cannot start lobby join");
                }

                try
                {
                    var joinSuccess = await _lobbyHandler.PrecheckPhaseThenJoin(code, password);
                    if (!joinSuccess)
                    {
                        await SafeTransitionToFailedAsync("Failed to join lobby");
                        return OperationResult.Failure("Failed to join lobby");
                    }

                    Debug.Log($"[LobbyManager] Joined lobby with code: {GetRelayCode()}");

                    var result = await NetworkController.Instance.StartClientAsync(GetRelayCode(), cancellationToken);
                    if (!result.IsSuccess)
                    {
                        await SafeTransitionToFailedAsync($"Failed to start client: {result.ErrorMessage}");
                        return OperationResult.Failure(result.ErrorMessage);
                    }

                    if (!await StateManager.TryTransitionAsync(Networking.StateMachine.LobbyState.LobbyActive))
                    {
                        await SafeTransitionToFailedAsync("Failed to transition lobby active");
                        return OperationResult.Failure("Failed to transition lobby active");
                    }

                    Debug.Log($"[LobbyManager] Successfully joined lobby: {code}");
                    return OperationResult.Success($"Joined lobby: {code}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[LobbyManager] Join lobby failed: {ex.Message}");
                    await SafeTransitionToFailedAsync($"Join failed: {ex.Message}");
                    return OperationResult.Failure($"Join failed: {ex.Message}");
                }
            });
        }

        #endregion

        #region Public API - Lobby Management

        public void StopCheckingLobby()
        {
            _lobbyRuntime?.StopRuntime();
            Debug.Log("[LobbyManager] Stopped lobby runtime");
        }

        public async Task<OperationResult> LeaveLobbyAsync()
        {
            if (!IsInLobby)
                return OperationResult.Failure("Not in lobby");

            return await ExecuteOperationAsync(async cancellationToken =>
            {
                if (!await StateManager.TryTransitionAsync(Networking.StateMachine.LobbyState.LeavingLobby))
                {
                    return OperationResult.Failure("Cannot start leaving");
                }

                try
                {
                    var success = await _lobbyHandler.LeaveLobbyAsync();
                    if (!success)
                    {
                        await SafeTransitionToFailedAsync("Failed to leave lobby");
                        return OperationResult.Failure("Failed to leave lobby");
                    }

                    await CleanupAllResourcesAsync();

                    if (!await StateManager.TryTransitionAsync(Networking.StateMachine.LobbyState.Default))
                    {
                        Debug.LogWarning("[LobbyManager] Failed to transition to None after leaving");
                    }

                    return OperationResult.Success("Left lobby successfully");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[LobbyManager] Leave lobby failed: {ex.Message}");
                    await SafeTransitionToFailedAsync($"Leave failed: {ex.Message}");
                    return OperationResult.Failure($"Leave failed: {ex.Message}");
                }
            });
        }

        public async Task<OperationResult> RemoveLobbyAsync()
        {
            if (!IsHost)
                return OperationResult.Failure("Only host can remove lobby");

            return await ExecuteOperationAsync(async cancellationToken =>
            {
                if (!await StateManager.TryTransitionAsync(Networking.StateMachine.LobbyState.RemovingLobby))
                {
                    return OperationResult.Failure("Cannot start removing");
                }

                try
                {
                    var success = await _lobbyHandler.RemoveLobbyAsync();
                    if (!success)
                    {
                        await SafeTransitionToFailedAsync("Failed to remove lobby");
                        return OperationResult.Failure("Failed to remove lobby");
                    }

                    await CleanupAllResourcesAsync();

                    if (!await StateManager.TryTransitionAsync(Networking.StateMachine.LobbyState.Default))
                    {
                        Debug.LogWarning("[LobbyManager] Failed to transition to None after removing");
                    }

                    return OperationResult.Success("Lobby removed successfully");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[LobbyManager] Remove lobby failed: {ex.Message}");
                    await SafeTransitionToFailedAsync($"Remove failed: {ex.Message}");
                    return OperationResult.Failure($"Remove failed: {ex.Message}");
                }
            });
        }

        #endregion

        #region Recovery & Cleanup

        private async Task<bool> SafeTransitionToFailedAsync(string reason)
        {
            try
            {
                Debug.LogWarning($"[LobbyManager] Transitioning to failed: {reason}");
                LobbyEvents.TriggerRelayError(reason);
                return await StateManager.TryTransitionAsync(Networking.StateMachine.LobbyState.Failed);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Failed to transition to failed state: {ex.Message}");
                return false;
            }
        }

        private async Task CleanupAllResourcesAsync()
        {
            var cleanupTasks = new[]
            {
                CleanupNetworkAsync(),
                CleanupLobbyComponentsAsync()
            };

            try
            {
                await Task.WhenAll(cleanupTasks);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Cleanup failed: {ex.Message}");
            }
        }

        private async Task CleanupNetworkAsync()
        {
            try
            {
                await RelayHandler.SafeShutdownAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Network cleanup failed: {ex.Message}");
            }
        }

        private async Task CleanupLobbyComponentsAsync()
        {
            try
            {
                _lobbyRuntime?.StopRuntime();
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Lobby components cleanup failed: {ex.Message}");
            }
        }

        public async Task<bool> EmergencyResetAsync(string reason = null)
        {
            try
            {
                Debug.LogWarning($"[LobbyManager] Emergency reset: {reason ?? "Manual reset"}");

                lock (_operationLock)
                {
                    _operationCancellation?.Cancel();
                    _isOperationInProgress = false;
                }

                await CleanupAllResourcesAsync();

                return await StateManager.SafeReturnToDefaultAsync(reason);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Emergency reset failed: {ex.Message}");

                try
                {
                    StateManager?.ForceTransition(Networking.StateMachine.LobbyState.Default);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        #endregion

        #region Validation & Utilities

        private bool ValidateSystemState()
        {
            if (_isDisposed)
            {
                Debug.LogError("[LobbyManager] System is disposed");
                return false;
            }

            if (!_isInitialized)
            {
                Debug.LogError("[LobbyManager] System not initialized");
                return false;
            }

            if (!_dependenciesHealthy)
            {
                Debug.LogError("[LobbyManager] Dependencies not healthy");
                CheckDependenciesHealth();
                return _dependenciesHealthy;
            }

            return true;
        }

        private string GetRelayCode() => CurrentLobby?.GetRelayJoinCode();

        public void OnLobbyUpdated(Lobby updated) => _lobbyHandler?.OnLobbyUpdated(updated);

        public Task<bool> SetPlayerReadyAsync(bool isReady) =>
            LobbyDataExtensions.SetPlayerReadyAsync(CurrentLobby?.Id, isReady);

        public Task<bool> SetPhaseAsync(string phase) =>
            LobbyDataExtensions.SetLobbyPhaseAsync(CurrentLobby?.Id, phase);

        public Task<bool> KickPlayerAsync(string playerId) =>
            _lobbyHandler?.KickPlayerAsync(playerId) ?? Task.FromResult(false);

        #endregion

        #region Debug

        [ContextMenu("Debug System State")]
        private void DebugSystemState()
        {
            Debug.Log($"[LobbyManager] System State:" +
                      $"\n  Initialized: {_isInitialized}" +
                      $"\n  Dependencies Healthy: {_dependenciesHealthy}" +
                      $"\n  Operation In Progress: {_isOperationInProgress}" +
                      $"\n  Current State: {StateManager?.CurrentState}" +
                      $"\n  In Lobby: {IsInLobby}" +
                      $"\n  Is Host: {IsHost}" +
                      $"\n  Lobby Code: {LobbyCode}" +
                      $"\n  Relay Code: {RelayJoinCode}" +
                      $"\n  Runtime Running: {(_lobbyRuntime?.IsRunning ?? false)}");
        }

        #endregion

        public async Task<bool> UpdateLobbyPasswordAsync(string arg0)
        {
            await LobbyHandler.UpdateLobbyAsync(CurrentLobby.Id, new UpdateLobbyOptions());
            return true;
        }

        public async Task<bool> UpdateLobbyNameAsync(string arg0)
        {
            await LobbyHandler.UpdateLobbyAsync(CurrentLobby.Id, new UpdateLobbyOptions());
            return true;
        }

        public async Task<bool> UpdateMaxPlayersAsync(int i)
        {
            await LobbyHandler.UpdateLobbyAsync(CurrentLobby.Id, new UpdateLobbyOptions());
            return true;
        }
    }

    #region Improved Result Types

    public class OperationResult<T> : OperationResult
    {
        public T Data { get; private set; }

        private OperationResult(bool isSuccess, T data, string message, string joinCode = null,
            Exception exception = null)
            : base(isSuccess, message, joinCode, exception)
        {
            Data = data;
        }

        public static OperationResult<T> Success(T data, string message = null) => new(true, data, message);
        public static OperationResult<T> Failure(string errorMessage) => new(false, default(T), null, errorMessage);
    }

    #endregion
}
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Networking.Relay;
using _GAME.Scripts.Networking.StateMachine;
using _GAME.Scripts.UI;
using GAME.Scripts.DesignPattern;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace _GAME.Scripts.Networking
{
    /// <summary>
    /// Enhanced NetworkController with improved callback handling and error recovery
    /// </summary>
    public class NetworkController : SingletonDontDestroy<NetworkController>
    {
        #region Fields & Properties

        private NetworkStateManager _stateManager;
        private NetworkCallbackHandler _callbackHandler;
        private CancellationTokenSource _operationCancellationSource;
        private Coroutine _connectionMonitoringCoroutine;

        // Properties
        public bool IsConnected => NetworkManager.Singleton?.IsConnectedClient ?? false;
        public bool IsHost => NetworkManager.Singleton?.IsHost ?? false;
        public bool IsClient => NetworkManager.Singleton?.IsClient ?? false;
        public ulong LocalClientId => NetworkManager.Singleton?.LocalClientId ?? 0;
        public NetworkState CurrentNetworkState => _stateManager?.CurrentState ?? NetworkState.Default;

        #endregion

        #region Events

        public event Action<ulong> OnClientJoined;
        public event Action<ulong> OnClientLeft;
        public event Action<NetworkError> OnNetworkError;
        public event Action<NetworkState, NetworkState> OnNetworkStateChanged;

        #endregion

        #region Network Callback Handler

        private class NetworkCallbackHandler : IDisposable
        {
            private readonly NetworkController _controller;
            private readonly NetworkManager _networkManager;

            public NetworkCallbackHandler(NetworkController controller)
            {
                _controller = controller;
                _networkManager = NetworkManager.Singleton;
                RegisterCallbacks();
            }

            private void RegisterCallbacks()
            {
                if (_networkManager == null) return;

                _networkManager.OnServerStarted += OnServerStarted;
                _networkManager.OnServerStopped += OnServerStopped;
                _networkManager.OnClientConnectedCallback += OnClientConnected;
                _networkManager.OnClientDisconnectCallback += OnClientDisconnected;
                _networkManager.OnClientStarted += OnClientStarted;
                _networkManager.OnClientStopped += OnClientStopped;
                _networkManager.ConnectionApprovalCallback += OnConnectionApproval;
                _networkManager.OnTransportFailure += OnTransportFailure;
            }

            private void UnregisterCallbacks()
            {
                if (_networkManager == null) return;

                _networkManager.OnServerStarted -= OnServerStarted;
                _networkManager.OnServerStopped -= OnServerStopped;
                _networkManager.OnClientConnectedCallback -= OnClientConnected;
                _networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                _networkManager.ConnectionApprovalCallback -= OnConnectionApproval;
                _networkManager.OnClientStarted -= OnClientStarted;
                _networkManager.OnClientStopped -= OnClientStopped;
                _networkManager.OnTransportFailure -= OnTransportFailure;
            }

            #region Callback Implementations

            private void OnServerStarted()
            {
                Debug.Log("[NetworkCallbacks] Server started");
                _controller.HandleServerStarted();
            }

            private void OnServerStopped(bool wasHost)
            {
                Debug.Log($"[NetworkCallbacks] Server stopped (wasHost: {wasHost})");
                _controller.HandleServerStopped(wasHost);
            }
            private void OnClientStarted()
            {
                Debug.Log($"[NetworkCallbacks] Client Started");
                _controller.HandleClientStarted();
            }
            
            private void OnClientConnected(ulong clientId)
            {
                Debug.Log($"[NetworkCallbacks] Client {clientId} connected");
                _controller.HandleClientConnected(clientId);
            }

            private void OnClientDisconnected(ulong clientId)
            {
                Debug.Log($"[NetworkCallbacks] Client {clientId} disconnected");
                _controller.HandleClientDisconnected(clientId);
            }
            
        
            
            private void OnClientStopped(bool stopped)
            {
                Debug.Log($"[NetworkCallbacks] Client Stopped");
                _controller.HandleClientStopped(stopped);
            }

            private void OnConnectionApproval(NetworkManager.ConnectionApprovalRequest request,
                NetworkManager.ConnectionApprovalResponse response)
            {
                _controller.HandleConnectionApproval(request, response);
            }

            private void OnTransportFailure()
            {
                Debug.LogError("[NetworkCallbacks] Transport failure detected");
                _controller.HandleTransportFailure();
            }

            #endregion

            public void Dispose()
            {
                UnregisterCallbacks();
            }
        }

        #endregion

        #region Lifecycle

        protected override void OnAwake()
        {
            base.OnAwake();
            InitializeController();
        }

        private void InitializeController()
        {
            try
            {
                // Initialize state manager
                _stateManager = new NetworkStateManager();
                _stateManager.Init();
                _stateManager.OnStateChanged += OnNetworkStateChanged;

                // Initialize callback handler
                _callbackHandler = new NetworkCallbackHandler(this);

                Debug.Log("[NetworkController] Initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkController] Initialization failed: {ex.Message}");
            }
        }

        protected override void OnDestroy()
        {
            Cleanup();
            base.OnDestroy();
        }

        private void Cleanup()
        {
            _operationCancellationSource?.Cancel();
            _operationCancellationSource?.Dispose();

            _callbackHandler?.Dispose();
            StopConnectionMonitoring();

            if (_stateManager != null)
            {
                _stateManager.OnStateChanged -= OnNetworkStateChanged;
                _stateManager.Clear();
            }

            // Clear events
            OnClientJoined = null;
            OnClientLeft = null;
            OnNetworkError = null;
            OnNetworkStateChanged = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Start as host with progress tracking
        /// </summary>
        public async Task<OperationResult> StartHostAsync(int maxConnection,
            CancellationToken cancellationToken = default)
        {
            if (!_stateManager.CanTransitionTo(NetworkState.ClientConnecting))
                return OperationResult.Failure("Cannot start host in current state");

            try
            {
                await _stateManager.TryTransitionAsync(NetworkState.ClientConnecting);

                // Step 1: Setup relay
                var relayResult = await RelayHandler.SetupHostRelayAsync(maxConnection, cancellationToken);
                if (!relayResult.IsSuccess)
                {
                    await _stateManager.TryTransitionAsync(NetworkState.Failed, relayResult.ErrorMessage);
                    return OperationResult.Failure(relayResult.ErrorMessage);
                }

                string joinCode = relayResult.Data;

                // Step 2: Start Netcode host (transport đã config bởi RelayConnector)
                var nm = NetworkManager.Singleton;
                if (nm == null)
                    return OperationResult.Failure("NetworkManager not found");

                if (!nm.StartHost())
                {
                    await _stateManager.TryTransitionAsync(NetworkState.Failed, "Failed to start host");
                    return OperationResult.Failure("Failed to start host");
                }

                Debug.Log($"[NetworkController] Host started with Relay. JoinCode={joinCode}");

                // Có thể emit joinCode ra UI hoặc Lobby system
                return OperationResult.Success($"Host started successfully with Relay. JoinCode={joinCode}", joinCode);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkController] StartHostWithRelay failed: {ex.Message}");
                await _stateManager.TryTransitionAsync(NetworkState.Failed, ex.Message);
                return OperationResult.Failure(ex.Message);
            }
        }

        public async Task<OperationResult> StartClientAsync(string joinCode,
            CancellationToken cancellationToken = default)
        {
            // Yêu cầu: đã Initialize Unity Services + đã SignIn 
            if (!AuthenticationService.Instance.IsSignedIn)
                return OperationResult.Failure("Not signed in. Call Authentication first.");

            if (string.IsNullOrWhiteSpace(joinCode))
                return OperationResult.Failure("Join code is required for Relay client.");

            if (!_stateManager.CanTransitionTo(NetworkState.ClientConnecting))
                return OperationResult.Failure("Cannot start client in current state");

            _operationCancellationSource?.Cancel();
            _operationCancellationSource = new CancellationTokenSource();
            var token = _operationCancellationSource.Token;

            try
            {
                await _stateManager.TryTransitionAsync(NetworkState.ClientConnecting);

                var nm = NetworkManager.Singleton;
                if (nm == null)
                {
                    await _stateManager.TryTransitionAsync(NetworkState.Failed, "NetworkManager not found");
                    return OperationResult.Failure("NetworkManager not found");
                }

                // 1) Join allocation bằng joinCode
                var result = await RelayConnector.JoinAsClientAsync(joinCode, cancellationToken);
                if (!result.IsSuccess)
                {
                    await _stateManager.TryTransitionAsync(NetworkState.Failed, result.ErrorMessage);
                    return OperationResult.Failure(result.ErrorMessage);
                }

                // 3) Bắt đầu client sau khi đã set Relay
                bool startSuccess = nm.StartClient();
                if (!startSuccess)
                {
                    await _stateManager.TryTransitionAsync(NetworkState.Failed, "Failed to start client");
                    return OperationResult.Failure("Failed to start client");
                }

                return OperationResult.Success("Client connection initiated via Relay");
            }
            catch (RelayServiceException rse)
            {
                Debug.LogError($"[NetworkController] Relay join failed: {rse.Message}");
                await _stateManager.TryTransitionAsync(NetworkState.Failed, rse.Message);
                return OperationResult.Failure($"Relay join failed: {rse.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkController] Start client failed: {ex.Message}");
                await _stateManager.TryTransitionAsync(NetworkState.Failed, ex.Message);
                return OperationResult.Failure($"Client start failed: {ex.Message}");
            }
        }

        public async Task<OperationResult> LoadSceneAsync(
            SceneDefinitions sceneDefinitions, 
            Action onSceneLoaded = null, 
            LoadSceneMode mode = LoadSceneMode.Single,
            float timeoutSeconds = 30f,
            CancellationToken cancellationToken = default)
        {
            var nm = NetworkManager.Singleton; 
            if (nm == null)
                return OperationResult.Failure("NetworkManager not found");

            if (!nm.IsServer) // Host hoặc Dedicated Server
            {
                Debug.LogWarning("[NetworkController] Only the server/host can initiate scene changes.");
                return OperationResult.Failure("Only server/host can load scenes");
            }

            var nsm = nm.SceneManager;
            if (nsm == null)
                return OperationResult.Failure("NetworkSceneManager not found");

            var sceneName = SceneHelper.ToSceneName(sceneDefinitions);

            // Optional: tránh lỗi scene không có trong Build Settings
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
                return OperationResult.Failure($"Scene '{sceneName}' not found in Build Settings");

            var tcs = new TaskCompletionSource<(IReadOnlyList<ulong> completed, IReadOnlyList<ulong> timedOut)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void OnLoadEventCompleted(string loadedSceneName, LoadSceneMode loadedMode,
                List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
            {
                if (loadedSceneName == sceneName && loadedMode == mode)
                {
                    tcs.TrySetResult((clientsCompleted, clientsTimedOut));
                }
            }

            // Đăng ký callback trước khi gọi LoadScene để không miss sự kiện
            nsm.OnLoadEventCompleted += OnLoadEventCompleted;

            try
            {
                // Phát lệnh load scene tới tất cả client
                nsm.LoadScene(sceneName, mode);

                // Chờ hoàn tất hoặc timeout/cancel
                using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var linkedCts =
                    CancellationTokenSource.CreateLinkedTokenSource(ctsTimeout.Token, cancellationToken);

                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));
                if (completedTask != tcs.Task)
                {
                    // Timeout hoặc bị hủy
                    if (ctsTimeout.IsCancellationRequested)
                        return OperationResult.Failure($"Scene load timeout after {timeoutSeconds:0.#}s");

                    if (cancellationToken.IsCancellationRequested)
                        return OperationResult.Failure("Scene load canceled");
                }

                var (clientsCompleted, clientsTimedOut) = await tcs.Task;

                if (clientsTimedOut != null && clientsTimedOut.Count > 0)
                {
                    // Có client không kịp load — vẫn coi là loaded nhưng cảnh báo
                    Debug.LogWarning($"[NetworkController] Scene '{sceneName}' loaded with timeouts. " +
                                     $"Timed out clients: {string.Join(", ", clientsTimedOut)}");
                    return OperationResult.Success(
                        $"Scene '{sceneName}' loaded (with timeouts: {clientsTimedOut.Count})");
                }
                onSceneLoaded?.Invoke();
                return OperationResult.Success($"Scene '{sceneName}' loaded successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkController] LoadSceneAsync failed: {ex.Message}");
                return OperationResult.Failure($"Load scene failed: {ex.Message}");
            }
            finally
            {
                nsm.OnLoadEventCompleted -= OnLoadEventCompleted;
            }
        }

        /// <summary>
        /// Disconnect from network
        /// </summary>
        public async Task<OperationResult> DisconnectAsync(string reason = null)
        {
            try
            {
                Debug.Log($"[NetworkController] Disconnecting. Reason: {reason ?? "User initiated"}");

                await _stateManager.TryTransitionAsync(NetworkState.Disconnecting);

                var nm = NetworkManager.Singleton;
                if (nm != null && (nm.IsHost || nm.IsClient))
                {
                    nm.Shutdown();
                }

                await _stateManager.TryTransitionAsync(NetworkState.Default);

                return OperationResult.Success("Disconnected successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkController] Disconnect failed: {ex.Message}");
                return OperationResult.Failure($"Disconnect failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Get current network state info
        /// </summary>
        public NetworkStateInfo GetNetworkStateInfo()
        {
            return _stateManager?.GetCurrentStateInfo() ?? new NetworkStateInfo
            {
                CurrentState = NetworkState.Default,
                DisplayName = "Not Initialized"
            };
        }

        #endregion

        #region Network Callback Handlers

        private void HandleServerStarted()
        {
            _ = _stateManager.TryTransitionAsync(NetworkState.Connected);
            StartConnectionMonitoring();
        }

        private void HandleServerStopped(bool wasHost)
        {
            StopConnectionMonitoring();
            _ = _stateManager.TryTransitionAsync(NetworkState.Disconnected, $"Server stopped (wasHost: {wasHost})");
        }

        private void HandleClientStarted()
        {
            
        }
        
        private void HandleClientConnected(ulong clientId)
        {
            if (clientId == LocalClientId)
            {
                // Local client connected
                _ = _stateManager.TryTransitionAsync(NetworkState.Connected);
                StartConnectionMonitoring();
            }
            else
            {
                // Remote client connected
                OnClientJoined?.Invoke(clientId);
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (clientId == LocalClientId)
            {
                // Local client disconnected
                StopConnectionMonitoring();
                _ = HandleUnexpectedDisconnectionAsync("Local client disconnected");
            }
            else
            {
                // Remote client disconnected
                OnClientLeft?.Invoke(clientId);
            }
        }
        
        private void HandleClientStopped(bool stopped)
        {
            
        }


        private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            // Simple approval logic - can be enhanced
            response.Approved = true;
            response.CreatePlayerObject = true;
        }

        private void HandleTransportFailure()
        {
            var error = new NetworkError
            {
                Type = NetworkErrorType.TransportFailure,
                Message = "Transport layer failure",
                Timestamp = DateTime.UtcNow
            };

            OnNetworkError?.Invoke(error);
            _ = _stateManager.SafeReturnToDefaultAsync("Transport failure");
        }

        #endregion

        #region Error Handling

        private async Task HandleUnexpectedDisconnectionAsync(string reason)
        {
            Debug.LogWarning($"[NetworkController] Unexpected disconnection: {reason}");

            try
            {
                StopConnectionMonitoring();

                PopupNotification.Instance?.ShowPopup(false,
                    "Lost connection to the session",
                    "Connection Lost");

                await _stateManager.SafeReturnToDefaultAsync(reason);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkController] Error handling disconnection: {ex.Message}");
            }
        }

        #endregion

        #region Connection Monitoring

        public void StartConnectionMonitoring()
        {
            if (_connectionMonitoringCoroutine == null && IsConnected)
            {
                _connectionMonitoringCoroutine = StartCoroutine(MonitorConnection());
            }
        }

        public void StopConnectionMonitoring()
        {
            if (_connectionMonitoringCoroutine != null)
            {
                StopCoroutine(_connectionMonitoringCoroutine);
                _connectionMonitoringCoroutine = null;
            }
        }

        private System.Collections.IEnumerator MonitorConnection()
        {
            while (IsConnected)
            {
                // Basic connection health check
                var nm = NetworkManager.Singleton;
                if (nm == null || !nm.IsConnectedClient)
                {
                    Debug.LogWarning("[NetworkController] Connection health check failed");
                    _ = HandleUnexpectedDisconnectionAsync("Health check failed");
                    yield break;
                }

                yield return new WaitForSeconds(5f);
            }
        }

        #endregion

        public void StartGameAsync()
        {
            //Swtich to gameplay scene
            _ = LoadSceneAsync(SceneDefinitions.GameScene, null, LoadSceneMode.Single);
        }
    }

    #region Supporting Types

    public class OperationResult
    {
        public bool IsSuccess { get; private set; }
        public string Message { get; private set; }
        public string JoinCode { get; private set; }
        public Exception Exception { get; private set; }
        public string ErrorMessage { get; private set; }

        protected OperationResult(bool isSuccess, string message, string joinCode = null, Exception exception = null)
        {
            IsSuccess = isSuccess;
            Message = message;
            JoinCode = joinCode;
            Exception = exception;
            ErrorMessage = exception != null ? exception.Message : "";
        }

        public static OperationResult Success(string message = "Operation completed successfully",
            string joinCode = null)
        {
            return new OperationResult(true, message, joinCode);
        }

        public static OperationResult Failure(string message, Exception exception = null)
        {
            return new OperationResult(false, message, null, exception);
        }
    }

    public enum NetworkErrorType
    {
        ConnectionFailure,
        TransportFailure,
        ConnectionQuality,
        AuthenticationError,
        VersionMismatch
    }

    public class NetworkError
    {
        public NetworkErrorType Type { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public Exception Exception { get; set; }
    }

    #endregion
}
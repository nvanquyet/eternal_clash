using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _GAME.Scripts.Config;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Networking.Lobbies;
using _GAME.Scripts.Networking.Relay;
using _GAME.Scripts.UI;
using GAME.Scripts.DesignPattern;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace _GAME.Scripts.Networking
{
    /// <summary>
    /// Enhanced NetworkController - Fixed để sync với GameNet flow
    /// </summary>
    public class NetworkController : MonoBehaviour
    {
        #region Fields & Properties
        private NetworkCallbackHandler _callbackHandler;
        private CancellationTokenSource _operationCancellationSource;
        private Coroutine _connectionMonitoringCoroutine;

        // Properties
        public bool IsConnected => NetworkManager.Singleton?.IsConnectedClient ?? false;
        public bool IsHost => NetworkManager.Singleton?.IsHost ?? false;
        public bool IsClient => NetworkManager.Singleton?.IsClient ?? false;
        public ulong LocalClientId => NetworkManager.Singleton?.LocalClientId ?? 0;

        #endregion

        #region Events

        public event Action<ulong> OnClientJoined;
        public event Action<ulong> OnClientLeft;
        public event Action<NetworkError> OnNetworkError;
        public event Action OnHostStarted;
        public event Action OnClientConnected;

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

        protected void Awake()
        {
            InitializeController();
        }

        private void InitializeController()
        {
            try
            {
                // Initialize callback handler
                _callbackHandler = new NetworkCallbackHandler(this);
                Debug.Log("[NetworkController] Initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkController] Initialization failed: {ex.Message}");
            }
        }

        protected void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            _operationCancellationSource?.Cancel();
            _operationCancellationSource?.Dispose();

            _callbackHandler?.Dispose();
            StopConnectionMonitoring();

            // Clear events
            OnClientJoined = null;
            OnClientLeft = null;
            OnNetworkError = null;
            OnHostStarted = null;
            OnClientConnected = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Start as host with relay - CHỈ SETUP NETWORK, không tạo lobby
        /// </summary>
        public async Task<OperationResult> StartHostAsync(int maxConnection,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Debug.Log("[NetworkController] Starting host with relay...");
                
                // Cancel previous operations
                _operationCancellationSource?.Cancel();
                _operationCancellationSource = new CancellationTokenSource();

                // Reset relay state
                RelayHandler.ResetAllState();
                
                // Validate authentication
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    return OperationResult.Failure("Not authenticated");
                }

                // Step 1: Setup relay allocation và get join code
                var relayResult = await RelayHandler.SetupHostRelayAsync(maxConnection, cancellationToken);
                if (!relayResult.IsSuccess)
                {
                    Debug.LogError($"[NetworkController] Relay setup failed: {relayResult.ErrorMessage}");
                    return OperationResult.Failure($"Relay setup failed: {relayResult.ErrorMessage}");
                }

                string joinCode = relayResult.Data;
                Debug.Log($"[NetworkController] Relay allocated, join code: {joinCode}");

                // Step 2: Start Netcode host (transport đã được config bởi RelayHandler)
                var hostResult = RelayHandler.StartNetworkHost();
                if (!hostResult.IsSuccess)
                {
                    Debug.LogError($"[NetworkController] Failed to start network host: {hostResult.ErrorMessage}");
                    await RelayHandler.SafeShutdownAsync();
                    return OperationResult.Failure($"Failed to start network host: {hostResult.ErrorMessage}");
                }

                Debug.Log($"[NetworkController] Host started successfully with join code: {joinCode}");

                // Return success với join code để GameNet update vào lobby
                return OperationResult.Success($"Host started successfully", joinCode);
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[NetworkController] Host start operation was cancelled");
                return OperationResult.Failure("Operation cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkController] StartHostAsync failed: {ex}");
                await RelayHandler.SafeShutdownAsync();
                return OperationResult.Failure($"Host start failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Start as client with join code - CHỈ CONNECT NETWORK, không join lobby
        /// </summary>
        public async Task<OperationResult> StartClientAsync(string joinCode,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                return OperationResult.Failure("Join code is required");
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                return OperationResult.Failure("Not authenticated");
            }

            try
            {
                Debug.Log($"[NetworkController] Starting client with join code: {joinCode}");
                
                // Cancel previous operations
                _operationCancellationSource?.Cancel();
                _operationCancellationSource = new CancellationTokenSource();

                // Reset relay state
                RelayHandler.ResetAllState();

                // Step 1: Connect to relay server
                var relayResult = await RelayHandler.ConnectClientToRelayAsync(joinCode, cancellationToken);
                if (!relayResult.IsSuccess)
                {
                    Debug.LogError($"[NetworkController] Failed to connect to relay: {relayResult.ErrorMessage}");
                    return OperationResult.Failure($"Failed to connect to relay: {relayResult.ErrorMessage}");
                }

                Debug.Log("[NetworkController] Connected to relay, starting network client...");

                // Step 2: Start network client
                var clientResult = RelayHandler.StartNetworkClient();
                if (!clientResult.IsSuccess)
                {
                    Debug.LogError($"[NetworkController] Failed to start network client: {clientResult.ErrorMessage}");
                    await RelayHandler.SafeShutdownAsync();
                    return OperationResult.Failure($"Failed to start network client: {clientResult.ErrorMessage}");
                }

                Debug.Log("[NetworkController] Client started successfully");
                return OperationResult.Success("Client connected successfully");
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[NetworkController] Client start operation was cancelled");
                return OperationResult.Failure("Operation cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkController] StartClientAsync failed: {ex}");
                await RelayHandler.SafeShutdownAsync();
                return OperationResult.Failure($"Client start failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Load scene on network - CHỈ HOST mới được gọi
        /// </summary>
        public async Task<OperationResult> LoadSceneAsync(
            SceneDefinitions sceneDefinitions, 
            Action onSceneLoaded = null, 
            LoadSceneMode mode = LoadSceneMode.Single,
            float timeoutSeconds = 30f,
            CancellationToken cancellationToken = default)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                return OperationResult.Failure("NetworkManager not found");
            }

            if (!nm.IsHost && !nm.IsServer)
            {
                Debug.LogWarning("[NetworkController] Only host/server can load scenes");
                return OperationResult.Failure("Only host/server can load scenes");
            }
            
            var nsm = nm.SceneManager;
            if (nsm == null)
            {
                return OperationResult.Failure("NetworkSceneManager not found");
            }

            var sceneName = SceneHelper.ToSceneName(sceneDefinitions);

            // Validate scene exists
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                return OperationResult.Failure($"Scene '{sceneName}' not found in Build Settings");
            }

            try
            {
                Debug.Log($"[NetworkController] Loading scene: {sceneName}");

                var tcs = new TaskCompletionSource<(IReadOnlyList<ulong> completed, IReadOnlyList<ulong> timedOut)>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                void OnLoadEventCompleted(string loadedSceneName, LoadSceneMode loadedMode,
                    List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
                {
                    if (loadedSceneName == sceneName && loadedMode == mode)
                    {
                        Debug.Log($"[NetworkController] Scene load completed: {loadedSceneName}, Clients: {clientsCompleted?.Count ?? 0}, Timed out: {clientsTimedOut?.Count ?? 0}");
                        tcs.TrySetResult((clientsCompleted, clientsTimedOut));
                    }
                }

                // Register callback before loading
                nsm.OnLoadEventCompleted += OnLoadEventCompleted;

                try
                {
                    // Load scene for all clients
                    nsm.LoadScene(sceneName, mode);

                    // Wait for completion with timeout
                    using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ctsTimeout.Token, cancellationToken);

                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));
                    
                    if (completedTask != tcs.Task)
                    {
                        if (ctsTimeout.IsCancellationRequested)
                            return OperationResult.Failure($"Scene load timeout after {timeoutSeconds:0.#}s");
                        if (cancellationToken.IsCancellationRequested)
                            return OperationResult.Failure("Scene load cancelled");
                    }

                    var (clientsCompleted, clientsTimedOut) = await tcs.Task;

                    if (clientsTimedOut != null && clientsTimedOut.Count > 0)
                    {
                        Debug.LogWarning($"[NetworkController] Scene '{sceneName}' loaded with timeouts. Timed out clients: {string.Join(", ", clientsTimedOut)}");
                        return OperationResult.Success($"Scene '{sceneName}' loaded (with {clientsTimedOut.Count} timeouts)");
                    }

                    onSceneLoaded?.Invoke();
                    Debug.Log($"[NetworkController] Scene '{sceneName}' loaded successfully for all clients");
                    return OperationResult.Success($"Scene '{sceneName}' loaded successfully");
                }
                finally
                {
                    nsm.OnLoadEventCompleted -= OnLoadEventCompleted;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkController] LoadSceneAsync failed: {ex}");
                return OperationResult.Failure($"Load scene failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop network - sẽ được gọi từ GameNet
        /// </summary>
        public async Task<OperationResult> StopAsync(string reason = null)
        {
            try
            {
                Debug.Log($"[NetworkController] Stopping network. Reason: {reason ?? "User initiated"}");

                // Cancel any ongoing operations
                _operationCancellationSource?.Cancel();

                // Stop connection monitoring
                StopConnectionMonitoring();

                // Shutdown relay and network
                await RelayHandler.SafeShutdownAsync();

                Debug.Log("[NetworkController] Network stopped successfully");
                return OperationResult.Success("Network stopped successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkController] Stop failed: {ex}");
                return OperationResult.Failure($"Stop failed: {ex.Message}");
            }
        }

        
        public void ForceAllClientDisconnect()
        {
            if(!IsHost) return;
            var nm = NetworkManager.Singleton;
            if(nm == null) return;
            foreach (var clientId in nm.ConnectedClientsIds)
            {
                if (clientId != LocalClientId)
                {
                    nm.DisconnectClient(clientId);
                    Debug.Log($"[NetworkController] Forced disconnect for client {clientId}");
                }
            }
        }
        
        
        #endregion

        #region Network Callback Handlers

        private void HandleServerStarted()
        {
            Debug.Log("[NetworkController] Host server started");
            StartConnectionMonitoring();
            OnHostStarted?.Invoke();
        }

        private void HandleServerStopped(bool wasHost)
        {
            Debug.Log($"[NetworkController] Server stopped (was host: {wasHost})");
            StopConnectionMonitoring();
        }

        private void HandleClientStarted()
        {
            Debug.Log("[NetworkController] Client started");
        }
        
        private void HandleClientConnected(ulong clientId)
        {
            Debug.Log($"[NetworkController] Client {clientId} connected");
            
            if (clientId == LocalClientId)
            {
                // Local client connected
                Debug.Log("[NetworkController] Local client connected successfully");
                StartConnectionMonitoring();
                OnClientConnected?.Invoke();
            }
            else
            {
                // Remote client connected
                Debug.Log($"[NetworkController] Remote client {clientId} joined");
                OnClientJoined?.Invoke(clientId);
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            Debug.Log($"[NetworkController] Client {clientId} disconnected");
            
            if (clientId == LocalClientId)
            {
                // Local client disconnected
                Debug.LogWarning("[NetworkController] Local client disconnected");
                StopConnectionMonitoring();
                HandleUnexpectedDisconnection("Local client disconnected");
            }
            else
            {
                // Remote client disconnected
                Debug.Log($"[NetworkController] Remote client {clientId} left");
                OnClientLeft?.Invoke(clientId);
            }
        }
        
        private void HandleClientStopped(bool stopped)
        {
            Debug.Log($"[NetworkController] Client stopped: {stopped}");
            StopConnectionMonitoring();
        }

        private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            // Simple approval - có thể enhance với validation
            response.Approved = true;
            response.CreatePlayerObject = true;
            
            Debug.Log($"[NetworkController] Connection approved for client");
        }

        private void HandleTransportFailure()
        {
            Debug.LogError("[NetworkController] Transport failure detected");
            
            var error = new NetworkError
            {
                Type = NetworkErrorType.TransportFailure,
                Message = "Transport layer failure",
                Timestamp = DateTime.UtcNow
            };

            OnNetworkError?.Invoke(error);
            HandleUnexpectedDisconnection("Transport failure");
        }

        #endregion

        #region Error Handling

        private void HandleUnexpectedDisconnection(string reason)
        {
            Debug.LogWarning($"[NetworkController] Unexpected disconnection: {reason}");

            StopConnectionMonitoring();
            
            //Lose connection popup + return to home
            LoadingUI.Instance.RunTimed(1, () =>
            {
                SceneController.Instance.LoadSceneAsync(SceneHelper.ToSceneName(SceneDefinitions.Home));
                PopupNotification.Instance?.ShowPopup(false,
                    "Lost connection to the session",
                    "Connection Lost");
            }, "Lost connection. ReturningHome",false);

            // Emergency cleanup
            _ = StopAsync($"Unexpected disconnection: {reason}");
        }

        #endregion

        #region Connection Monitoring

        private void StartConnectionMonitoring()
        {
            if (_connectionMonitoringCoroutine == null && IsConnected)
            {
                Debug.Log("[NetworkController] Starting connection monitoring");
                _connectionMonitoringCoroutine = StartCoroutine(MonitorConnection());
            }
        }

        private void StopConnectionMonitoring()
        {
            if (_connectionMonitoringCoroutine != null)
            {
                Debug.Log("[NetworkController] Stopping connection monitoring");
                StopCoroutine(_connectionMonitoringCoroutine);
                _connectionMonitoringCoroutine = null;
            }
        }

        private System.Collections.IEnumerator MonitorConnection()
        {
            while (IsConnected)
            {
                var nm = NetworkManager.Singleton;
                if (nm == null || !nm.IsConnectedClient)
                {
                    Debug.LogWarning("[NetworkController] Connection health check failed");
                    HandleUnexpectedDisconnection("Health check failed");
                    yield break;
                }

                yield return new WaitForSeconds(5f);
            }
            
            Debug.Log("[NetworkController] Connection monitoring ended");
        }

        #endregion

        #region Game Flow Methods

        public void StartGame()
        {
            if (!IsHost)
            {
                Debug.LogWarning("[NetworkController] Only host can start the game");
                return;
            }
            //Change lobby to Ingame
            _ = GameNet.Instance.Lobby.SetLobbyPhaseAsync(SessionPhase.PLAYING);
            Debug.Log("[NetworkController] Starting game...");
            SceneLoadingBroadcaster.Instance?.PreShowAllClients("Switching to gameplay...");
            _ = LoadSceneAsync(SceneDefinitions.GameScene, null, LoadSceneMode.Single);
        }

        #endregion
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
            ErrorMessage = exception?.Message ?? (isSuccess ? "" : message);
        }

        public static OperationResult Success(string message = "Operation completed successfully", string joinCode = null)
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
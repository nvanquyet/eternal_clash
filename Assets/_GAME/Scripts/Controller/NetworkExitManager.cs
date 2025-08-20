using System;
using System.Threading;
using System.Threading.Tasks;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Networking.Lobbies;
using _GAME.Scripts.Networking.Relay;
using GAME.Scripts.DesignPattern;
using Unity.Netcode;
using Unity.Services.Lobbies;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace _GAME.Scripts.Networking
{
    /// <summary>
    /// NetworkExitManager: Quản lý việc cleanup network/lobby khi người chơi thoát
    /// - Host thoát: Shutdown server + kick all players về main menu
    /// - Client thoát: Disconnect từ server + leave lobby
    /// - Tích hợp với AppExitController để cleanup đúng thứ tự
    /// </summary>
    public class NetworkExitManager : SingletonDontDestroy<NetworkExitManager>
    {

        public string HomeScene => SceneHelper.ToSceneName(SceneDefinitions.Home);
        
        [Header("Timeouts")]
        [SerializeField] private float networkShutdownTimeout = 3f;
        [SerializeField] private float lobbyCleanupTimeout = 2f;

        private bool _isExiting = false;
        private bool _isHost = false;

        #region Unity Lifecycle
        
        private void Start()
        {
            RegisterWithAppExitController();
            SubscribeToNetworkEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeFromNetworkEvents();
        }

        #endregion

        #region Registration with AppExitController

        private void RegisterWithAppExitController()
        {
            var appExitController = AppExitController.Instance;
            if (appExitController == null)
            {
                Debug.LogWarning("[NetworkExitManager] AppExitController not found!");
                return;
            }

            // Best-effort cleanup (when app loses focus/pauses)
            // appExitController.RegisterBestEffortTask(
            //     "NetworkPause", 
            //     HandleBestEffortNetworkPause, 
            //     order: 100
            // );

            // Full cleanup (when quitting app)
            appExitController.RegisterFullCleanupTask(
                "NetworkShutdown", 
                HandleFullNetworkCleanup, 
                order: 100
            );

            Debug.Log("[NetworkExitManager] Registered with AppExitController");
        }

        #endregion

        #region Network Event Subscriptions

        private void SubscribeToNetworkEvents()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted += OnServerStarted;
                NetworkManager.Singleton.OnClientStarted += OnClientStarted;
                NetworkManager.Singleton.OnServerStopped += OnServerStopped;
                NetworkManager.Singleton.OnClientStopped += OnClientStopped;
            }
        }

        private void UnsubscribeFromNetworkEvents()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
                NetworkManager.Singleton.OnClientStarted -= OnClientStarted;
                NetworkManager.Singleton.OnServerStopped -= OnServerStopped;
                NetworkManager.Singleton.OnClientStopped -= OnClientStopped;
            }
        }

        #endregion

        #region Network Event Handlers

        private void OnServerStarted()
        {
            _isHost = true;
            Debug.Log("[NetworkExitManager] Server started - Host mode enabled");
        }

        private void OnClientStarted()
        {
            Debug.Log("[NetworkExitManager] Client started");
        }

        private void OnServerStopped(bool wasHost)
        {
            if (wasHost) _isHost = false;
            Debug.Log("[NetworkExitManager] Server stopped");
        }

        private void OnClientStopped(bool wasHost)
        {
            if (wasHost) _isHost = false;
            Debug.Log("[NetworkExitManager] Client stopped");
            
            // Nếu không phải do chúng ta chủ động thoát thì có thể do host disconnect
            if (!_isExiting)
            {
                HandleUnexpectedDisconnect();
            }
        }

        #endregion

        #region Public Exit Methods

        /// <summary>
        /// Người chơi muốn thoát về main menu (không quit app)
        /// </summary>
        public async void ExitToMainMenu()
        {
            if (_isExiting) return;
            
            Debug.Log("[NetworkExitManager] Player wants to exit to main menu");
            _isExiting = true;

            try
            {
                if (_isHost)
                {
                    await HandleHostExit();
                }
                else
                {
                    await HandleClientExit();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkExitManager] Error during exit: {ex.Message}");
            }
            finally
            {
                // Chuyển về main menu
                await LoadMainMenu();
                _isExiting = false;
            }
        }

        /// <summary>
        /// Force exit - chỉ dùng khi cần thiết (emergency)
        /// </summary>
        public async void ForceExit()
        {
            Debug.LogWarning("[NetworkExitManager] Force exit initiated");
            _isExiting = true;

            try
            {
                // Quick cleanup without waiting
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    NetworkManager.Singleton.Shutdown(true);
                }

                await CleanupLobby();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkExitManager] Error during force exit: {ex.Message}");
            }
            finally
            {
                await LoadMainMenu();
                _isExiting = false;
            }
        }

        #endregion

        #region Exit Handlers

        private async Task HandleHostExit()
        {
            Debug.Log("[NetworkExitManager] Host exit: Shutting down server and kicking all players");

            try
            {
                // Notify all clients that host is leaving
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                {
                    // Send notification to all clients (if you have a custom message system)
                    NotifyClientsOfHostExit();
                    
                    // Give clients a moment to process the message
                    await Task.Delay(500);
                    
                    // Shutdown server
                    NetworkManager.Singleton.Shutdown(true);
                }

                // Cleanup lobby
                await CleanupLobby();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkExitManager] Error in host exit: {ex.Message}");
            }
        }

        private async Task HandleClientExit()
        {
            Debug.Log("[NetworkExitManager] Client exit: Disconnecting from server");

            try
            {
                // Disconnect from server
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
                {
                    NetworkManager.Singleton.Shutdown(false);
                }

                // Leave lobby
                await LeaveLobby();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkExitManager] Error in client exit: {ex.Message}");
            }
        }

        private void HandleUnexpectedDisconnect()
        {
            Debug.LogWarning("[NetworkExitManager] Unexpected disconnect detected");
            
            // Show error message to player
            // You can implement UI notification here
            ShowDisconnectMessage("Connection to host lost");
            
            // Return to main menu after a delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000); // Give time for user to see message
                UnityMainThreadDispatcher.Enqueue(() => {
                    _ = LoadMainMenu();
                });
            });
        }

        #endregion

        #region AppExitController Integration

        private void HandleBestEffortNetworkPause(CancellationToken cancellationToken)
        {
            try
            {
                // Quick pause - don't disconnect, just prepare for background
                Debug.Log("[NetworkExitManager] Best-effort network pause");
                
                // You might want to pause game state, save progress, etc.
                // Keep it simple and fast - no network calls
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetworkExitManager] Best-effort pause error: {ex.Message}");
            }
        }

        private async Task HandleFullNetworkCleanup(CancellationToken cancellationToken)
        {
            Debug.Log("[NetworkExitManager] Full network cleanup for app exit");
            _isExiting = true;

            try
            {
                // Cleanup network
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(networkShutdownTimeout));

                    var shutdownTask = Task.Run(() => {
                        NetworkManager.Singleton.Shutdown(true);
                    });

                    var delayTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);

                    try
                    {
                        var completedTask = await Task.WhenAny(shutdownTask, delayTask);
                
                        if (completedTask == delayTask)
                        {
                            Debug.LogWarning("[NetworkExitManager] Network shutdown timeout");
                        }
                        else
                        {
                            // Đảm bảo task hoàn thành thành công
                            await shutdownTask;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.LogWarning("[NetworkExitManager] Network shutdown timeout");
                    }
                }

                // Cleanup lobby
                await CleanupLobby(cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkExitManager] Full cleanup error: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Helper Methods

        private void NotifyClientsOfHostExit()
        {
            // Implement your custom network message system here
            // Example: Send a "HostLeaving" message to all clients
            Debug.Log("[NetworkExitManager] Notifying clients of host exit");
        }

        private async Task CleanupLobby(CancellationToken cancellationToken = default)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(lobbyCleanupTimeout));

                var currentLobby = LobbyExtensions.GetCurrentLobby();
                if (currentLobby != null)
                {
                    if (_isHost)
                    {
                        // Delete lobby if host
                        await LobbyService.Instance.DeleteLobbyAsync(currentLobby.Id);
                        Debug.Log("[NetworkExitManager] Lobby deleted");
                    }
                    else
                    {
                        // Leave lobby if client
                        await LeaveLobby(timeoutCts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[NetworkExitManager] Lobby cleanup timeout");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkExitManager] Lobby cleanup error: {ex.Message}");
            }
        }

        private async Task LeaveLobby(CancellationToken cancellationToken = default)
        {
            try
            {
                var currentLobby = LobbyExtensions.GetCurrentLobby();
                if (currentLobby != null)
                {
                    var playerId = NetIdHub.PlayerId;
                    if (!string.IsNullOrEmpty(playerId))
                    {
                        await LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, playerId);
                        Debug.Log("[NetworkExitManager] Left lobby");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkExitManager] Leave lobby error: {ex.Message}");
            }
        }

        private async Task LoadMainMenu()
        {
            try
            {
                Debug.Log($"[NetworkExitManager] Loading main menu: {HomeScene}");
                
                // Use Unity's scene loading
                var asyncLoad = SceneManager.LoadSceneAsync(HomeScene);
                
                while (!asyncLoad.isDone)
                {
                    await Task.Yield();
                }
                
                Debug.Log("[NetworkExitManager] Main menu loaded successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkExitManager] Error loading main menu: {ex.Message}");
            }
        }

        private void ShowDisconnectMessage(string message)
        {
            // Implement UI notification here
            Debug.LogWarning($"[NetworkExitManager] Disconnect Message: {message}");
            
            // Example: Show popup or notification
            // UIManager.Instance?.ShowNotification(message);
        }

        #endregion

        #region Debug Methods

        [ContextMenu("Test Exit To Main Menu")]
        private void TestExitToMainMenu()
        {
            ExitToMainMenu();
        }

        [ContextMenu("Test Force Exit")]
        private void TestForceExit()
        {
            ForceExit();
        }

        #endregion
    }

    #region Utility Classes

    /// <summary>
    /// Simple main thread dispatcher for callbacks from background tasks
    /// </summary>
    public static class UnityMainThreadDispatcher
    {
        private static readonly System.Collections.Generic.Queue<Action> _queue = new();

        static UnityMainThreadDispatcher()
        {
            // Create a GameObject to handle Update calls
            var go = new GameObject("MainThreadDispatcher");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<MainThreadDispatcherBehaviour>();
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_queue) _queue.Enqueue(action);
        }

        internal static void ProcessQueue()
        {
            if (_queue.Count == 0) return;
            Action action = null;
            lock (_queue)
            {
                if (_queue.Count > 0) action = _queue.Dequeue();
            }
            action?.Invoke();
        }
    }

    internal class MainThreadDispatcherBehaviour : MonoBehaviour
    {
        private void Update()
        {
            UnityMainThreadDispatcher.ProcessQueue();
        }
    }

    #endregion
}
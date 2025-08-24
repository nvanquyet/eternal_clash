using System;
using System.Collections;
using System.Threading.Tasks;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Networking.Lobbies;
using _GAME.Scripts.Networking.Relay;
using _GAME.Scripts.UI;
using GAME.Scripts.DesignPattern;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace _GAME.Scripts.Networking
{
    /// <summary>
    /// Manages network session lifecycle: lobby creation/join -> relay setup -> network start -> scene management
    /// Handles both host and client flows with proper error handling and timeout management
    /// </summary>
    public class NetSessionManager : SingletonDontDestroy<NetSessionManager>
    {
        [Header("Settings")] [SerializeField] private SceneDefinitions waitingRoomScene = SceneDefinitions.WaitingRoom;
        [SerializeField] private int maxRetries = 3;
        [SerializeField] private float retryDelay = 2f;
        [SerializeField] private float networkTimeout = 15f;
        [SerializeField] private float relayCodeWaitTimeout = 20f;

        // State management
        private NetworkSessionState _sessionState = NetworkSessionState.Idle;
        private Coroutine _timeoutCoroutine;
        private Coroutine _relayWaitCoroutine;
        private bool _isShuttingDown = false;

        private enum NetworkSessionState
        {
            Idle,
            HostingRelay,
            JoiningRelay,
            StartingNetwork,
            NetworkActive,
            ShuttingDown,
            Failed
        }

        #region Unity Lifecycle

        protected override void OnAwake()
        {
            base.OnAwake();
            DontDestroyOnLoad(gameObject);
           
        }

        private void Start()
        {
            RegisterEvents();
        }

        private void OnDisable()
        {
            StopAllCoroutines();
        }
        

        private void RegisterEvents()
        {
            // Initialize NetIdHub
            NetIdHub.Wire();

            // Lobby events
            LobbyEvents.OnLobbyCreated += HandleLobbyCreated;
            LobbyEvents.OnLobbyJoined += HandleLobbyJoined;
            LobbyEvents.OnLobbyUpdated += HandleLobbyUpdated;
            LobbyEvents.OnLeftLobby += HandleLeftLobby;
            LobbyEvents.OnLobbyRemoved += HandleLobbyRemoved;
            LobbyEvents.OnPlayerKicked += HandlePlayerKicked;

            // Network events
            RegisterNetworkEvents();
        }

        private void UnregisterEvents()
        {
            // Lobby events
            LobbyEvents.OnLobbyCreated -= HandleLobbyCreated;
            LobbyEvents.OnLobbyJoined -= HandleLobbyJoined;
            LobbyEvents.OnLobbyUpdated -= HandleLobbyUpdated;
            LobbyEvents.OnLeftLobby -= HandleLeftLobby;
            LobbyEvents.OnLobbyRemoved -= HandleLobbyRemoved;
            LobbyEvents.OnPlayerKicked -= HandlePlayerKicked;

            // Network events
            UnregisterNetworkEvents();
        }

        private void RegisterNetworkEvents()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            nm.OnServerStarted += OnServerStarted;
            nm.OnServerStopped += OnServerStopped;
            nm.OnClientStarted += OnClientStarted;
            nm.OnClientStopped += OnClientStopped;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
        }

        private void UnregisterNetworkEvents()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            nm.OnServerStarted -= OnServerStarted;
            nm.OnServerStopped -= OnServerStopped;
            nm.OnClientStarted -= OnClientStarted;
            nm.OnClientStopped -= OnClientStopped;
            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        #endregion

        #region Lobby Event Handlers

        private async void HandleLobbyCreated(Lobby lobby, bool success, string message)
        {
            try
            {
                if (!success || lobby == null)
                {
                    Debug.LogError($"[NetSessionManager] Lobby creation failed: {message}");
                    return;
                }

                if (!IsLocalHost(lobby))
                {
                    Debug.Log("[NetSessionManager] Not the host, skipping host flow");
                    return;
                }

                if (_sessionState != NetworkSessionState.Idle)
                {
                    Debug.LogWarning($"[NetSessionManager] Cannot start host flow, current state: {_sessionState}");
                    return;
                }

                Debug.Log("[NetSessionManager] Starting host flow...");
                await StartHostFlow(lobby);
            }
            catch (Exception e)
            {
                Debug.Log($"[NetSessionManager] Exception in HandleLobbyCreated: {e.Message}");
            }
        }

        private async void HandleLobbyJoined(Lobby lobby, bool success, string message)
        {
            try
            {
                if (!success || lobby == null)
                {
                    Debug.LogError($"[NetSessionManager] Lobby join failed: {message}");
                    return;
                }

                if (IsLocalHost(lobby))
                {
                    Debug.Log("[NetSessionManager] Joined as host, waiting for host flow completion");
                    return;
                }

                if (IsNetworkAlreadyRunning())
                {
                    Debug.Log("[NetSessionManager] Network already running, skipping client flow");
                    return;
                }

                Debug.Log("[NetSessionManager] Joined as client, checking for relay code...");

                if (!string.IsNullOrEmpty(NetIdHub.RelayJoinCode))
                {
                    await StartClientFlow(lobby);
                }
                else
                {
                    StartWaitingForRelayCode(lobby);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetSessionManager] Exception in HandleLobbyJoined: {e.Message}");
            }
        }

        private void HandleLobbyUpdated(Lobby lobby, string message)
        {
            if (lobby == null) return;

            // Update NetIdHub with fresh lobby data
            NetIdHub.BindLobby(lobby);

            // Skip if we're the host or already in a flow
            if (IsLocalHost(lobby) || _sessionState != NetworkSessionState.Idle)
                return;

            // Skip if network is already running
            if (IsNetworkAlreadyRunning())
                return;

            // Check for relay code and start client flow if available
            if (!string.IsNullOrEmpty(NetIdHub.RelayJoinCode))
            {
                Debug.Log("[NetSessionManager] Relay code detected in update, starting client flow");
                _ = StartClientFlow(lobby);
            }
        }

        private void HandleLeftLobby(Lobby lobby, bool success, string message)
        {
            if (success)
            {
                Debug.Log($"[NetSessionManager] Left lobby: {message}");
                PopupNotification.Instance.ShowPopup(true, "You have been left lobby", "Left Lobby");
                HandleSessionEnd("Lobby left");
            }
        }
        
        private void HandlePlayerKicked(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message)
        {
            if (player == null || lobby == null || string.IsNullOrEmpty(player.Id) || player.Id != NetIdHub.PlayerId)
                return;
            //Show popup
            PopupNotification.Instance.ShowPopup(false, "You have been kicked from the lobby", "Kicked");
            Debug.Log("[NetSessionManager] You have been kicked from the lobby");
            HandleSessionEnd("Kicked from lobby");
        }

        private void HandleLobbyRemoved(Lobby lobby, bool success, string message)
        {
            if (success)
            {
                Debug.Log($"[NetSessionManager] Lobby removed: {message}");
                PopupNotification.Instance.ShowPopup(false, "Lobby has been removed", "Removed");
                HandleSessionEnd("Lobby removed");
            }
        }

        #endregion

        #region Host Flow

        private async Task StartHostFlow(Lobby lobby)
        {
            if (_sessionState != NetworkSessionState.Idle)
            {
                Debug.LogWarning($"[NetSessionManager] Cannot start host flow, state: {_sessionState}");
                return;
            }

            _sessionState = NetworkSessionState.HostingRelay;
            StartTimeoutCoroutine("Host relay setup");

            try
            {
                Debug.Log("[NetSessionManager] Setting up host relay...");

                // Set connecting status in lobby
                await LobbyDataExtensions.SetNetworkStatusAsync(lobby.Id, LobbyConstants.NetworkStatus.CONNECTING);

                // Start host with relay
                bool started = await NetworkStarter.HostWithRelayAsync(
                    lobby.MaxPlayers,
                    async (joinCode) =>
                    {
                        Debug.Log($"[NetSessionManager] Relay code generated: {joinCode}");
                        await LobbyDataExtensions.SetRelayJoinCodeAsync(lobby.Id, joinCode);
                    }
                );

                if (!started)
                {
                    throw new Exception("Failed to start host network");
                }

                _sessionState = NetworkSessionState.StartingNetwork;
                Debug.Log("[NetSessionManager] Host network start initiated...");
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetSessionManager] Host flow failed: {e.Message}");
                await HandleHostFlowFailure(lobby.Id, e.Message);
            }
        }

        private async Task HandleHostFlowFailure(string lobbyId, string error)
        {
            _sessionState = NetworkSessionState.Failed;
            StopTimeoutCoroutine();

            if (!string.IsNullOrEmpty(lobbyId))
            {
                await LobbyDataExtensions.SetNetworkStatusAsync(lobbyId, LobbyConstants.NetworkStatus.FAILED);
            }

            LobbyEvents.TriggerRelayError($"Host setup failed: {error}");
            ResetSession();
        }

        #endregion

        #region Client Flow

        private void StartWaitingForRelayCode(Lobby lobby)
        {
            if (_relayWaitCoroutine != null)
                StopCoroutine(_relayWaitCoroutine);

            _relayWaitCoroutine = StartCoroutine(WaitForRelayCodeCoroutine(lobby));
        }

        private IEnumerator WaitForRelayCodeCoroutine(Lobby lobby)
        {
            Debug.Log("[NetSessionManager] Waiting for relay code from host...");
            float elapsed = 0f;

            while (elapsed < relayCodeWaitTimeout && _sessionState == NetworkSessionState.Idle)
            {
                // Check for relay code
                if (!string.IsNullOrEmpty(NetIdHub.RelayJoinCode))
                {
                    Debug.Log("[NetSessionManager] Relay code received, starting client flow");
                    _ = StartClientFlow(lobby);
                    yield break;
                }

                // Check if we should stop waiting
                if (string.IsNullOrEmpty(NetIdHub.LobbyId) || _isShuttingDown)
                {
                    Debug.Log("[NetSessionManager] Stopping relay code wait - session ended");
                    yield break;
                }

                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            if (_sessionState == NetworkSessionState.Idle)
            {
                Debug.LogWarning($"[NetSessionManager] Timeout waiting for relay code after {relayCodeWaitTimeout}s");
                LobbyEvents.TriggerRelayError("Timeout waiting for host setup");
            }
        }

        private async Task StartClientFlow(Lobby lobby)
        {
            if (_sessionState != NetworkSessionState.Idle)
            {
                Debug.LogWarning($"[NetSessionManager] Cannot start client flow, state: {_sessionState}");
                return;
            }

            _sessionState = NetworkSessionState.JoiningRelay;
            StartTimeoutCoroutine("Client relay connection");

            try
            {
                var joinCode = NetIdHub.RelayJoinCode;
                Debug.Log($"[NetSessionManager] Connecting to relay with code: {joinCode}");

                bool started = await NetworkStarter.ClientWithRelayAsync(joinCode);
                if (!started)
                {
                    throw new Exception("Failed to start client network");
                }

                _sessionState = NetworkSessionState.StartingNetwork;
                Debug.Log("[NetSessionManager] Client network start initiated...");
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetSessionManager] Client flow failed: {e.Message}");
                await HandleClientFlowFailure(e.Message);
            }
        }

        private async Task HandleClientFlowFailure(string error)
        {
            _sessionState = NetworkSessionState.Failed;
            StopTimeoutCoroutine();

            LobbyEvents.TriggerRelayError($"Client connection failed: {error}");

            // Try to retry connection
            await RetryClientConnection();
        }

        private async Task RetryClientConnection()
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                Debug.Log($"[NetSessionManager] Retry attempt {attempt}/{maxRetries}");
                await Task.Delay(TimeSpan.FromSeconds(retryDelay));

                // Check if we should stop retrying
                if (string.IsNullOrEmpty(NetIdHub.LobbyId) || _isShuttingDown)
                {
                    Debug.Log("[NetSessionManager] Stopping retry - session ended");
                    return;
                }

                try
                {
                    var lobby = LobbyExtensions.GetCurrentLobby();
                    if (lobby != null)
                    {
                        _sessionState = NetworkSessionState.Idle; // Reset state for retry
                        await StartClientFlow(lobby);
                        return; // Success, exit retry loop
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NetSessionManager] Retry {attempt} failed: {e.Message}");
                }
            }

            Debug.LogError("[NetSessionManager] All retry attempts failed");
            ResetSession();
        }

        #endregion

        #region Network Event Handlers

        private async void OnServerStarted()
        {
            try
            {
                Debug.Log("[NetSessionManager] Server started successfully");
                _sessionState = NetworkSessionState.NetworkActive;
                StopTimeoutCoroutine();

                var lobbyId = NetIdHub.LobbyId;
                if (!string.IsNullOrEmpty(lobbyId))
                {
                    await LobbyDataExtensions.SetNetworkStatusAsync(lobbyId, LobbyConstants.NetworkStatus.READY);
                }

                LobbyEvents.TriggerRelayHostReady(NetIdHub.RelayJoinCode ?? "");
                LoadWaitingRoomScene();
            }
            catch (Exception e)
            {
                Debug.Log($"[NetSessionManager] Exception in OnServerStarted: {e.Message}");
            }
        }

        private void OnServerStopped(bool wasHost)
        {
            Debug.Log($"[NetSessionManager] Server stopped (wasHost: {wasHost})");

            if (_sessionState == NetworkSessionState.StartingNetwork && !_isShuttingDown)
            {
                Debug.LogWarning("[NetSessionManager] Server stopped unexpectedly during startup");
                _ = HandleHostFlowFailure(NetIdHub.LobbyId, "Server stopped unexpectedly");
            }
            else if (!_isShuttingDown)
            {
                HandleSessionEnd("Server stopped");
            }
        }

        private void OnClientStarted()
        {
            Debug.Log("[NetSessionManager] Client started");
        }

        private void OnClientStopped(bool wasHost)
        {
            Debug.Log($"[NetSessionManager] Client stopped (wasHost: {wasHost})");

            if (_sessionState == NetworkSessionState.StartingNetwork && !_isShuttingDown)
            {
                Debug.LogWarning("[NetSessionManager] Client stopped unexpectedly during startup");
                _ = HandleClientFlowFailure("Client stopped unexpectedly");
            }
            else if (!_isShuttingDown)
            {
                HandleSessionEnd("Client stopped");
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[NetSessionManager] Client {clientId} connected");

            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                Debug.Log("[NetSessionManager] Local client connected successfully");
                _sessionState = NetworkSessionState.NetworkActive;
                StopTimeoutCoroutine();
                LobbyEvents.TriggerRelayClientReady();
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.Log($"[NetSessionManager] Client {clientId} disconnected");

            if (clientId == NetworkManager.Singleton.LocalClientId && !_isShuttingDown)
            {
                Debug.Log("[NetSessionManager] Local client disconnected");
                HandleSessionEnd("Disconnected from server");
            }
        }

        #endregion

        #region Session Management

        private void HandleSessionEnd(string reason)
        {
            if (_isShuttingDown) return;

            Debug.Log($"[NetSessionManager] Session ended: {reason}");
            _isShuttingDown = true;

            StopAllCoroutines();
            ResetSession();
            ShutdownNetwork();
            
            //Shutdown lobby
            LobbyExtensions.ShutdownLobbyAsync();

            // Return to home scene after a brief delay
            StartCoroutine(ReturnToHomeCoroutine());
        }

        private IEnumerator ReturnToHomeCoroutine()
        {
            yield return new WaitForSeconds(0.5f);

            Debug.Log("[NetSessionManager] Returning to home scene");
            SceneManager.LoadScene(SceneHelper.ToSceneName(SceneDefinitions.Home));

            yield return new WaitForSeconds(1f);
            _isShuttingDown = false;
        }

        private void ResetSession()
        {
            _sessionState = NetworkSessionState.Idle;
            StopTimeoutCoroutine();

            if (_relayWaitCoroutine != null)
            {
                StopCoroutine(_relayWaitCoroutine);
                _relayWaitCoroutine = null;
            }
        }

        private void ShutdownNetwork()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer || nm.IsHost))
            {
                Debug.Log("[NetSessionManager] Shutting down NetworkManager");
                try
                {
                    nm.Shutdown();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[NetSessionManager] Error during network shutdown: {e.Message}");
                }
            }
        }

        private void LoadWaitingRoomScene()
        {
            var nm = NetworkManager.Singleton;
            if (nm?.SceneManager != null)
            {
                try
                {
                    nm.SceneManager.LoadScene(SceneHelper.ToSceneName(waitingRoomScene), LoadSceneMode.Single);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[NetSessionManager] Failed to load waiting room scene: {e.Message}");
                }
            }
        }

        #endregion

        #region Timeout Management

        private void StartTimeoutCoroutine(string operation)
        {
            StopTimeoutCoroutine();
            _timeoutCoroutine = StartCoroutine(TimeoutCoroutine(operation));
        }

        private void StopTimeoutCoroutine()
        {
            if (_timeoutCoroutine != null)
            {
                StopCoroutine(_timeoutCoroutine);
                _timeoutCoroutine = null;
            }
        }

        private IEnumerator TimeoutCoroutine(string operation)
        {
            yield return new WaitForSeconds(networkTimeout);

            if (_sessionState == NetworkSessionState.HostingRelay ||
                _sessionState == NetworkSessionState.JoiningRelay ||
                _sessionState == NetworkSessionState.StartingNetwork)
            {
                Debug.LogError($"[NetSessionManager] {operation} timeout after {networkTimeout}s");
                HandleTimeout(operation);
            }
        }

        private void HandleTimeout(string operation)
        {
            _sessionState = NetworkSessionState.Failed;

            var error = $"{operation} timeout";
            LobbyEvents.TriggerRelayError(error);

            ResetSession();
        }

        #endregion

        #region Utility Methods

        private bool IsLocalHost(Lobby lobby)
        {
            var myId = AuthenticationService.Instance?.PlayerId;
            return lobby != null && !string.IsNullOrEmpty(myId) && lobby.HostId == myId;
        }

        private bool IsNetworkAlreadyRunning()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && (nm.IsServer || nm.IsHost || nm.IsClient);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Force shutdown the current session
        /// </summary>
        public void ForceShutdown()
        {
            Debug.Log("[NetSessionManager] Force shutdown requested");
            HandleSessionEnd("Force shutdown");
        }

        /// <summary>
        /// Get current session state
        /// </summary>
        public bool IsSessionActive()
        {
            return _sessionState == NetworkSessionState.NetworkActive;
        }

        /// <summary>
        /// Get current session state for debugging
        /// </summary>
        public string GetSessionState()
        {
            return _sessionState.ToString();
        }

        #endregion

        #region Cleanup

        protected override void OnDestroy()
        {
            _isShuttingDown = true;
            StopAllCoroutines();
            UnregisterEvents();
            ShutdownNetwork();
            base.OnDestroy();
        }

        #endregion

        #region Debug

        [ContextMenu("Debug Session State")]
        private void DebugSessionState()
        {
            var nm = NetworkManager.Singleton;
            Debug.Log($"[NetSessionManager] Debug State:" +
                      $"\n  Session State: {_sessionState}" +
                      $"\n  Is Shutting Down: {_isShuttingDown}" +
                      $"\n  NetworkManager: {(nm != null ? $"Host:{nm.IsHost}, Server:{nm.IsServer}, Client:{nm.IsClient}" : "null")}" +
                      $"\n  Lobby ID: {NetIdHub.LobbyId}" +
                      $"\n  Relay Code: {NetIdHub.RelayJoinCode}" +
                      $"\n  Is Local Host: {NetIdHub.IsLocalHost()}");
        }

        [ContextMenu("Force Reset Session")]
        private void ForceResetSession()
        {
            Debug.Log("[NetSessionManager] Force reset session (debug)");
            ResetSession();
            ShutdownNetwork();
        }

        #endregion
    }
}
using System;
using System.Threading.Tasks;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Networking.Lobbies;
using _GAME.Scripts.Networking.Relay;
using GAME.Scripts.DesignPattern;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace _GAME.Scripts.Networking
{
    public class NetSessionManager : SingletonDontDestroy<NetSessionManager>
    {
        [SerializeField] private SceneDefinitions waitingRoomScene = SceneDefinitions.WaitingRoom;
        [SerializeField] private int maxRetries = 3;
        [SerializeField] private float retryDelay = 2f;

        private bool _hostFlowStarting = false;   // set = true ngay khi vào HandleLobbyCreated
        private bool _clientFlowStarting = false; // set = true ngay khi vào TryJoinRelay


        private void OnEnable()
        {
            LobbyEvents.OnLobbyCreated += HandleLobbyCreated;
            LobbyEvents.OnLobbyJoined += HandleLobbyJoined;
            LobbyEvents.OnLobbyUpdated += HandleLobbyUpdated; // Listen for relay code updates
            LobbyEvents.OnLobbyRemoved += HandleLobbyRemoved;

            // Network events
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
                NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            }
        }

        private void OnDisable()
        {
            LobbyEvents.OnLobbyCreated -= HandleLobbyCreated;
            LobbyEvents.OnLobbyJoined -= HandleLobbyJoined;
            LobbyEvents.OnLobbyUpdated -= HandleLobbyUpdated;
            LobbyEvents.OnLobbyRemoved -= HandleLobbyRemoved;

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
                NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            }
        }

        
        private static bool IsLocalLobbyHost(Lobby lobby)
        {
            var myId = AuthenticationService.Instance?.PlayerId;
            return lobby != null && !string.IsNullOrEmpty(myId) && lobby.HostId == myId;
        }
        
        // HOST: Lobby created -> Create Relay -> Set join code -> Start host
        private async void HandleLobbyCreated(Lobby lobby, bool ok, string msg)
        {
            if (!ok || lobby == null) return;
            if (_hostFlowStarting) return; 
            _hostFlowStarting = true;
            try
            {
                // Set connecting status
                await RelayExtension.SetNetworkStatusAsync(lobby.Id, LobbyConstants.NetworkStatus.CONNECTING);

                bool started = await NetworkStarter.HostWithRelayAsync(
                    lobby.MaxPlayers,
                    async (joinCode) =>
                    {
                        await RelayExtension.SetRelayJoinCodeAsync(lobby.Id, joinCode);
                        Debug.Log($"[NetSessionManager] Relay join code set: {joinCode}");
                    }
                );

                if (!started)
                    throw new Exception("StartHost failed");

                // Will be set to READY in OnServerStarted
                Debug.Log("[NetSessionManager] Host relay setup completed");
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetSessionManager] Host flow failed: {e.Message}");
                _hostFlowStarting = false;
                await RelayExtension.SetNetworkStatusAsync(lobby.Id, LobbyConstants.NetworkStatus.FAILED);
                RelayEvent.TriggerRelayError(e.Message);
                NetworkManager.Singleton?.Shutdown();
            }
        }

        // CLIENT: Join lobby -> Wait for relay code -> Join relay -> Start client
        private async void HandleLobbyJoined(Lobby lobby, bool ok, string msg)
        {
            if (!ok || lobby == null) return;

            if (NetworkManager.Singleton != null &&
                (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost))
            {
                Debug.Log("[NetSessionManager] Ignore OnLobbyJoined on host.");
                return;
            }
            
            // Check if relay code is already available
            if (lobby.HasValidRelayCode())
            {
                await TryJoinRelay(lobby);
            }
            // Otherwise wait for lobby updates
        }

        // Handle lobby updates - check for new relay join code
        private async void HandleLobbyUpdated(Lobby lobby, string msg)
        {
            if (lobby == null) return;

            // ⛔️ Là host thì tuyệt đối không join dưới nhánh client
            if (IsLocalLobbyHost(lobby) || _hostFlowStarting) return;

            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            // ⛔️ Đang chạy client/server hoặc đang khởi động -> bỏ qua
            if (nm.IsClient || nm.IsServer || _clientFlowStarting) return;

            if (lobby.HasValidRelayCode())
            {
                await TryJoinRelay(lobby);
            }
        }
        // private async void HandleLobbyUpdated(Lobby lobby, string msg)
        // {
        //     if (lobby == null) return;
        //
        //     // If we're not connected and relay code becomes available
        //     if (!NetworkManager.Singleton.IsClient ||
        //         !NetworkManager.Singleton.IsServer ||
        //         lobby.HasValidRelayCode())
        //     {
        //         await TryJoinRelay(lobby);
        //     }
        // }

        private async Task TryJoinRelay(Lobby lobby)
        {
            if (_clientFlowStarting) return;
            _clientFlowStarting = true;
            try
            {
                var code = lobby.GetRelayJoinCode();
                Debug.Log($"[NetSessionManager] Attempting to join relay with code: {code}");

                bool started = await NetworkStarter.ClientWithRelayAsync(code);
                if (!started)
                    throw new Exception("StartClient failed");

                Debug.Log("[NetSessionManager] Client relay connection initiated");
            }
            catch (Exception e)
            {
                _clientFlowStarting = false;
                Debug.LogError($"[NetSessionManager] Client relay join failed: {e.Message}");
                RelayEvent.TriggerRelayError(e.Message);

                // Optionally retry
                await RetryJoinRelay(lobby);
            }
        }

        private async Task RetryJoinRelay(Lobby lobby)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(retryDelay));

                try
                {
                    Debug.Log($"[NetSessionManager] Retry {i + 1}/{maxRetries}");
                    await TryJoinRelay(lobby);
                    return; // Success
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NetSessionManager] Retry {i + 1} failed: {e.Message}");
                }
            }

            Debug.LogError("[NetSessionManager] All retry attempts failed");
        }

        // Network callbacks
        private async void OnServerStarted()
        {
            Debug.Log("[NetSessionManager] Server started successfully");
            var lobbyId = LobbyExtensions.GetCurrentLobbyId();
            if (!string.IsNullOrEmpty(lobbyId))
            {
                await RelayExtension.SetNetworkStatusAsync(lobbyId, LobbyConstants.NetworkStatus.READY);
            }

            RelayEvent.TriggerRelayHostReady(LobbyExtensions.GetCurrentLobby()?.GetRelayJoinCode() ?? "");

            // Load waiting room scene
            NetworkManager.Singleton.SceneManager.LoadScene(
                SceneHelper.ToSceneName(waitingRoomScene), LoadSceneMode.Single);
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[NetSessionManager] Client {clientId} connected");
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning("[NetSessionManager] OnClientConnected called but not a server instance.");
                return;
            }
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                RelayEvent.TriggerRelayClientReady();
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.Log($"[NetSessionManager] Client {clientId} disconnected");

            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                // Local client disconnected - return to home
                HandleNetworkDisconnection();
            }
        }

        private void HandleLobbyRemoved(Lobby l, bool s, string c)
        {
            HandleNetworkDisconnection();
        }

        private void HandleNetworkDisconnection()
        {
            if (NetworkManager.Singleton != null &&
                (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                NetworkManager.Singleton.Shutdown();
            }

            SceneManager.LoadScene(SceneHelper.ToSceneName(SceneDefinitions.Home));
        }
    }
}
using System;
using System.Threading.Tasks;
using _GAME.Scripts.Networking.Lobbies;
using _GAME.Scripts.Networking.Relay;
using _GAME.Scripts.UI;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking
{
    /// <summary>
    /// Fixed LobbyManager với proper sequencing và validation
    /// </summary>
    public class LobbyManager : MonoBehaviour
    {
        #region Components & Fields
        
        [Header("Components")]
        [SerializeField] private LobbyRuntime lobbyRuntime;
        
        [Header("Settings")]
        [SerializeField] private float heartbeatInterval = 15f;
        [SerializeField] private float updateInterval = 4f;
        [SerializeField] private float relayCodeTimeout = 10f; // Timeout chờ relay code

        private LobbyHandler _handler;
        private bool _isInitialized = false;

        #endregion

        #region Properties

        public Lobby CurrentLobby => _handler?.CachedLobby;
        public string LobbyId => CurrentLobby?.Id;
        public string LobbyCode => CurrentLobby?.LobbyCode;
        public string HostId => CurrentLobby?.HostId;
        public string RelayJoinCode => CurrentLobby?.GetRelayJoinCode();

        public bool IsInLobby => CurrentLobby != null;
        public bool IsHost => IsInLobby && CurrentLobby.HostId == PlayerIdManager.PlayerId;
        public Unity.Services.Lobbies.Models.Player CurrentPlayer => 
            CurrentLobby?.Players?.Find(p => p.Id == PlayerIdManager.PlayerId);

        public LobbyRuntime Runtime => lobbyRuntime;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            InitializeComponents();
        }

        private void OnValidate()
        {
            lobbyRuntime ??= GetComponent<LobbyRuntime>();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        #endregion

        #region Initialization

        private void InitializeComponents()
        {
            if (_isInitialized) return;

            try
            {
                lobbyRuntime ??= GetComponent<LobbyRuntime>();

                if (lobbyRuntime == null)
                {
                    Debug.LogError("[LobbyManager] LobbyRuntime component missing");
                    return;
                }

                _handler = new LobbyHandler();
                _handler.InitializeComponents(lobbyRuntime);
                
                lobbyRuntime.Initialize(heartbeatInterval, updateInterval);

                _isInitialized = true;
                Debug.Log("[LobbyManager] Initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Initialization failed: {ex.Message}");
            }
        }

        #endregion

        #region Public API - Host Operations

        public async Task<OperationResult> CreateLobbyAsync(string lobbyName, int maxPlayers, CreateLobbyOptions options = null)
        {
            if (!ValidateOperation("CreateLobby")) 
                return OperationResult.Failure("System not ready");

            try
            {
                // Step 1: Create lobby (WITHOUT starting runtime yet)
                var createdLobby = await _handler.CreateLobbyAsync(lobbyName, maxPlayers, options); 
                if (createdLobby == null)
                {
                    LoadingUI.Instance.SetProgress(1,1, "Failed to create lobby");
                    return OperationResult.Failure("Failed to create lobby");
                }
                
                LoadingUI.Instance.SetProgress(0.5f,1, "Setup hosting...");
                // Step 2: Start network as host (get relay code)
                var networkResult = await GameNet.Instance.Network.StartHostAsync(maxPlayers);
                if (!networkResult.IsSuccess)
                {
                    // Rollback: delete lobby
                    await RollbackLobbyCreation(createdLobby.Id);
                    return OperationResult.Failure($"Network failed: {networkResult.ErrorMessage}");
                }
                
                // Step 3: Update lobby with relay code
                LoadingUI.Instance.SetProgress(0.8f,1, "Finalizing...");
                var joinCode = networkResult.JoinCode;
                var updateSuccess = await SetRelayJoinCodeAsync(joinCode);
                if (!updateSuccess)
                {
                    Debug.LogWarning("[LobbyManager] Failed to set relay code, but continuing...");
                }

                // Step 4: NOW start runtime (sau khi relay code đã được set)
                lobbyRuntime?.StartRuntime(createdLobby.Id, true);
                Debug.Log($"[LobbyManager] Lobby created successfully: {joinCode}");
                return OperationResult.Success("Lobby created successfully", joinCode);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] CreateLobby failed: {ex.Message}");
                await GameNet.Instance.Network.StopAsync();
                return OperationResult.Failure($"Create failed: {ex.Message}");
            }
        }

        #endregion

        #region Public API - Client Operations

        public async Task<OperationResult> JoinLobbyAsync(string code, string password = null)
        {
            if (!ValidateOperation("JoinLobby")) 
                return OperationResult.Failure("System not ready");

            Lobby joinedLobby = null;
            bool networkStarted = false;

            try
            {
                // Step 1: Join lobby service
                var joinSuccess = await _handler.PrecheckPhaseThenJoin(code, password);
                if (!joinSuccess)
                {
                    return OperationResult.Failure("Failed to join lobby");
                }
                LoadingUI.Instance.SetProgress(0.3f, 1, "Wait connecting...");
                joinedLobby = CurrentLobby;

                // Step 2: Wait for relay code (với timeout)
                var relayCode = await WaitForRelayCodeAsync(relayCodeTimeout);
                if (string.IsNullOrEmpty(relayCode))
                {
                    await RollbackLobbyJoin(joinedLobby.Id);
                    return OperationResult.Failure("Relay code not available");
                }
                LoadingUI.Instance.SetProgress(0.8f, 1, "Finishing...");
                // Step 3: Start network as client
                var networkResult = await GameNet.Instance.Network.StartClientAsync(relayCode);
                if (!networkResult.IsSuccess)
                {
                    await RollbackLobbyJoin(joinedLobby.Id);
                    return OperationResult.Failure($"Network failed: {networkResult.ErrorMessage}");
                }
                networkStarted = true;

                // Step 4: Start runtime (sau khi network đã connect)
                lobbyRuntime?.StartRuntime(joinedLobby.Id, false);

                Debug.Log($"[LobbyManager] Joined lobby: {code}");
                return OperationResult.Success($"Joined lobby: {code}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] JoinLobby failed: {ex.Message}");
                
                // Cleanup on failure
                if (networkStarted)
                {
                    await GameNet.Instance.Network.StopAsync();
                }
                if (joinedLobby != null)
                {
                    await RollbackLobbyJoin(joinedLobby.Id);
                }
                
                return OperationResult.Failure($"Join failed: {ex.Message}");
            }
        }

        #endregion

        #region Public API - Lobby Management

        public async Task<OperationResult> LeaveLobbyAsync()
        {
            if (!IsInLobby) 
                return OperationResult.Failure("Not in lobby");

            if (IsHost)
            {
                return OperationResult.Failure("Host must remove lobby, not leave");
            }

            try
            {
                // Step 1: Stop runtime first (ngừng polling)
                lobbyRuntime?.StopRuntime();

                // Step 2: Shutdown network
                await GameNet.Instance.Network.StopAsync();

                // Step 3: Leave lobby service
                var success = await _handler.LeaveLobbyAsync();
                if (!success)
                {
                    return OperationResult.Failure("Failed to leave lobby");
                }

                // Step 4: Cleanup relay
                await RelayHandler.SafeShutdownAsync();

                return OperationResult.Success("Left lobby successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] LeaveLobby failed: {ex.Message}");
                return OperationResult.Failure($"Leave failed: {ex.Message}");
            }
        }

        public async Task<OperationResult> RemoveLobbyAsync()
        {
            if (!IsHost) 
                return OperationResult.Failure("Only host can remove lobby");

            try
            {
                // Step 1: Stop runtime first
                lobbyRuntime?.StopRuntime();

                // Step 2: Shutdown network (disconnect all clients)
                await GameNet.Instance.Network.StopAsync();

                // Step 3: Delete lobby
                var success = await _handler.RemoveLobbyAsync();
                if (!success)
                {
                    return OperationResult.Failure("Failed to remove lobby");
                }

                // Step 4: Cleanup relay
                await RelayHandler.SafeShutdownAsync();

                return OperationResult.Success("Lobby removed successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] RemoveLobby failed: {ex.Message}");
                return OperationResult.Failure($"Remove failed: {ex.Message}");
            }
        }

        #endregion

        #region Public API - Lobby Updates

        public async Task<bool> SetPlayerReadyAsync(bool isReady)
        {
            if (!IsInLobby) return false;
            
            var playerId = PlayerIdManager.PlayerId;
            if (string.IsNullOrEmpty(playerId)) return false;
            
            var updateOptions = LobbyUpdateDataFactory.CreatePlayerReadyUpdate(isReady);
            
            try
            {
                var result = await LobbyService.Instance.UpdatePlayerAsync(LobbyId, playerId, updateOptions);
                return result != null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyManager] SetPlayerReady failed: {e.Message}");
                return false;
            }
        }

        public async Task<bool> SetLobbyPhaseAsync(string phase)
        {
            if (!IsHost) return false;
            
            var updateOptions = LobbyUpdateDataFactory.CreatePhaseUpdate(phase);
            var result = await _handler.UpdateLobbyAsync(LobbyId, updateOptions);
            return result != null;
        }

        public async Task<bool> KickPlayerAsync(string playerId)
        {
            if (!IsHost) return false;
            
            // Kick from lobby service
            var kickSuccess = await _handler.KickPlayerAsync(playerId);
            if (!kickSuccess) return false;

            // Trigger network disconnection for that client
            // (Implement trong NetworkManager nếu cần force disconnect)
            
            return true;
        }

        public async Task<bool> UpdateLobbyPasswordAsync(string newPassword)
        {
            if (!IsHost) return false;
            
            var updateOptions = LobbyUpdateDataFactory.CreatePasswordUpdate(newPassword);
            var result = await _handler.UpdateLobbyAsync(LobbyId, updateOptions);
            return result != null;
        }

        public async Task<bool> UpdateLobbyNameAsync(string newName)
        {
            if (!IsHost) return false;
            
            var updateOptions = LobbyUpdateDataFactory.CreateNameUpdate(newName);
            var result = await _handler.UpdateLobbyAsync(LobbyId, updateOptions);
            return result != null;
        }

        public async Task<bool> UpdateMaxPlayersAsync(int maxPlayers)
        {
            if (!IsHost) return false;
            
            var updateOptions = LobbyUpdateDataFactory.CreateMaxPlayersUpdate(maxPlayers);
            var result = await _handler.UpdateLobbyAsync(LobbyId, updateOptions);
            return result != null;
        }

        public async Task<bool> SetRelayJoinCodeAsync(string joinCode)
        {
            if (!IsHost) return false;
            
            var updateOptions = LobbyUpdateDataFactory.CreateRelayJoinCodeUpdate(joinCode);
            var result = await _handler.UpdateLobbyAsync(LobbyId, updateOptions);
            return result != null;
        }

        #endregion

        #region Internal Helpers

        private bool ValidateOperation(string operationName)
        {
            if (!_isInitialized)
            {
                Debug.LogError($"[LobbyManager] {operationName} failed: Not initialized");
                return false;
            }

            if (_handler == null)
            {
                Debug.LogError($"[LobbyManager] {operationName} failed: Handler is null");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Chờ relay code xuất hiện trong lobby data (với timeout)
        /// </summary>
        private async Task<string> WaitForRelayCodeAsync(float timeoutSeconds)
        {
            var startTime = Time.realtimeSinceStartup;
            var pollInterval = 0.5f;

            while (Time.realtimeSinceStartup - startTime < timeoutSeconds)
            {
                var relayCode = CurrentLobby?.GetRelayJoinCode();
                if (!string.IsNullOrEmpty(relayCode))
                {
                    return relayCode;
                }

                await Task.Delay(TimeSpan.FromSeconds(pollInterval));
            }

            Debug.LogWarning($"[LobbyManager] Relay code not available after {timeoutSeconds}s");
            return null;
        }

        /// <summary>
        /// Rollback khi create lobby fail
        /// </summary>
        private async Task RollbackLobbyCreation(string lobbyId)
        {
            try
            {
                if (!string.IsNullOrEmpty(lobbyId))
                {
                    await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
                    Debug.Log($"[LobbyManager] Rolled back lobby creation: {lobbyId}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyManager] Rollback failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Rollback khi join lobby fail
        /// </summary>
        private async Task RollbackLobbyJoin(string lobbyId)
        {
            try
            {
                var playerId = PlayerIdManager.PlayerId;
                if (!string.IsNullOrEmpty(lobbyId) && !string.IsNullOrEmpty(playerId))
                {
                    await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
                    Debug.Log($"[LobbyManager] Rolled back lobby join");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyManager] Rollback join failed: {ex.Message}");
            }
        }

        private void Cleanup()
        {
            try
            {
                lobbyRuntime?.StopRuntime();
                _handler?.OnDestroy();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Final cleanup error: {ex.Message}");
            }
        }

        #endregion

        #region Debug

        [ContextMenu("Debug Lobby State")]
        private void DebugLobbyState()
        {
            Debug.Log($"[LobbyManager] State:" +
                     $"\n  Initialized: {_isInitialized}" +
                     $"\n  In Lobby: {IsInLobby}" +
                     $"\n  Is Host: {IsHost}" +
                     $"\n  Lobby Code: {LobbyCode}" +
                     $"\n  Relay Code: {RelayJoinCode}" +
                     $"\n  Runtime Running: {(lobbyRuntime?.IsRunning ?? false)}");
        }

        #endregion
    }
}
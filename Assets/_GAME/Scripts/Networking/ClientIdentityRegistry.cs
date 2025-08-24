using System.Collections.Generic;
using _GAME.Scripts.Networking.Lobbies;
using GAME.Scripts.DesignPattern;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking
{
    /// <summary>
    /// Registry to map between Unity Gaming Services Player IDs and Netcode Client IDs
    /// Essential for proper player management across Lobby and Network systems
    /// </summary>
    public class ClientIdentityRegistry : SingletonDontDestroy<ClientIdentityRegistry>
    {
        private readonly Dictionary<string, ulong> _ugsToNetcode = new();
        private readonly Dictionary<ulong, string> _netcodeToUgs = new();

        private bool _isRegistered = false;

        protected override void OnAwake()
        {
            base.OnAwake();
            RegisterEvents();
        }

        private void RegisterEvents()
        {
            if (_isRegistered) return;
            _isRegistered = true;

            // Network events
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }

            // Lobby events
            LobbyEvents.OnLobbyJoined += OnLobbyJoined;
            LobbyEvents.OnLeftLobby += OnLeftLobby;
            LobbyEvents.OnLobbyRemoved += OnLobbyRemoved;
        }

        private void UnregisterEvents()
        {
            if (!_isRegistered) return;
            _isRegistered = false;

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            LobbyEvents.OnLobbyJoined -= OnLobbyJoined;
            LobbyEvents.OnLeftLobby -= OnLeftLobby;
            LobbyEvents.OnLobbyRemoved -= OnLobbyRemoved;
        }

        #region Public API

        /// <summary>
        /// Register mapping between UGS player ID and Netcode client ID
        /// </summary>
        public void RegisterMapping(string ugsPlayerId, ulong netcodeClientId)
        {
            if (string.IsNullOrEmpty(ugsPlayerId))
            {
                Debug.LogWarning("[ClientIdentityRegistry] Cannot register null/empty UGS Player ID");
                return;
            }

            // Remove any existing mappings for these IDs
            UnregisterByUgs(ugsPlayerId);
            UnregisterByClient(netcodeClientId);

            // Add new mapping
            _ugsToNetcode[ugsPlayerId] = netcodeClientId;
            _netcodeToUgs[netcodeClientId] = ugsPlayerId;

            Debug.Log(
                $"[ClientIdentityRegistry] Registered mapping: UGS({ugsPlayerId}) <-> Netcode({netcodeClientId})");
        }

        /// <summary>
        /// Get Netcode client ID from UGS player ID
        /// </summary>
        public bool TryGetClientId(string ugsPlayerId, out ulong clientId)
        {
            clientId = 0;
            return !string.IsNullOrEmpty(ugsPlayerId) && _ugsToNetcode.TryGetValue(ugsPlayerId, out clientId);
        }

        /// <summary>
        /// Get UGS player ID from Netcode client ID
        /// </summary>
        public bool TryGetUgsId(ulong clientId, out string ugsPlayerId)
        {
            ugsPlayerId = null;
            return _netcodeToUgs.TryGetValue(clientId, out ugsPlayerId);
        }

        /// <summary>
        /// Check if UGS player ID has a registered mapping
        /// </summary>
        public bool HasUgsMapping(string ugsPlayerId)
        {
            return !string.IsNullOrEmpty(ugsPlayerId) && _ugsToNetcode.ContainsKey(ugsPlayerId);
        }

        /// <summary>
        /// Check if Netcode client ID has a registered mapping
        /// </summary>
        public bool HasClientMapping(ulong clientId)
        {
            return _netcodeToUgs.ContainsKey(clientId);
        }

        /// <summary>
        /// Remove mapping by UGS player ID
        /// </summary>
        public void UnregisterByUgs(string ugsPlayerId)
        {
            if (string.IsNullOrEmpty(ugsPlayerId)) return;

            if (_ugsToNetcode.TryGetValue(ugsPlayerId, out var clientId))
            {
                _ugsToNetcode.Remove(ugsPlayerId);
                _netcodeToUgs.Remove(clientId);
                Debug.Log($"[ClientIdentityRegistry] Unregistered UGS mapping: {ugsPlayerId}");
            }
        }

        /// <summary>
        /// Remove mapping by Netcode client ID
        /// </summary>
        public void UnregisterByClient(ulong clientId)
        {
            if (_netcodeToUgs.TryGetValue(clientId, out var ugsPlayerId))
            {
                _netcodeToUgs.Remove(clientId);
                _ugsToNetcode.Remove(ugsPlayerId);
                Debug.Log($"[ClientIdentityRegistry] Unregistered client mapping: {clientId}");
            }
        }

        /// <summary>
        /// Clear all mappings
        /// </summary>
        public void ClearAll()
        {
            int count = _ugsToNetcode.Count;
            _ugsToNetcode.Clear();
            _netcodeToUgs.Clear();

            if (count > 0)
            {
                Debug.Log($"[ClientIdentityRegistry] Cleared {count} mappings");
            }
        }

        /// <summary>
        /// Get all currently mapped UGS player IDs
        /// </summary>
        public IEnumerable<string> GetAllUgsIds()
        {
            return _ugsToNetcode.Keys;
        }

        /// <summary>
        /// Get all currently mapped Netcode client IDs
        /// </summary>
        public IEnumerable<ulong> GetAllClientIds()
        {
            return _netcodeToUgs.Keys;
        }

        #endregion

        #region Event Handlers

        private void OnClientConnected(ulong clientId)
        {
            if (!NetworkManager.Singleton.IsServer) return;

            Debug.Log($"[ClientIdentityRegistry] Client {clientId} connected to network");

            // Auto-register local client if possible
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                var myUgsId = AuthenticationService.Instance?.PlayerId;
                if (!string.IsNullOrEmpty(myUgsId))
                {
                    RegisterMapping(myUgsId, clientId);
                }
            }

            // For remote clients, we'll need to establish mapping through other means
            // (e.g., through lobby data correlation or custom network messages)
            TryAutoMapClient(clientId);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.Log($"[ClientIdentityRegistry] Client {clientId} disconnected from network");
            UnregisterByClient(clientId);
        }

        private void OnLobbyJoined(Lobby lobby, bool success, string message)
        {
            if (!success || lobby == null) return;

            Debug.Log("[ClientIdentityRegistry] Lobby joined, attempting to sync mappings");
            SyncWithLobbyPlayers(lobby);
        }

        private void OnLeftLobby(Lobby lobby, bool success, string message)
        {
            if (success)
            {
                Debug.Log("[ClientIdentityRegistry] Lobby left, clearing mappings");
                ClearAll();
            }
        }

        private void OnLobbyRemoved(Lobby lobby, bool success, string message)
        {
            if (success)
            {
                Debug.Log("[ClientIdentityRegistry] Lobby removed, clearing mappings");
                ClearAll();
            }
        }

        #endregion

        #region Auto-Mapping Logic

        /// <summary>
        /// Attempt to automatically map a client based on available information
        /// </summary>
        private void TryAutoMapClient(ulong clientId)
        {
            if (!NetworkManager.Singleton.IsServer) return;

            var lobby = LobbyExtensions.GetCurrentLobby();
            if (lobby?.Players == null) return;

            // Try to correlate with lobby players
            // This is a best-effort approach and may need game-specific logic
            foreach (var player in lobby.Players)
            {
                if (!HasUgsMapping(player.Id))
                {
                    // This is speculative mapping - ideally you'd have a proper handshake
                    Debug.Log(
                        $"[ClientIdentityRegistry] Attempting speculative mapping: UGS({player.Id}) -> Client({clientId})");
                    RegisterMapping(player.Id, clientId);
                    break;
                }
            }
        }

        /// <summary>
        /// Sync registry with current lobby players
        /// </summary>
        private void SyncWithLobbyPlayers(Lobby lobby)
        {
            if (lobby?.Players == null) return;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            // Remove mappings for players no longer in lobby
            var currentLobbyPlayerIds = new HashSet<string>();
            foreach (var player in lobby.Players)
            {
                currentLobbyPlayerIds.Add(player.Id);
            }

            var toRemove = new List<string>();
            foreach (var ugsId in _ugsToNetcode.Keys)
            {
                if (!currentLobbyPlayerIds.Contains(ugsId))
                {
                    toRemove.Add(ugsId);
                }
            }

            foreach (var ugsId in toRemove)
            {
                UnregisterByUgs(ugsId);
            }
        }

        #endregion

        #region Network Messages (Optional Enhancement)

        /// <summary>
        /// Send identity information to server (client -> server)
        /// This would be called from a NetworkBehaviour component
        /// </summary>
        public void SendIdentityToServer(string ugsPlayerId, ulong clientId)
        {
            if (!NetworkManager.Singleton.IsClient) return;

            // This would require a NetworkBehaviour to send RPCs
            Debug.Log(
                $"[ClientIdentityRegistry] Would send identity to server: UGS({ugsPlayerId}) from Client({clientId})");
        }

        /// <summary>
        /// Receive identity information from client (server receives)
        /// This would be called from a NetworkBehaviour RPC
        /// </summary>
        public void ReceiveIdentityFromClient(string ugsPlayerId, ulong clientId)
        {
            if (!NetworkManager.Singleton.IsServer) return;

            Debug.Log(
                $"[ClientIdentityRegistry] Received identity from client: UGS({ugsPlayerId}) from Client({clientId})");
            RegisterMapping(ugsPlayerId, clientId);
        }

        #endregion

        #region Debugging

        [ContextMenu("Debug Registry State")]
        public void DebugRegistryState()
        {
            Debug.Log($"[ClientIdentityRegistry] Registry State:" +
                      $"\n  Total Mappings: {_ugsToNetcode.Count}" +
                      $"\n  Registered: {_isRegistered}");

            foreach (var kvp in _ugsToNetcode)
            {
                Debug.Log($"  UGS({kvp.Key}) <-> Netcode({kvp.Value})");
            }
        }

        [ContextMenu("Sync with Current Lobby")]
        public void ForceSyncWithLobby()
        {
            var lobby = LobbyExtensions.GetCurrentLobby();
            if (lobby != null)
            {
                SyncWithLobbyPlayers(lobby);
                Debug.Log("[ClientIdentityRegistry] Force sync completed");
            }
            else
            {
                Debug.LogWarning("[ClientIdentityRegistry] No current lobby to sync with");
            }
        }

        #endregion

        protected override void OnDestroy()
        {
            UnregisterEvents();
            ClearAll();
            base.OnDestroy();
        }
    }
}
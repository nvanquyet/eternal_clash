using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;
using _GAME.Scripts.Networking.Relay;
using _GAME.Scripts.Networking.Lobbies;
using UnityEngine;

namespace _GAME.Scripts.Networking
{
    /// <summary>
    /// Trung tâm đồng bộ và cung cấp ID (PlayerId/LobbyId/LobbyCode/HostId/RelayJoinCode/LocalClientId).
    /// Không gọi SDK trực tiếp bên ngoài; mọi nơi khác chỉ đọc từ đây.
    /// </summary>
    public static class NetIdHub
    {
        // --- Sources ---
        public static string PlayerId => AuthenticationService.Instance?.PlayerId;

        public static ulong LocalClientId =>
            NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;

        // --- State synced from LobbyEvents ---
        public static string LobbyId { get; private set; }
        public static string LobbyCode { get; private set; }
        public static string HostId { get; private set; }
        public static string RelayJoinCode { get; private set; }

        private static bool _wired;
        private static Lobby _lastSyncedLobby;

        /// <summary>Gắn sự kiện 1 lần ở game start (vd: trong NetSessionManager.OnEnable)</summary>
        public static void Wire()
        {
            if (_wired) return;
            _wired = true;

            LobbyEvents.OnLobbyCreated += OnLobbyCreatedOrJoined;
            LobbyEvents.OnLobbyJoined += OnLobbyCreatedOrJoined;
            LobbyEvents.OnLobbyUpdated += OnLobbyUpdated;
            LobbyEvents.OnLeftLobby += OnLeftLobbyOrRemoved;
            LobbyEvents.OnLobbyRemoved += OnLeftLobbyOrRemoved;

            Debug.Log("[NetIdHub] Wired to lobby events");
        }

        /// <summary>Ngắt kết nối events (cleanup)</summary>
        public static void Unwire()
        {
            if (!_wired) return;
            
            LobbyEvents.OnLobbyCreated -= OnLobbyCreatedOrJoined;
            LobbyEvents.OnLobbyJoined -= OnLobbyCreatedOrJoined;
            LobbyEvents.OnLobbyUpdated -= OnLobbyUpdated;
            LobbyEvents.OnLeftLobby -= OnLeftLobbyOrRemoved;
            LobbyEvents.OnLobbyRemoved -= OnLeftLobbyOrRemoved;
            
            _wired = false;
            Debug.Log("[NetIdHub] Unwired from lobby events");
        }

        private static void OnLobbyCreatedOrJoined(Lobby lobby, bool success, string message)
        {
            if (success && lobby != null)
            {
                SyncFromLobby(lobby);
                Debug.Log($"[NetIdHub] Synced from lobby event: {message}");
            }
            else
            {
                Debug.LogWarning($"[NetIdHub] Failed lobby event ignored: {message}");
            }
        }

        private static void OnLobbyUpdated(Lobby lobby, string message)
        {
            if (lobby != null)
            {
                SyncFromLobby(lobby);
                // Don't log every update to avoid spam
                if (Application.isEditor && Debug.isDebugBuild)
                {
                    Debug.Log($"[NetIdHub] Updated from lobby: {message}");
                }
            }
        }

        private static void OnLeftLobbyOrRemoved(Lobby lobby, bool success, string message)
        {
            if (success)
            {
                Clear();
                Debug.Log($"[NetIdHub] Cleared due to: {message}");
            }
        }

        private static void SyncFromLobby(Lobby lobby)
        {
            if (lobby == null) return;

            // Avoid unnecessary syncing
            if (_lastSyncedLobby != null && 
                _lastSyncedLobby.Id == lobby.Id && 
                _lastSyncedLobby.Version == lobby.Version)
            {
                return;
            }

            string oldLobbyId = LobbyId;
            string oldRelayCode = RelayJoinCode;

            LobbyId = lobby.Id;
            LobbyCode = lobby.LobbyCode;
            HostId = lobby.HostId;
            
            // Get relay join code from lobby data
            string newRelayCode = lobby.GetRelayJoinCode();
            if (!string.IsNullOrEmpty(newRelayCode) && newRelayCode != RelayJoinCode)
            {
                RelayJoinCode = newRelayCode;
                Debug.Log($"[NetIdHub] Relay join code updated: {RelayJoinCode}");
            }

            _lastSyncedLobby = lobby;

            // Log significant changes
            if (oldLobbyId != LobbyId)
            {
                Debug.Log($"[NetIdHub] Lobby ID changed: {oldLobbyId} -> {LobbyId}");
            }
        }

        /// <summary>External binding for lobby data (used by LobbyHandler)</summary>
        public static void BindLobby(Lobby lobby)
        {
            if (lobby != null)
            {
                SyncFromLobby(lobby);
            }
        }

        /// <summary>Set relay join code manually (used by RelayConnector)</summary>
        public static void SetRelayJoinCode(string code)
        {
            if (RelayJoinCode != code)
            {
                RelayJoinCode = code;
                Debug.Log($"[NetIdHub] Relay join code set manually: {code}");
            }
        }

        /// <summary>Check if current player is the lobby host</summary>
        public static bool IsLocalHost()
        {
            string myId = PlayerId;
            return !string.IsNullOrEmpty(HostId) && !string.IsNullOrEmpty(myId) && HostId == myId;
        }

        /// <summary>Clear all stored IDs</summary>
        public static void Clear()
        {
            bool hadData = !string.IsNullOrEmpty(LobbyId) || !string.IsNullOrEmpty(RelayJoinCode);
            
            LobbyId = null;
            LobbyCode = null;
            HostId = null;
            RelayJoinCode = null;
            _lastSyncedLobby = null;

            if (hadData)
            {
                Debug.Log("[NetIdHub] All IDs cleared");
            }
        }

        /// <summary>Get current state summary for debugging</summary>
        public static string GetStateInfo()
        {
            return $"NetIdHub State:" +
                   $"\n  PlayerId: {PlayerId}" +
                   $"\n  LobbyId: {LobbyId}" +
                   $"\n  LobbyCode: {LobbyCode}" +
                   $"\n  HostId: {HostId}" +
                   $"\n  RelayJoinCode: {(string.IsNullOrEmpty(RelayJoinCode) ? "null" : RelayJoinCode)}" +
                   $"\n  IsLocalHost: {IsLocalHost()}" +
                   $"\n  LocalClientId: {LocalClientId}";
        }

        /// <summary>Validate that all necessary IDs are present for hosting</summary>
        public static bool IsValidHostState()
        {
            return !string.IsNullOrEmpty(PlayerId) && 
                   !string.IsNullOrEmpty(LobbyId) && 
                   !string.IsNullOrEmpty(HostId) && 
                   IsLocalHost();
        }

        /// <summary>Validate that all necessary IDs are present for joining</summary>
        public static bool IsValidClientState()
        {
            return !string.IsNullOrEmpty(PlayerId) && 
                   !string.IsNullOrEmpty(LobbyId) && 
                   !string.IsNullOrEmpty(RelayJoinCode) && 
                   !IsLocalHost();
        }

        /// <summary>Force refresh from current lobby (emergency sync)</summary>
        public static void ForceRefresh()
        {
            var currentLobby = LobbyExtensions.GetCurrentLobby();
            if (currentLobby != null)
            {
                _lastSyncedLobby = null; // Force sync
                SyncFromLobby(currentLobby);
                Debug.Log("[NetIdHub] Force refresh completed");
            }
            else
            {
                Debug.LogWarning("[NetIdHub] Force refresh failed - no current lobby");
            }
        }

        // Debug methods
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void LogCurrentState()
        {
            Debug.Log(GetStateInfo());
        }
    }
}
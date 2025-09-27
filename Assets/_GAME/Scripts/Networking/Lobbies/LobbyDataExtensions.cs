// LobbyDataExtensions.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _GAME.Scripts.Config;
using _GAME.Scripts.Lobbies;
using _GAME.Scripts.Networking;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking.Lobbies
{
    public static class LobbyDataExtensions
    {
        #region Lobby Data Extensions

        public static string GetDataValue(this Lobby lobby, string key, string defaultValue = "")
        {
            if (lobby?.Data != null &&
                lobby.Data.TryGetValue(key, out var dataObject) &&
                dataObject != null &&
                !string.IsNullOrEmpty(dataObject.Value))
            {
                return dataObject.Value;
            }
            return defaultValue;
        }

        public static bool HasDataKey(this Lobby lobby, string key)
        {
            return lobby?.Data?.ContainsKey(key) ?? false;
        }

        public static string GetRelayJoinCode(this Lobby lobby)
        {
            return lobby.GetDataValue(LobbyConstants.LobbyData.RELAY_JOIN_CODE);
        }

        private static string GetNetworkStatus(this Lobby lobby)
        {
            return lobby.GetDataValue(LobbyConstants.LobbyData.NETWORK_STATUS, LobbyConstants.NetworkStatus.NONE);
        }

        public static bool IsNetworkReady(this Lobby lobby)
        {
            return lobby.GetNetworkStatus() == LobbyConstants.NetworkStatus.READY;
        }

        public static bool HasValidRelayCode(this Lobby lobby)
        {
            return !string.IsNullOrWhiteSpace(lobby.GetRelayJoinCode());
        }

        public static string LobbyPassword(this Lobby lobby)
            => lobby.GetDataValue(LobbyConstants.LobbyData.PASSWORD, null);

        /// <summary>
        /// Phase helper CHUẨN: ưu tiên LobbyData.PHASE, fallback LobbyKeys.PHASE (legacy/indexed)
        /// </summary>
        public static string GetPhase(this Lobby lobby, string fallback = SessionPhase.WAITING)
        {
            if (lobby?.Data == null) return fallback;
            if (lobby.Data.TryGetValue(LobbyConstants.LobbyData.PHASE, out var p1) && !string.IsNullOrEmpty(p1.Value))
                return p1.Value;
            if (lobby.Data.TryGetValue(LobbyConstants.LobbyKeys.PHASE, out var p2) && !string.IsNullOrEmpty(p2.Value))
                return p2.Value;
            return fallback;
        }

        #endregion

        public static string NormalizeLobbyCode(string code)
            => (code ?? "").Trim().ToUpperInvariant();

        #region Lobby Update Operations

        public static async Task<Lobby> UpdateLobbyDataAsync(string lobbyId, Dictionary<string, DataObject> data)
        {
            if (string.IsNullOrEmpty(lobbyId) || data == null || data.Count == 0)
            {
                Debug.LogWarning("[LobbyDataExtensions] Invalid parameters for UpdateLobbyDataAsync");
                return null;
            }

            try
            {
                var handler = LobbyManager.Instance.LobbyHandler;
                if (handler == null)
                {
                    Debug.LogError("[LobbyDataExtensions] LobbyHandler not available");
                    return null;
                }

                var options = new UpdateLobbyOptions { Data = data };
                return await handler.UpdateLobbyAsync(lobbyId, options);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyDataExtensions] UpdateLobbyDataAsync failed: {e.Message}");
                return null;
            }
        }

        private static async Task<bool> UpdateLobbyDataValueAsync(string lobbyId, string key, string value,
            DataObject.VisibilityOptions visibility = DataObject.VisibilityOptions.Member)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("[LobbyDataExtensions] Data key cannot be empty");
                return false;
            }

            var data = new Dictionary<string, DataObject>
            {
                { key, new DataObject(visibility, value ?? string.Empty) }
            };

            var result = await UpdateLobbyDataAsync(lobbyId, data);
            return result != null;
        }

        private static async Task<bool> UpdateLobbyDataValueAsync(
            string lobbyId,
            string key,
            string value,
            DataObject.IndexOptions index,
            DataObject.VisibilityOptions visibility = DataObject.VisibilityOptions.Member)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("[LobbyDataExtensions] Data key cannot be empty");
                return false;
            }

            var data = new Dictionary<string, DataObject>
            {
                { key, new DataObject(visibility, value ?? string.Empty, index) }
            };

            var result = await UpdateLobbyDataAsync(lobbyId, data);
            return result != null;
        }
        
        public static async Task<bool> SetRelayJoinCodeAsync(string lobbyId, string joinCode)
        {
            if (string.IsNullOrEmpty(lobbyId))
            {
                Debug.LogWarning("[LobbyDataExtensions] Lobby ID cannot be empty");
                return false;
            }
            return await UpdateLobbyDataValueAsync(lobbyId, LobbyConstants.LobbyData.RELAY_JOIN_CODE, joinCode);
        }

        public static async Task<bool> SetNetworkStatusAsync(string lobbyId, string status)
        {
            return await UpdateLobbyDataValueAsync(lobbyId, LobbyConstants.LobbyData.NETWORK_STATUS, status);
        }

        /// <summary>
        /// Set phase CHUẨN: ghi vào LobbyData.PHASE, và mirror sang LobbyKeys.PHASE (index) để query cũ vẫn chạy.
        /// </summary>
        public static async Task<bool> SetLobbyPhaseAsync(string lobbyId, string phase)
        {
            var value = phase ?? SessionPhase.WAITING;

            var data = new Dictionary<string, DataObject>
            {
                // CHUẨN
                { LobbyConstants.LobbyData.PHASE, new DataObject(
                    DataObject.VisibilityOptions.Public, value, DataObject.IndexOptions.S2) },

                // MIRROR (legacy/indexed)
                { LobbyConstants.LobbyKeys.PHASE, new DataObject(
                    DataObject.VisibilityOptions.Public, value, DataObject.IndexOptions.S2) },
            };

            return await UpdateLobbyDataAsync(lobbyId, data) != null;
        }

        public static async Task<bool> SetLobbyPasswordAsync(string lobbyId, string password,
            bool alsoSetBuiltIn = true)
        {
            try
            {
                var handler = LobbyManager.Instance.LobbyHandler;
                if (handler == null)
                {
                    Debug.LogError("[LobbyDataExtensions] LobbyHandler not available");
                    return false;
                }

                var data = new Dictionary<string, DataObject>
                {
                    { LobbyConstants.LobbyData.PASSWORD, new DataObject(
                        DataObject.VisibilityOptions.Member, password ?? string.Empty) }
                };

                var options = new UpdateLobbyOptions { Data = data };

                if (alsoSetBuiltIn)
                {
                    options.Password = password ?? string.Empty;
                }

                var result = await handler.UpdateLobbyAsync(lobbyId, options);
                return result != null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyDataExtensions] SetLobbyPasswordAsync failed: {e.Message}");
                return false;
            }
        }

        #endregion

        #region Player Data Operations

        public static string GetPlayerDataValue(this Unity.Services.Lobbies.Models.Player player, string key,
            string defaultValue = "")
        {
            if (player?.Data != null &&
                player.Data.TryGetValue(key, out var dataObject) &&
                dataObject != null &&
                !string.IsNullOrEmpty(dataObject.Value))
            {
                return dataObject.Value;
            }

            return defaultValue;
        }

        public static async Task<bool> UpdatePlayerDataAsync(string lobbyId, string playerId,
            Dictionary<string, PlayerDataObject> data)
        {
            if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(playerId) || data == null || data.Count == 0)
            {
                Debug.LogWarning("[LobbyDataExtensions] Invalid parameters for UpdatePlayerDataAsync");
                return false;
            }

            try
            {
                var options = new UpdatePlayerOptions { Data = data };
                var result = await LobbyService.Instance.UpdatePlayerAsync(lobbyId, playerId, options);

                if (result != null)
                {
                    var updatedPlayer = result.Players.Find(p => p.Id == playerId);
                    if (updatedPlayer != null)
                    {
                        LobbyEvents.TriggerPlayerUpdated(updatedPlayer, result, "Player data updated");
                    }
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyDataExtensions] UpdatePlayerDataAsync failed: {e.Message}");
                return false;
            }
        }

        public static async Task<bool> UpdatePlayerDataValueAsync(string lobbyId, string playerId, string key,
            string value,
            PlayerDataObject.VisibilityOptions visibility = PlayerDataObject.VisibilityOptions.Public)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("[LobbyDataExtensions] Player data key cannot be empty");
                return false;
            }

            var data = new Dictionary<string, PlayerDataObject>
            {
                { key, new PlayerDataObject(visibility, value ?? string.Empty) }
            };

            return await UpdatePlayerDataAsync(lobbyId, playerId, data);
        }

        public static async Task<bool> SetPlayerReadyAsync(string lobbyId, bool isReady)
        {
            var playerId = PlayerIdManager.PlayerId;
            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogError("[LobbyDataExtensions] Player ID not available");
                return false;
            }

            var readyValue = isReady ? LobbyConstants.Defaults.READY_TRUE : LobbyConstants.Defaults.READY_FALSE;
            return await UpdatePlayerDataValueAsync(lobbyId, playerId, LobbyConstants.PlayerData.IS_READY, readyValue);
        }

        public static async Task<bool> SetPlayerNameAsync(string lobbyId, string displayName)
        {
            var playerId = PlayerIdManager.PlayerId;
            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogError("[LobbyDataExtensions] Player ID not available");
                return false;
            }

            return await UpdatePlayerDataValueAsync(lobbyId, playerId, LobbyConstants.PlayerData.DISPLAY_NAME, displayName);
        }

        #endregion

        #region Convenience Methods

        public static Unity.Services.Lobbies.Models.Player GetCurrentPlayer(this Lobby lobby)
        {
            var myId = AuthenticationService.Instance?.PlayerId;
            if (string.IsNullOrEmpty(myId) || lobby?.Players == null)
                return null;

            return lobby.Players.Find(p => p.Id == myId);
        }

        public static bool IsPlayerReady(this Unity.Services.Lobbies.Models.Player player)
        {
            if (player?.Data == null) return false;

            if (player.Data.TryGetValue(LobbyConstants.PlayerData.IS_READY, out var readyData))
            {
                return bool.TryParse(readyData.Value, out var isReady) && isReady;
            }

            if (player.Data.TryGetValue(LobbyConstants.PlayerData.LEGACY_READY, out var legacyData))
            {
                return legacyData.Value == LobbyConstants.Defaults.LEGACY_READY_TRUE;
            }

            return false;
        }

        public static string GetPlayerDisplayName(this Unity.Services.Lobbies.Models.Player player)
        {
            if (player?.Data == null) return LobbyConstants.Defaults.UNKNOWN_PLAYER;

            if (player.Data.TryGetValue(LobbyConstants.PlayerData.DISPLAY_NAME, out var nameData))
            {
                return nameData.Value ?? LobbyConstants.Defaults.UNKNOWN_PLAYER;
            }

            if (player.Data.TryGetValue(LobbyConstants.PlayerData.LEGACY_NAME, out var legacyData))
            {
                return legacyData.Value ?? LobbyConstants.Defaults.UNKNOWN_PLAYER;
            }

            return $"Player_{player.Id?[..Math.Min(6, player.Id.Length)] ?? "Unknown"}";
        }

        #endregion

        #region Validation

        public static bool ValidateLobbyOperation(string lobbyId, bool requireHost = false)
        {
            if (string.IsNullOrEmpty(lobbyId))
            {
                Debug.LogWarning("[LobbyDataExtensions] Lobby ID cannot be empty");
                return false;
            }

            if (requireHost && !NetworkController.Instance.IsHost)
            {
                Debug.LogWarning("[LobbyDataExtensions] Operation requires host privileges");
                return false;
            }

            return true;
        }

        public static bool ValidatePlayerOperation(string lobbyId, string playerId = null)
        {
            if (string.IsNullOrEmpty(lobbyId))
            {
                Debug.LogWarning("[LobbyDataExtensions] Lobby ID cannot be empty");
                return false;
            }

            playerId ??= AuthenticationService.Instance?.PlayerId;
            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogWarning("[LobbyDataExtensions] Player ID not available");
                return false;
            }

            return true;
        }

        #endregion
    }
}

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
    /// <summary>
    /// Extension methods for reading/writing Lobby.Data and Player.Data
    /// Provides a consistent interface for data operations
    /// </summary>
    public static class LobbyDataExtensions
    {
        #region Lobby Data Extensions

        /// <summary>
        /// Get data value from lobby with fallback
        /// </summary>
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

        /// <summary>
        /// Check if lobby has specific data key
        /// </summary>
        public static bool HasDataKey(this Lobby lobby, string key)
        {
            return lobby?.Data?.ContainsKey(key) ?? false;
        }

        /// <summary>
        /// Get relay join code from lobby data
        /// </summary>
        public static string GetRelayJoinCode(this Lobby lobby)
        {
            return lobby.GetDataValue(LobbyConstants.LobbyData.RELAY_JOIN_CODE);
        }

        /// <summary>
        /// Get network status from lobby data
        /// </summary>
        public static string GetNetworkStatus(this Lobby lobby)
        {
            return lobby.GetDataValue(LobbyConstants.LobbyData.NETWORK_STATUS, LobbyConstants.NetworkStatus.NONE);
        }

        /// <summary>
        /// Check if lobby network is ready
        /// </summary>
        public static bool IsNetworkReady(this Lobby lobby)
        {
            return lobby.GetNetworkStatus() == LobbyConstants.NetworkStatus.READY;
        }

        /// <summary>
        /// Check if lobby has valid relay code
        /// </summary>
        public static bool HasValidRelayCode(this Lobby lobby)
        {
            return !string.IsNullOrWhiteSpace(lobby.GetRelayJoinCode());
        }

        #endregion

        
        public static string NormalizeLobbyCode(string code)
            => (code ?? "").Trim().ToUpperInvariant();
        
        #region Lobby Update Operations

        /// <summary>
        /// Update multiple lobby data values at once
        /// </summary>
        public static async Task<Lobby> UpdateLobbyDataAsync(string lobbyId, Dictionary<string, DataObject> data)
        {
            if (string.IsNullOrEmpty(lobbyId) || data == null || data.Count == 0)
            {
                Debug.LogWarning("[LobbyDataExtensions] Invalid parameters for UpdateLobbyDataAsync");
                return null;
            }

            try
            {
                var handler = LobbyHandler.Instance;
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

        /// <summary>
        /// Update single lobby data value
        /// </summary>
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
        
        /// <summary>
        /// Set relay join code in lobby data
        /// </summary>
        public static async Task<bool> SetRelayJoinCodeAsync(string lobbyId, string joinCode)
        {
            if (string.IsNullOrEmpty(lobbyId))
            {
                Debug.LogWarning("[LobbyDataExtensions] Lobby ID cannot be empty");
                return false;
            }

            // Update NetIdHub first
            NetIdHub.SetRelayJoinCode(joinCode ?? "");

            // Then update lobby data
            return await UpdateLobbyDataValueAsync(lobbyId, LobbyConstants.LobbyData.RELAY_JOIN_CODE, joinCode);
        }

        /// <summary>
        /// Set network status in lobby data
        /// </summary>
        public static async Task<bool> SetNetworkStatusAsync(string lobbyId, string status)
        {
            return await UpdateLobbyDataValueAsync(lobbyId, LobbyConstants.LobbyData.NETWORK_STATUS, status);
        }

        /// <summary>
        /// Set lobby phase
        /// </summary>
        public static async Task<bool> SetLobbyPhaseAsync(string lobbyId, string phase)
        {
            return await UpdateLobbyDataValueAsync(lobbyId, LobbyConstants.LobbyData.PHASE,
                phase ?? SessionPhase.WAITING);
        }

        /// <summary>
        /// Set lobby password in data (visible to members)
        /// </summary>
        public static async Task<bool> SetLobbyPasswordAsync(string lobbyId, string password,
            bool alsoSetBuiltIn = true)
        {
            try
            {
                var handler = LobbyHandler.Instance;
                if (handler == null)
                {
                    Debug.LogError("[LobbyDataExtensions] LobbyHandler not available");
                    return false;
                }

                var data = new Dictionary<string, DataObject>
                {
                    {
                        LobbyConstants.LobbyData.PASSWORD,
                        new DataObject(DataObject.VisibilityOptions.Member, password ?? string.Empty)
                    }
                };

                var options = new UpdateLobbyOptions { Data = data };

                // Also set built-in password for join protection
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

        /// <summary>
        /// Get player data value with fallback
        /// </summary>
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

        /// <summary>
        /// Update player data
        /// </summary>
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
                    // Find and trigger event for updated player
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

        /// <summary>
        /// Update single player data value
        /// </summary>
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

        /// <summary>
        /// Set player ready status
        /// </summary>
        public static async Task<bool> SetPlayerReadyAsync(string lobbyId, bool isReady)
        {
            var playerId = AuthenticationService.Instance?.PlayerId;
            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogError("[LobbyDataExtensions] Player ID not available");
                return false;
            }

            var readyValue = isReady ? LobbyConstants.Defaults.READY_TRUE : LobbyConstants.Defaults.READY_FALSE;
            return await UpdatePlayerDataValueAsync(lobbyId, playerId, LobbyConstants.PlayerData.IS_READY, readyValue);
        }

        /// <summary>
        /// Set player display name
        /// </summary>
        public static async Task<bool> SetPlayerNameAsync(string lobbyId, string displayName)
        {
            var playerId = AuthenticationService.Instance?.PlayerId;
            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogError("[LobbyDataExtensions] Player ID not available");
                return false;
            }

            return await UpdatePlayerDataValueAsync(lobbyId, playerId, LobbyConstants.PlayerData.DISPLAY_NAME,
                displayName);
        }

        #endregion

        #region Convenience Methods

        /// <summary>
        /// Get current player from lobby
        /// </summary>
        public static Unity.Services.Lobbies.Models.Player GetCurrentPlayer(this Lobby lobby)
        {
            var myId = AuthenticationService.Instance?.PlayerId;
            if (string.IsNullOrEmpty(myId) || lobby?.Players == null)
                return null;

            return lobby.Players.Find(p => p.Id == myId);
        }

        /// <summary>
        /// Check if player is ready with fallback to legacy format
        /// </summary>
        public static bool IsPlayerReady(this Unity.Services.Lobbies.Models.Player player)
        {
            if (player?.Data == null) return false;

            // Check new format
            if (player.Data.TryGetValue(LobbyConstants.PlayerData.IS_READY, out var readyData))
            {
                return bool.TryParse(readyData.Value, out var isReady) && isReady;
            }

            // Check legacy format
            if (player.Data.TryGetValue(LobbyConstants.PlayerData.LEGACY_READY, out var legacyData))
            {
                return legacyData.Value == LobbyConstants.Defaults.LEGACY_READY_TRUE;
            }

            return false;
        }

        /// <summary>
        /// Get player display name with fallback to legacy format
        /// </summary>
        public static string GetPlayerDisplayName(this Unity.Services.Lobbies.Models.Player player)
        {
            if (player?.Data == null) return LobbyConstants.Defaults.UNKNOWN_PLAYER;

            // Check new format
            if (player.Data.TryGetValue(LobbyConstants.PlayerData.DISPLAY_NAME, out var nameData))
            {
                return nameData.Value ?? LobbyConstants.Defaults.UNKNOWN_PLAYER;
            }

            // Check legacy format
            if (player.Data.TryGetValue(LobbyConstants.PlayerData.LEGACY_NAME, out var legacyData))
            {
                return legacyData.Value ?? LobbyConstants.Defaults.UNKNOWN_PLAYER;
            }

            return $"Player_{player.Id?[..Math.Min(6, player.Id.Length)] ?? "Unknown"}";
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validate that a lobby operation can be performed
        /// </summary>
        public static bool ValidateLobbyOperation(string lobbyId, bool requireHost = false)
        {
            if (string.IsNullOrEmpty(lobbyId))
            {
                Debug.LogWarning("[LobbyDataExtensions] Lobby ID cannot be empty");
                return false;
            }

            if (requireHost && !NetIdHub.IsLocalHost())
            {
                Debug.LogWarning("[LobbyDataExtensions] Operation requires host privileges");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validate that a player operation can be performed
        /// </summary>
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
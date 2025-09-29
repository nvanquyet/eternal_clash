using System;
using System.Collections.Generic;
using _GAME.Scripts.Config;
using _GAME.Scripts.Lobbies;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking.Lobbies
{
    /// <summary>
    /// Utility class for lobby data operations - creates data structures without performing operations
    /// No circular dependencies - only returns data structures for Manager to use
    /// </summary>
    public static class LobbyUtils
    {
        #region Lobby Data Extensions (Read-only)

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

        public static bool HasValidRelayCode(this Lobby lobby)
        {
            return !string.IsNullOrWhiteSpace(lobby.GetRelayJoinCode());
        }

        public static string GetLobbyPassword(this Lobby lobby)
            => lobby.GetDataValue(LobbyConstants.LobbyData.PASSWORD, null);

        /// <summary>
        /// Phase helper: ưu tiên LobbyData.PHASE, fallback LobbyKeys.PHASE (legacy/indexed)
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

        #region Player Data Extensions (Read-only)

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

            return false;
        }

        public static string GetPlayerDisplayName(this Unity.Services.Lobbies.Models.Player player)
        {
            if (player?.Data == null) return LobbyConstants.Defaults.UNKNOWN_PLAYER;

            if (player.Data.TryGetValue(LobbyConstants.PlayerData.DISPLAY_NAME, out var nameData))
            {
                return nameData.Value ?? LobbyConstants.Defaults.UNKNOWN_PLAYER;
            }
            
            return $"Player_{player.Id?[..Math.Min(6, player.Id.Length)] ?? "Unknown"}";
        }

        #endregion

        #region Utility Methods

        public static string NormalizeLobbyCode(string code)
            => (code ?? "").Trim().ToUpperInvariant();

        #endregion
    }

    /// <summary>
    /// Factory class for creating lobby update data structures
    /// Returns data objects for Manager to use with handler operations
    /// </summary>
    public static class LobbyUpdateDataFactory
    {
        #region Lobby Data Factories

        public static UpdateLobbyOptions CreateRelayJoinCodeUpdate(string joinCode)
        {
            return new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { LobbyConstants.LobbyData.RELAY_JOIN_CODE, 
                      new DataObject(DataObject.VisibilityOptions.Member, joinCode ?? string.Empty) }
                }
            };
        }

        public static UpdateLobbyOptions CreatePhaseUpdate(string phase)
        {
            var value = phase ?? SessionPhase.WAITING;
            
            return new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    // Primary phase data
                    { LobbyConstants.LobbyData.PHASE, new DataObject(
                        DataObject.VisibilityOptions.Public, value, DataObject.IndexOptions.S2) },
                    
                    // Mirror for legacy compatibility
                    { LobbyConstants.LobbyKeys.PHASE, new DataObject(
                        DataObject.VisibilityOptions.Public, value, DataObject.IndexOptions.S2) }
                }
            };
        }

        public static UpdateLobbyOptions CreatePasswordUpdate(string password, bool alsoSetBuiltIn = true)
        {
            var options = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { LobbyConstants.LobbyData.PASSWORD, new DataObject(
                        DataObject.VisibilityOptions.Member, password ?? string.Empty) }
                }
            };

            if (alsoSetBuiltIn)
            {
                options.Password = password ?? string.Empty;
            }

            return options;
        }

        public static UpdateLobbyOptions CreateNameUpdate(string lobbyName)
        {
            return new UpdateLobbyOptions
            {
                Name = lobbyName ?? string.Empty
            };
        }

        public static UpdateLobbyOptions CreateMaxPlayersUpdate(int maxPlayers)
        {
            return new UpdateLobbyOptions
            {
                MaxPlayers = maxPlayers
            };
        }

        public static UpdateLobbyOptions CreateCustomDataUpdate(Dictionary<string, DataObject> customData)
        {
            return new UpdateLobbyOptions
            {
                Data = customData ?? new Dictionary<string, DataObject>()
            };
        }

        #endregion

        #region Player Data Factories

        public static UpdatePlayerOptions CreatePlayerReadyUpdate(bool isReady)
        {
            var readyValue = isReady ? LobbyConstants.Defaults.READY_TRUE : LobbyConstants.Defaults.READY_FALSE;
            
            return new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { LobbyConstants.PlayerData.IS_READY, 
                      new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, readyValue) }
                }
            };
        }

        public static UpdatePlayerOptions CreatePlayerNameUpdate(string displayName)
        {
            return new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { LobbyConstants.PlayerData.DISPLAY_NAME, 
                      new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, displayName ?? string.Empty) }
                }
            };
        }

        public static UpdatePlayerOptions CreatePlayerRoleUpdate(string role)
        {
            return new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { LobbyConstants.PlayerData.PLAYER_ROLE, 
                      new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, role ?? LobbyConstants.Defaults.DEFAULT_PLAYER_ROLE) }
                }
            };
        }

        public static UpdatePlayerOptions CreateCustomPlayerDataUpdate(Dictionary<string, PlayerDataObject> customData)
        {
            return new UpdatePlayerOptions
            {
                Data = customData ?? new Dictionary<string, PlayerDataObject>()
            };
        }

        #endregion

        #region Validation Helpers

        public static bool ValidateLobbyOperation(string lobbyId, bool requireHost = false)
        {
            if (string.IsNullOrEmpty(lobbyId))
            {
                Debug.LogWarning("[LobbyUpdateDataFactory] Lobby ID cannot be empty");
                return false;
            }

            if (requireHost && !GameNet.Instance.Network.IsHost)
            {
                Debug.LogWarning("[LobbyUpdateDataFactory] Operation requires host privileges");
                return false;
            }

            return true;
        }

        public static bool ValidatePlayerOperation(string lobbyId, string playerId = null)
        {
            if (string.IsNullOrEmpty(lobbyId))
            {
                Debug.LogWarning("[LobbyUpdateDataFactory] Lobby ID cannot be empty");
                return false;
            }

            playerId ??= AuthenticationService.Instance?.PlayerId;
            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogWarning("[LobbyUpdateDataFactory] Player ID not available");
                return false;
            }

            return true;
        }

        #endregion
    }
}
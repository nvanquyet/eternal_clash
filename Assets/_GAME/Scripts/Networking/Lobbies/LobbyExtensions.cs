using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using _GAME.Scripts.Lobbies;
using _GAME.Scripts.Networking;
using UnityEngine;

namespace _GAME.Scripts.Networking.Lobbies
{
    /// <summary>
    /// Simplified extension methods for common lobby operations
    /// Acts as a facade over LobbyHandler with additional convenience methods
    /// </summary>
    public static class LobbyExtensions
    {
        private static LobbyHandler Handler => LobbyHandler.Instance;
        private static Lobby CachedLobby => Handler?.CachedLobby;

        #region Initialization

        /// <summary>
        /// Initialize extensions - should be called once at startup
        /// </summary>
        public static void Initialize()
        {
            // Subscribe to events for logging/debugging
            LobbyEvents.OnLobbyCreated += OnLobbyEvent;
            LobbyEvents.OnLobbyJoined += OnLobbyEvent;
            LobbyEvents.OnLeftLobby += (lobby, success, message) => LogEvent("Lobby Left", success, message);
            LobbyEvents.OnLobbyRemoved += (lobby, success, message) => LogEvent("Lobby Removed", success, message);
        }

        private static void OnLobbyEvent(Lobby lobby, bool success, string message)
        {
            LogEvent($"Lobby Event - {message}", success, lobby?.Name ?? "Unknown");
        }

        private static void LogEvent(string eventName, bool success, string details)
        {
            if (success)
                Debug.Log($"[LobbyExtensions] {eventName}: {details}");
            else
                Debug.LogWarning($"[LobbyExtensions] {eventName} Failed: {details}");
        }

        #endregion

        #region Lobby Operations

        /// <summary>
        /// Create a new lobby
        /// </summary>
        public static async Task<bool> CreateLobbyAsync(string lobbyName, int maxPlayers = 4, string password = null)
        {
            if (Handler == null)
            {
                Debug.LogError("[LobbyExtensions] LobbyHandler not available");
                return false;
            }

            var options = new CreateLobbyOptions();
            
            if (!string.IsNullOrEmpty(password))
            {
                options.IsPrivate = true;
                options.Password = password;
                options.Data = new Dictionary<string, DataObject>
                {
                    { LobbyConstants.LobbyData.PASSWORD, new DataObject(DataObject.VisibilityOptions.Member, password) }
                };
            }

            var lobby = await Handler.CreateLobbyAsync(lobbyName, maxPlayers, options);
            return lobby != null;
        }

        /// <summary>
        /// Join lobby by code
        /// </summary>
        public static async Task<bool> JoinLobbyAsync(string lobbyCode, string password = null)
        {
            if (Handler == null)
            {
                Debug.LogError("[LobbyExtensions] LobbyHandler not available");
                return false;
            }

            return await Handler.JoinLobbyAsync(lobbyCode, password);
        }

        /// <summary>
        /// Leave current lobby
        /// </summary>
        public static async Task<bool> LeaveLobbyAsync()
        {
            if (Handler == null)
            {
                Debug.LogError("[LobbyExtensions] LobbyHandler not available");
                return false;
            }

            return await Handler.LeaveLobbyAsync();
        }

        /// <summary>
        /// Remove current lobby (host only)
        /// </summary>
        public static async Task<bool> RemoveLobbyAsync()
        {
            if (Handler == null)
            {
                Debug.LogError("[LobbyExtensions] LobbyHandler not available");
                return false;
            }

            if (!IsHost())
            {
                Debug.LogWarning("[LobbyExtensions] Only host can remove lobby");
                return false;
            }

            return await Handler.RemoveLobbyAsync();
        }

        #endregion

        #region Lobby Updates

        /// <summary>
        /// Update lobby name (host only)
        /// </summary>
        public static async Task<bool> UpdateLobbyNameAsync(string newName)
        {
            if (!ValidateHostOperation()) return false;
            if (!ValidateInput(newName, "Lobby name", LobbyConstants.Validation.MAX_LOBBY_NAME_LENGTH)) return false;

            var options = new UpdateLobbyOptions { Name = newName };
            var updated = await Handler.UpdateLobbyAsync(CachedLobby.Id, options);
            return updated != null;
        }

        /// <summary>
        /// Update lobby password (host only)
        /// </summary>
        public static async Task<bool> UpdateLobbyPasswordAsync(string newPassword)
        {
            if (!ValidateHostOperation()) return false;
            
            if (!string.IsNullOrEmpty(newPassword) && newPassword.Length < LobbyConstants.Validation.MIN_PASSWORD_LENGTH)
            {
                Debug.LogWarning($"[LobbyExtensions] Password must be at least {LobbyConstants.Validation.MIN_PASSWORD_LENGTH} characters");
                return false;
            }

            return await LobbyDataExtensions.SetLobbyPasswordAsync(CachedLobby.Id, newPassword);
        }

        /// <summary>
        /// Update max players (host only)
        /// </summary>
        public static async Task<bool> UpdateMaxPlayersAsync(int maxPlayers)
        {
            if (!ValidateHostOperation()) return false;
            
            if (maxPlayers < CachedLobby.Players.Count)
            {
                Debug.LogWarning($"[LobbyExtensions] Cannot reduce max players below current count ({CachedLobby.Players.Count})");
                return false;
            }

            var options = new UpdateLobbyOptions { MaxPlayers = maxPlayers };
            var updated = await Handler.UpdateLobbyAsync(CachedLobby.Id, options);
            return updated != null;
        }

        /// <summary>
        /// Set lobby phase (host only)
        /// </summary>
        public static async Task<bool> SetLobbyPhaseAsync(string phase)
        {
            if (!ValidateHostOperation()) return false;

            return await LobbyDataExtensions.SetLobbyPhaseAsync(CachedLobby.Id, phase);
        }

        #endregion

        #region Player Operations

        /// <summary>
        /// Set player ready status
        /// </summary>
        public static async Task<bool> SetPlayerReadyAsync(bool isReady)
        {
            if (!ValidatePlayerOperation()) return false;

            return await LobbyDataExtensions.SetPlayerReadyAsync(CachedLobby.Id, isReady);
        }

        /// <summary>
        /// Set player display name
        /// </summary>
        public static async Task<bool> SetPlayerNameAsync(string displayName)
        {
            if (!ValidatePlayerOperation()) return false;
            if (!ValidateInput(displayName, "Display name", LobbyConstants.Validation.MAX_DISPLAY_NAME_LENGTH)) return false;

            return await LobbyDataExtensions.SetPlayerNameAsync(CachedLobby.Id, displayName);
        }

        /// <summary>
        /// Kick player from lobby (host only)
        /// </summary>
        public static async Task<bool> KickPlayerAsync(string playerId)
        {
            if (!ValidateHostOperation()) return false;
            
            if (playerId == NetIdHub.PlayerId)
            {
                Debug.LogWarning("[LobbyExtensions] Cannot kick yourself");
                return false;
            }

            return await Handler.KickPlayerAsync(playerId);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Get current lobby info
        /// </summary>
        public static Lobby GetCurrentLobby() => CachedLobby;

        /// <summary>
        /// Get all players in current lobby
        /// </summary>
        public static List<Unity.Services.Lobbies.Models.Player> GetAllPlayers()
        {
            return CachedLobby?.Players?.ToList() ?? new List<Unity.Services.Lobbies.Models.Player>();
        }

        /// <summary>
        /// Check if current player is host
        /// </summary>
        public static bool IsHost() => NetIdHub.IsLocalHost();

        /// <summary>
        /// Check if player ID is current player
        /// </summary>
        public static bool IsMe(string playerId) => playerId == NetIdHub.PlayerId;

        /// <summary>
        /// Get lobby name
        /// </summary>
        public static string GetLobbyName() => CachedLobby?.Name ?? LobbyConstants.Defaults.DEFAULT_LOBBY_NAME;

        /// <summary>
        /// Get lobby max players
        /// </summary>
        public static int GetMaxPlayers() => CachedLobby?.MaxPlayers ?? LobbyConstants.Defaults.MAX_PLAYERS;

        /// <summary>
        /// Get lobby player count
        /// </summary>
        public static int GetPlayerCount() => CachedLobby?.Players?.Count ?? 0;

        /// <summary>
        /// Get lobby code
        /// </summary>
        public static string GetLobbyCode() => CachedLobby?.LobbyCode ?? "";

        /// <summary>
        /// Check if lobby has password
        /// </summary>
        public static bool HasPassword() => !string.IsNullOrEmpty(GetLobbyPassword());

        /// <summary>
        /// Get lobby password (visible to members only)
        /// </summary>
        public static string GetLobbyPassword() => CachedLobby?.GetDataValue(LobbyConstants.LobbyData.PASSWORD) ?? "";

        /// <summary>
        /// Get lobby phase
        /// </summary>
        public static string GetLobbyPhase() => CachedLobby?.GetDataValue(LobbyConstants.LobbyData.PHASE, LobbyConstants.Phases.WAITING) ?? LobbyConstants.Phases.WAITING;

        /// <summary>
        /// Check if lobby is in waiting phase
        /// </summary>
        public static bool IsWaitingPhase() => GetLobbyPhase() == LobbyConstants.Phases.WAITING;

        /// <summary>
        /// Check if lobby is in playing phase
        /// </summary>
        public static bool IsPlayingPhase() => GetLobbyPhase() == LobbyConstants.Phases.PLAYING;

        #endregion

        #region Player Utility Methods

        /// <summary>
        /// Check if player is ready
        /// </summary>
        public static bool IsPlayerReady(Unity.Services.Lobbies.Models.Player player)
        {
            if (player?.Data == null) return false;

            if (player.Data.TryGetValue(LobbyConstants.PlayerData.IS_READY, out var data))
            {
                return bool.TryParse(data.Value, out var isReady) && isReady;
            }

            return false;
        }

        /// <summary>
        /// Get player display name
        /// </summary>
        public static string GetPlayerName(Unity.Services.Lobbies.Models.Player player)
        {
            if (player?.Data == null) return LobbyConstants.Defaults.UNKNOWN_PLAYER;

            if (player.Data.TryGetValue(LobbyConstants.PlayerData.DISPLAY_NAME, out var data))
            {
                return data.Value ?? LobbyConstants.Defaults.UNKNOWN_PLAYER;
            }

            return $"Player_{player.Id?[..Math.Min(6, player.Id.Length)] ?? "Unknown"}";
        }

        /// <summary>
        /// Get current player from lobby
        /// </summary>
        public static Unity.Services.Lobbies.Models.Player GetCurrentPlayer()
        {
            var myId = NetIdHub.PlayerId;
            return GetAllPlayers().FirstOrDefault(p => p.Id == myId);
        }

        /// <summary>
        /// Check if current player is ready
        /// </summary>
        public static bool IsCurrentPlayerReady()
        {
            var currentPlayer = GetCurrentPlayer();
            return currentPlayer != null && IsPlayerReady(currentPlayer);
        }

        /// <summary>
        /// Check if all players are ready
        /// </summary>
        public static bool AreAllPlayersReady()
        {
            var players = GetAllPlayers();
            return players.Count > 0 && players.All(IsPlayerReady);
        }

        #endregion

        #region Validation

        private static bool ValidateHostOperation()
        {
            if (Handler == null)
            {
                Debug.LogError("[LobbyExtensions] LobbyHandler not available");
                return false;
            }

            if (CachedLobby == null)
            {
                Debug.LogWarning("[LobbyExtensions] No active lobby");
                return false;
            }

            if (!IsHost())
            {
                Debug.LogWarning("[LobbyExtensions] Only host can perform this operation");
                return false;
            }

            return true;
        }

        private static bool ValidatePlayerOperation()
        {
            if (Handler == null)
            {
                Debug.LogError("[LobbyExtensions] LobbyHandler not available");
                return false;
            }

            if (CachedLobby == null)
            {
                Debug.LogWarning("[LobbyExtensions] No active lobby");
                return false;
            }

            return true;
        }

        private static bool ValidateInput(string input, string fieldName, int maxLength = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                Debug.LogWarning($"[LobbyExtensions] {fieldName} cannot be empty");
                return false;
            }

            if (input.Length > maxLength)
            {
                Debug.LogWarning($"[LobbyExtensions] {fieldName} too long (max {maxLength} characters)");
                return false;
            }

            return true;
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Cleanup extensions
        /// </summary>
        public static void Cleanup()
        {
            LobbyEvents.OnLobbyCreated -= OnLobbyEvent;
            LobbyEvents.OnLobbyJoined -= OnLobbyEvent;
        }

        #endregion

        public static void ShutdownLobbyAsync()
        {
            Handler.StopHeartbeat();
            Handler.StopUpdater();
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using _GAME.Scripts.Lobbies;
using UnityEngine;

namespace _GAME.Scripts.Networking.Lobbies
{
    public static class LobbyExtensions
    {
        private static LobbyHandler LobbyHandler => LobbyHandler.Instance;
        private static string CurrentPlayerId => AuthenticationService.Instance.PlayerId;
        
        // Cached lobby data - updated from LobbyEvents
        private static Lobby _cachedLobby;
        
        // ========== INITIALIZATION ==========
        
        /// <summary>
        /// Initialize LobbyExtensions - call this once at game start
        /// </summary>
        public static void Initialize()
        {
            // Subscribe to lobby events to keep cached data updated
            LobbyEvents.OnLobbyCreated += OnLobbyEvent;
            LobbyEvents.OnLobbyJoined += OnLobbyEvent;
            LobbyEvents.OnLobbyUpdated += (lobby, message) => _cachedLobby = lobby;
            LobbyEvents.OnLobbyLeft += (lobby, success, message) => { if (success) _cachedLobby = null; };
            LobbyEvents.OnLobbyRemoved += (lobby, success, message) => { if (success) _cachedLobby = null; };
        }
        
        private static void OnLobbyEvent(Lobby lobby, bool success, string message)
        {
            if (lobby == null)
            {
                Debug.LogWarning("[LobbyEvent] Lobby event received with null lobby");
                return;
            }
            if (success) _cachedLobby = lobby;
            Debug.Log($"[LobbyEvent] {message} - Lobby ID: {lobby.Id}, Name: {lobby.Name}, Players: {lobby.Players.Count}");
        }
        
        // ========== LOBBY OPERATIONS ==========
        
        /// <summary>
        /// Update lobby name
        /// </summary>
        public static async Task OnLobbyNameChanged(string newName)
        {
            try
            {
                if (LobbyHandler == null)
                {
                    Debug.LogError("LobbyHandler is not available");
                    return;
                }

                if (_cachedLobby == null || !IsHost())
                {
                    Debug.LogWarning("Cannot update lobby name: Not host or lobby not found");
                    return;
                }

                // Validate lobby name length
                if (!string.IsNullOrEmpty(newName) && newName.Length > LobbyConstants.Validation.MAX_LOBBY_NAME_LENGTH)
                {
                    Debug.LogWarning($"Lobby name too long. Maximum length: {LobbyConstants.Validation.MAX_LOBBY_NAME_LENGTH}");
                    return;
                }

                await LobbyHandler.UpdateLobbyNameAsync(_cachedLobby.Id, newName);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to update lobby name: {e.Message}");
            }
        }

        /// <summary>
        /// Update lobby password
        /// </summary>
        public static async Task OnPasswordChanged(string newPassword)
        {
            try
            {
                if (LobbyHandler == null)
                {
                    Debug.LogError("LobbyHandler is not available");
                    return;
                }

                if (_cachedLobby == null || !IsHost())
                {
                    Debug.LogWarning("Cannot update password: Not host or lobby not found");
                    return;
                }

                // Validate password length
                if (!string.IsNullOrEmpty(newPassword) && newPassword.Length < LobbyConstants.Validation.MIN_PASSWORD_LENGTH)
                {
                    Debug.LogWarning($"Password must be at least {LobbyConstants.Validation.MIN_PASSWORD_LENGTH} characters long");
                    return;
                }

                await LobbyHandler.UpdateLobbyPasswordInDataAsync(_cachedLobby.Id, newPassword, true, true);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to update password: {e.Message}");
            }
        }

        /// <summary>
        /// Update max players in lobby
        /// </summary>
        public static async Task OnMaxPlayersChanged(int maxPlayers)
        {
            try
            {
                if (LobbyHandler == null)
                {
                    Debug.LogError("LobbyHandler is not available");
                    return;
                }

                if (_cachedLobby == null || !IsHost())
                {
                    Debug.LogWarning("Cannot update max players: Not host or lobby not found");
                    return;
                }

                // Validate that we're not reducing below current player count
                if (maxPlayers < _cachedLobby.Players.Count)
                {
                    Debug.LogWarning($"Cannot reduce max players to {maxPlayers} while lobby has {_cachedLobby.Players.Count} players");
                    return;
                }

                await LobbyHandler.UpdateLobbyMaxPlayersAsync(_cachedLobby.Id, maxPlayers);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to update max players: {e.Message}");
            }
        }

        /// <summary>
        /// Set lobby phase (Waiting/Playing/etc.)
        /// </summary>
        public static async Task SetPhaseAsync(string phase)
        {
            try
            {
                if (LobbyHandler == null)
                {
                    Debug.LogError("LobbyHandler is not available");
                    return;
                }

                if (_cachedLobby == null || !IsHost())
                {
                    Debug.LogWarning("Cannot set phase: Not host or lobby not found");
                    return;
                }

                var updateOptions = new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        { LobbyConstants.LobbyData.PHASE, new DataObject(DataObject.VisibilityOptions.Member, phase ?? LobbyConstants.Phases.WAITING) }
                    }
                };

                await LobbyHandler.UpdateLobbyAsync(_cachedLobby.Id, updateOptions);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to set phase: {e.Message}");
            }
        }

        // ========== PLAYER OPERATIONS ==========

        /// <summary>
        /// Set player ready status
        /// </summary>
        public static async Task SetPlayerReadyAsync(bool ready)
        {
            try
            {
                if (LobbyHandler == null)
                {
                    Debug.LogError("LobbyHandler is not available");
                    return;
                }

                if (_cachedLobby == null)
                {
                    Debug.LogWarning("Cannot set ready status: Lobby not found");
                    return;
                }

                await LobbyHandler.ToggleReadyAsync(_cachedLobby.Id, ready);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to set player ready status: {e.Message}");
            }
        }

        /// <summary>
        /// Set player name/display name
        /// </summary>
        public static async Task SetPlayerNameAsync(string playerName)
        {
            try
            {
                if (LobbyHandler == null)
                {
                    Debug.LogError("LobbyHandler is not available");
                    return;
                }

                if (_cachedLobby == null)
                {
                    Debug.LogWarning("Cannot set player name: Lobby not found");
                    return;
                }

                // Validate display name length
                if (!string.IsNullOrEmpty(playerName) && playerName.Length > LobbyConstants.Validation.MAX_DISPLAY_NAME_LENGTH)
                {
                    Debug.LogWarning($"Display name too long. Maximum length: {LobbyConstants.Validation.MAX_DISPLAY_NAME_LENGTH}");
                    return;
                }

                await LobbyHandler.SetDisplayNameAsync(_cachedLobby.Id, playerName);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to set player name: {e.Message}");
            }
        }

        /// <summary>
        /// Kick player from lobby (host only)
        /// </summary>
        public static async Task KickPlayerFromLobby(string playerId)
        {
            try
            {
                if (LobbyHandler == null)
                {
                    Debug.LogError("LobbyHandler is not available");
                    return;
                }

                if (_cachedLobby == null || !IsHost())
                {
                    Debug.LogWarning("Cannot kick player: Not host or lobby not found");
                    return;
                }

                // Don't allow kicking yourself
                if (playerId == CurrentPlayerId)
                {
                    Debug.LogWarning("Cannot kick yourself");
                    return;
                }

                await LobbyHandler.KickPlayerAsync(_cachedLobby.Id, playerId);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to kick player: {e.Message}");
            }
        }

        /// <summary>
        /// Leave current lobby
        /// </summary>
        public static async Task LeaveLobby()
        {
            try
            {
                if (LobbyHandler == null)
                {
                    Debug.LogError("LobbyHandler is not available");
                    return;
                }

                if (_cachedLobby == null)
                {
                    Debug.LogWarning("No lobby to leave");
                    return;
                }

                bool isHost = IsHost();
                await LobbyHandler.LeaveLobbyAsync(_cachedLobby.Id, CurrentPlayerId, isHost);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to leave lobby: {e.Message}");
            }
        }
        
        
        /// <summary>
        /// Remove/Delete current lobby (host only)
        /// </summary>
        public static async Task RemoveLobby()
        {
            try
            {
                if (LobbyHandler == null)
                {
                    Debug.LogError("LobbyHandler is not available");
                    return;
                }

                if (_cachedLobby == null)
                {
                    Debug.LogWarning("No lobby to remove");
                    return;
                }

                if (!IsHost())
                {
                    Debug.LogWarning("Cannot remove lobby: Only host can remove lobby");
                    return;
                }

                await LobbyHandler.RemoveLobbyAsync(_cachedLobby.Id);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to remove lobby: {e.Message}");
            }
        }

        // ========== UTILITY METHODS ==========

        /// <summary>
        /// Get all players in current lobby
        /// </summary>
        public static List<Unity.Services.Lobbies.Models.Player> GetAllPlayersInLobby()
        {
            if (_cachedLobby == null || _cachedLobby.Players == null)
            {
                Debug.LogWarning("No players found in current lobby");
                return new List<Unity.Services.Lobbies.Models.Player>();
            }
            return _cachedLobby?.Players?.ToList() ?? new List<Unity.Services.Lobbies.Models.Player>();
        }

        /// <summary>
        /// Check if current player is the host
        /// </summary>
        public static bool IsHost()
        {
            return _cachedLobby != null && _cachedLobby.HostId == CurrentPlayerId;
        }

        /// <summary>
        /// Check if player ID belongs to current user
        /// </summary>
        public static bool IsMe(string playerId)
        {
            return playerId == CurrentPlayerId;
        }

        /// <summary>
        /// Check if player is ready
        /// </summary>
        public static bool IsPlayerReady(Unity.Services.Lobbies.Models.Player player)
        {
            if (player?.Data == null) return false;
            
            // Check both new format and legacy format
            if (player.Data.TryGetValue(LobbyConstants.PlayerData.IS_READY, out var readyData))
            {
                return bool.TryParse(readyData.Value, out var isReady) && isReady;
            }
            
            if (player.Data.TryGetValue(LobbyConstants.PlayerData.LEGACY_READY, out var legacyReadyData))
            {
                return legacyReadyData.Value == LobbyConstants.Defaults.LEGACY_READY_TRUE;
            }
            
            return false;
        }

        /// <summary>
        /// Get player display name
        /// </summary>
        public static string GetPlayerName(Unity.Services.Lobbies.Models.Player player)
        {
            if (player?.Data == null) return LobbyConstants.Defaults.UNKNOWN_PLAYER;

            // Check both new format and legacy format
            if (player.Data.TryGetValue(LobbyConstants.PlayerData.DISPLAY_NAME, out var displayNameData))
            {
                return displayNameData.Value ?? LobbyConstants.Defaults.UNKNOWN_PLAYER;
            }
            
            if (player.Data.TryGetValue(LobbyConstants.PlayerData.LEGACY_NAME, out var nameData))
            {
                return nameData.Value ?? LobbyConstants.Defaults.UNKNOWN_PLAYER;
            }

            return $"Player_{player.Id?[..6] ?? "Unknown"}";
        }

        /// <summary>
        /// Get lobby phase
        /// </summary>
        public static string GetLobbyPhase()
        {
            if (_cachedLobby?.Data != null && _cachedLobby.Data.TryGetValue(LobbyConstants.LobbyData.PHASE, out var phaseData))
            {
                return phaseData.Value ?? LobbyConstants.Phases.WAITING;
            }
            return LobbyConstants.Phases.WAITING;
        }

        /// <summary>
        /// Get lobby name
        /// </summary>
        /// <returns></returns>
        public static string GetLobbyName()
        {
            if(_cachedLobby != null && !string.IsNullOrEmpty(_cachedLobby.Name))
            {
                return _cachedLobby.Name;
            }
            return LobbyConstants.Defaults.DEFAULT_LOBBY_NAME;
        }
        
        /// <summary>
        /// Get lobby max player
        /// </summary>
        /// <returns></returns>
        public static int GetLobbyMaxPlayer()
        {
            if (_cachedLobby != null && _cachedLobby.MaxPlayers > 0)
            {
                return _cachedLobby.MaxPlayers;
            }
            return LobbyConstants.Defaults.MAX_PLAYERS;
        }

        /// <summary>
        /// Get lobby password from data
        /// </summary>
        public static string GetLobbyPassword()
        {
            if (_cachedLobby?.Data != null && _cachedLobby.Data.TryGetValue(LobbyConstants.LobbyData.PASSWORD, out var passwordData))
            {
                return passwordData.Value ?? string.Empty;
            }
            return string.Empty;
        }

        /// <summary>
        /// Check if all players are ready
        /// </summary>
        public static bool AreAllPlayersReady()
        {
            if (_cachedLobby?.Players == null || _cachedLobby.Players.Count == 0)
                return false;

            return _cachedLobby.Players.All(IsPlayerReady);
        }

        /// <summary>
        /// Find player by ID
        /// </summary>
        public static Unity.Services.Lobbies.Models.Player FindPlayerById(string playerId)
        {
            return _cachedLobby?.Players?.FirstOrDefault(p => p.Id == playerId);
        }

        // LobbyExtensions.cs (private helpers)
        private static async Task<Lobby> GetCurrentLobbyAsync()
        {
            var id = LobbyHandler?.CurrentLobbyId;
            if (string.IsNullOrEmpty(id)) return null;
            // dùng SDK wrapper để lấy snapshot mới nhất
            return await LobbyHandler.GetLobbyInfoAsync(id);
        }

        private static Lobby GetCachedLobby()
        {
            return LobbyHandler?.CachedLobby;   // đọc nhanh cho các check đồng bộ
        }
        
        /// <summary>
        /// Get current lobby info
        /// </summary>
        public static Lobby GetCurrentLobby()
        {
            return _cachedLobby;
        }

        /// <summary>
        /// Get current lobby ID
        /// </summary>
        public static string GetCurrentLobbyId()
        {
            return _cachedLobby?.Id;
        }

        /// <summary>
        /// Get current lobby code
        /// </summary>
        public static string GetCurrentLobbyCode()
        {
            return _cachedLobby?.LobbyCode;
        }

        /// <summary>
        /// Check if currently in a lobby
        /// </summary>
        public static bool IsInLobby()
        {
            return _cachedLobby != null;
        }

        // ========== PHASE UTILITIES ==========

        /// <summary>
        /// Check if lobby is in waiting phase
        /// </summary>
        public static bool IsWaitingPhase()
        {
            return GetLobbyPhase() == LobbyConstants.Phases.WAITING;
        }

        /// <summary>
        /// Check if lobby is in playing phase
        /// </summary>
        public static bool IsPlayingPhase()
        {
            return GetLobbyPhase() == LobbyConstants.Phases.PLAYING;
        }

        /// <summary>
        /// Check if lobby is in finished phase
        /// </summary>
        public static bool IsFinishedPhase()
        {
            return GetLobbyPhase() == LobbyConstants.Phases.FINISHED;
        }

        /// <summary>
        /// Set lobby to waiting phase
        /// </summary>
        public static async Task SetWaitingPhaseAsync()
        {
            await SetPhaseAsync(LobbyConstants.Phases.WAITING);
        }

        /// <summary>
        /// Set lobby to playing phase
        /// </summary>
        public static async Task SetPlayingPhaseAsync()
        {
            await SetPhaseAsync(LobbyConstants.Phases.PLAYING);
        }

        /// <summary>
        /// Set lobby to finished phase
        /// </summary>
        public static async Task SetFinishedPhaseAsync()
        {
            await SetPhaseAsync(LobbyConstants.Phases.FINISHED);
        }

        // ========== VALIDATION UTILITIES ==========

        /// <summary>
        /// Validate lobby name
        /// </summary>
        public static bool IsValidLobbyName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && 
                   name.Length <= LobbyConstants.Validation.MAX_LOBBY_NAME_LENGTH;
        }

        /// <summary>
        /// Validate player display name
        /// </summary>
        public static bool IsValidDisplayName(string displayName)
        {
            return !string.IsNullOrWhiteSpace(displayName) && 
                   displayName.Length <= LobbyConstants.Validation.MAX_DISPLAY_NAME_LENGTH;
        }

        /// <summary>
        /// Validate password
        /// </summary>
        public static bool IsValidPassword(string password)
        {
            return string.IsNullOrEmpty(password) || 
                   password.Length >= LobbyConstants.Validation.MIN_PASSWORD_LENGTH;
        }

        // ========== CLEANUP ==========

        /// <summary>
        /// Clean up cached data and unsubscribe from events
        /// </summary>
        public static void Cleanup()
        {
            _cachedLobby = null;
            
            // Unsubscribe from events
            LobbyEvents.OnLobbyCreated -= OnLobbyEvent;
            LobbyEvents.OnLobbyJoined -= OnLobbyEvent;
            LobbyEvents.OnLobbyUpdated -= (lobby, message) => _cachedLobby = lobby;
            LobbyEvents.OnLobbyLeft -= (lobby, success, message) => { if (success) _cachedLobby = null; };
            LobbyEvents.OnLobbyRemoved -= (lobby, success, message) => { if (success) _cachedLobby = null; };
        }
    }
}
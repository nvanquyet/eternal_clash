using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _GAME.Scripts.Config;
using _GAME.Scripts.Data;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking.Lobbies
{
    /// <summary>
    /// Fixed LobbyHandler - Proper event flow và change detection
    /// </summary>
    public class LobbyHandler
    {
        #region Fields & Properties

        private LobbyRuntime _runtime;
        private bool _isInitialized = false;
        private bool _isSubscribedToEvents = false;
        
        public Lobby CachedLobby { get; private set; }
        private LobbySnapshot _lastSnapshot;

        #endregion

        #region Initialization

        public void InitializeComponents(LobbyRuntime runtime)
        {
            if (_isInitialized) return;

            try
            {
                _runtime = runtime;
                
                // Đảm bảo chỉ subscribe 1 lần
                if (!_isSubscribedToEvents)
                {
                    LobbyEvents.OnLobbyUpdated += OnLobbyUpdated;
                    _isSubscribedToEvents = true;
                }
                
                _isInitialized = true;
                Debug.Log("[LobbyHandler] Initialized successfully");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] Failed to initialize: {e.Message}");
            }
        }

        #endregion

        #region Core Lobby Operations

        public async Task<Lobby> CreateLobbyAsync(string lobbyName, int maxPlayers, CreateLobbyOptions options = null)
        {
            if (!ValidateService() || !ValidateInput(lobbyName, "Lobby name")) 
                return null;

            if (maxPlayers <= 0 || maxPlayers > LobbyConstants.Validation.MAX_PLAYERS)
            {
                Debug.LogError($"[LobbyHandler] Invalid max players: {maxPlayers}");
                return null;
            }

            try
            {
                options ??= new CreateLobbyOptions();
                options.Player ??= CreateDefaultPlayerData();

                var lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
                if (lobby == null)
                {
                    Debug.LogError("[LobbyHandler] Created lobby is null");
                    return null;
                }

                // Update cache immediately
                UpdateCachedLobby(lobby);
                
                // Update lobby with searchable metadata
                await UpdateLobbyMetadata(lobby);
                
                // KHÔNG start runtime ở đây nữa - để Manager làm
                // Chỉ trigger event
                LobbyEvents.TriggerLobbyCreated(lobby, true, $"Lobby '{lobby.Name}' created");
                
                return lobby;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyHandler] CreateLobby failed: {e.Reason} - {e.Message}");
                LobbyEvents.TriggerLobbyCreated(null, false, e.Message);
                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] CreateLobby failed: {e.Message}");
                LobbyEvents.TriggerLobbyCreated(null, false, e.Message);
                return null;
            }
        }

        public async Task<bool> PrecheckPhaseThenJoin(string code, string password = null)
        {
            if (!ValidateService() || !ValidateInput(code, "Lobby code")) 
                return false;

            try
            {
                var lobby = await FindLobbyByCode(code);
                if (lobby == null)
                {
                    TriggerJoinFailed("Lobby not found");
                    return false;
                }

                UpdateCachedLobby(lobby);

                if (!CanJoinLobby(lobby))
                {
                    var phase = GetLobbyPhase(lobby);
                    var reason = GetJoinFailureReason(phase);
                    TriggerJoinFailed(reason);
                    return false;
                }

                var joinSuccess = await JoinLobbyInternal(code, password);
                if (joinSuccess)
                {
                    var updatedLobby = await GetUpdatedLobby(CachedLobby.Id);
                    if (updatedLobby != null)
                    {
                        UpdateCachedLobby(updatedLobby);
                    }
                    
                    // KHÔNG start runtime ở đây nữa - để Manager làm
                    LobbyEvents.TriggerLobbyJoined(CachedLobby, true, $"Joined lobby '{CachedLobby.Name}'");
                }

                return joinSuccess;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyHandler] Join failed: {e.Reason} - {e.Message}");
                TriggerJoinFailed(e.Message);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] Join failed: {e.Message}");
                TriggerJoinFailed(e.Message);
                return false;
            }
        }

        public async Task<bool> LeaveLobbyAsync()
        {
            if (!ValidateService()) return false;

            if (CachedLobby == null)
            {
                Debug.LogWarning("[LobbyHandler] No lobby to leave");
                return false;
            }

            var lobbyId = CachedLobby.Id;
            var playerId = AuthenticationService.Instance.PlayerId;

            if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(playerId))
            {
                Debug.LogWarning("[LobbyHandler] No lobby or player to leave");
                return false;
            }

            try
            {
                bool isHost = IsCurrentPlayerHost();

                if (isHost)
                {
                    Debug.LogWarning("[LobbyHandler] Host cannot leave, must remove lobby");
                    return false;
                }
                else
                {
                    await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
                    ClearCachedLobby();
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] LeaveLobby failed: {e.Message}");
                LobbyEvents.TriggerLobbyNotFound();
                return false;
            }
        }

        public async Task<bool> RemoveLobbyAsync()
        {
            if (!ValidateService()) return false;

            if (CachedLobby == null)
            {
                Debug.LogWarning("[LobbyHandler] No lobby to remove");
                return false;
            }

            var lobbyId = CachedLobby.Id;
            if (string.IsNullOrEmpty(lobbyId))
            {
                Debug.LogWarning("[LobbyHandler] No lobby to remove");
                return false;
            }

            if (!IsCurrentPlayerHost())
            {
                Debug.LogWarning("[LobbyHandler] Only host can remove lobby");
                return false;
            }

            try
            {
                await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
                ClearCachedLobby();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] RemoveLobby failed: {e.Message}");
                LobbyEvents.TriggerLobbyNotFound();
                return false;
            }
        }

        public async Task<bool> KickPlayerAsync(string targetPlayerId)
        {
            if (!ValidateService()) return false;

            var lobby = CachedLobby;
            if (lobby == null || string.IsNullOrEmpty(lobby.Id))
            {
                Debug.LogWarning("[LobbyHandler] Kick failed: Not in a lobby");
                return false;
            }

            if (!IsCurrentPlayerHost())
            {
                Debug.LogWarning("[LobbyHandler] Kick failed: Only host can kick players");
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetPlayerId) || 
                targetPlayerId == AuthenticationService.Instance.PlayerId ||
                targetPlayerId == lobby.HostId)
            {
                Debug.LogWarning("[LobbyHandler] Kick failed: Invalid target player");
                return false;
            }

            try
            {
                await LobbyService.Instance.RemovePlayerAsync(lobby.Id, targetPlayerId);
                Debug.Log($"[LobbyHandler] Kicked player {targetPlayerId}");
                return true;
            }
            catch (LobbyServiceException ex)
            {
                Debug.LogError($"[LobbyHandler] Kick failed: {ex.Reason} - {ex.Message}");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] Kick failed: {e.Message}");
                return false;
            }
        }

        public async Task<Lobby> UpdateLobbyAsync(string lobbyId, UpdateLobbyOptions options)
        {
            if (!ValidateService() || !ValidateInput(lobbyId, "Lobby ID")) 
                return null;

            options ??= new UpdateLobbyOptions();

            try
            {
                var updated = await LobbyService.Instance.UpdateLobbyAsync(lobbyId, options);
                if (updated != null)
                {
                    UpdateCachedLobby(updated);
                }
                return updated;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] UpdateLobby failed: {e.Message}");
                return null;
            }
        }

        #endregion

        #region Event Handlers & Polling

        private void OnLobbyUpdated(Lobby lobby)
        {
            if (lobby == null || !ShouldUpdateCache(lobby)) return;
            UpdateCachedLobby(lobby);
        }

        private bool ShouldUpdateCache(Lobby lobby)
        {
            if (CachedLobby == null) return true;
            if (!string.Equals(CachedLobby.Id, lobby.Id, StringComparison.Ordinal)) return true;

            try
            {
                return lobby.LastUpdated != CachedLobby.LastUpdated;
            }
            catch
            {
                return (lobby.Players?.Count ?? 0) != (CachedLobby.Players?.Count ?? 0) ||
                       (lobby.Data?.Count ?? 0) != (CachedLobby.Data?.Count ?? 0);
            }
        }

        #endregion

        #region Snapshot System

        private class LobbySnapshot
        {
            public string LobbyId { get; set; }
            public int PlayerCount { get; set; }
            public List<string> PlayerIds { get; set; } = new();
            public Dictionary<string, Unity.Services.Lobbies.Models.Player> Players { get; set; } = new();
            public string Phase { get; set; }
            public string RelayJoinCode { get; set; }
            public DateTime LastUpdated { get; set; }
        }

        private LobbySnapshot CreateSnapshot(Lobby lobby)
        {
            if (lobby == null) return null;

            var snapshot = new LobbySnapshot
            {
                LobbyId = lobby.Id,
                PlayerCount = lobby.Players?.Count ?? 0,
                PlayerIds = lobby.Players?.Select(p => p.Id).ToList() ?? new List<string>(),
                Phase = GetLobbyPhase(lobby),
                RelayJoinCode = lobby.GetRelayJoinCode(),
                LastUpdated = lobby.LastUpdated
            };

            // Snapshot player data
            if (lobby.Players != null)
            {
                foreach (var player in lobby.Players)
                {
                    snapshot.Players[player.Id] = player;
                }
            }

            return snapshot;
        }

        #endregion

        #region Internal Helpers

        private async Task<Lobby> FindLobbyByCode(string code)
        {
            var normalized = code.Trim().ToUpperInvariant();
            var queryOptions = new QueryLobbiesOptions
            {
                Count = 1,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.S1, normalized, QueryFilter.OpOptions.EQ)
                }
            };

            var result = await LobbyService.Instance.QueryLobbiesAsync(queryOptions);
            return result.Results.FirstOrDefault();
        }

        private bool CanJoinLobby(Lobby lobby)
        {
            var phase = GetLobbyPhase(lobby);
            return phase == SessionPhase.WAITING;
        }

        private string GetJoinFailureReason(string phase)
        {
            return phase switch
            {
                SessionPhase.STARTING => "Game is starting, cannot join",
                SessionPhase.PLAYING => "Game is already in progress, cannot join",
                _ => "Lobby is not accepting new players"
            };
        }

        private async Task<bool> JoinLobbyInternal(string code, string password)
        {
            var normalized = code.Trim().ToUpperInvariant();
            var joinOptions = new JoinLobbyByCodeOptions
            {
                Player = CreateDefaultPlayerData(),
                Password = string.IsNullOrEmpty(password) ? null : password
            };

            var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(normalized, joinOptions);
            if (lobby != null)
            {
                UpdateCachedLobby(lobby);
                return true;
            }
            return false;
        }

        private async Task UpdateLobbyMetadata(Lobby lobby)
        {
            var code = lobby.LobbyCode?.Trim().ToUpperInvariant() ?? "";
            var updateOptions = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    [LobbyConstants.LobbyKeys.CODE] = new DataObject(
                        DataObject.VisibilityOptions.Public, code, DataObject.IndexOptions.S1),
                    [LobbyConstants.LobbyKeys.PHASE] = new DataObject(
                        DataObject.VisibilityOptions.Public, SessionPhase.WAITING, DataObject.IndexOptions.S2)
                }
            };

            try
            {
                var updated = await LobbyService.Instance.UpdateLobbyAsync(lobby.Id, updateOptions);
                if (updated != null)
                {
                    UpdateCachedLobby(updated);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbyHandler] Failed to update lobby metadata: {e.Message}");
            }
        }

        private async Task<Lobby> GetUpdatedLobby(string lobbyId)
        {
            try
            {
                return await LobbyService.Instance.GetLobbyAsync(lobbyId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbyHandler] Failed to get updated lobby: {e.Message}");
                return null;
            }
        }

        private bool IsCurrentPlayerHost()
        {
            if (CachedLobby == null) return false;
            return CachedLobby.HostId == AuthenticationService.Instance?.PlayerId;
        }

        private string GetLobbyPhase(Lobby lobby, string fallback = SessionPhase.WAITING)
        {
            if (lobby?.Data == null) return fallback;
            
            if (lobby.Data.TryGetValue(LobbyConstants.LobbyData.PHASE, out var p1) && !string.IsNullOrEmpty(p1.Value))
                return p1.Value;
            
            if (lobby.Data.TryGetValue(LobbyConstants.LobbyKeys.PHASE, out var p2) && !string.IsNullOrEmpty(p2.Value))
                return p2.Value;
                
            return fallback;
        }

        private Unity.Services.Lobbies.Models.Player CreateDefaultPlayerData()
        {
            var playerId = AuthenticationService.Instance?.PlayerId ?? "Unknown";
            var displayName = LocalData.UserName ?? $"Player_{playerId[..Math.Min(6, playerId.Length)]}";

            return new Unity.Services.Lobbies.Models.Player
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { LobbyConstants.PlayerData.DISPLAY_NAME, 
                      new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, displayName) },
                    { LobbyConstants.PlayerData.IS_READY, 
                      new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, LobbyConstants.Defaults.READY_FALSE) }
                }
            };
        }

        #endregion

        #region Event Triggers & Cache Management

        private void TriggerJoinFailed(string message)
        {
            Debug.LogWarning($"[LobbyHandler] Join failed: {message}");
            LobbyEvents.TriggerLobbyJoined(null, false, message);
        }

        /// <summary>
        /// FIXED: Proper change detection với snapshot comparison
        /// </summary>
        private void UpdateCachedLobby(Lobby lobby)
        {
            if (lobby == null) return;
            
            var newSnap = CreateSnapshot(lobby);
            var oldSnap = _lastSnapshot;
            
            // Detect changes nếu có snapshot cũ
            if (oldSnap != null)
            {
                // 1) Player join/leave detection
                var oldPlayerSet = new HashSet<string>(oldSnap.PlayerIds);
                var newPlayerSet = new HashSet<string>(newSnap.PlayerIds);

                // Player joined
                foreach (var newId in newSnap.PlayerIds)
                {
                    if (!oldPlayerSet.Contains(newId))
                    {
                        var player = lobby.Players?.Find(x => x.Id == newId);
                        if (player != null)
                        {
                            LobbyEvents.TriggerPlayerJoined(player, lobby, "Player joined");
                        }
                    }
                }

                // Player left
                foreach (var oldId in oldSnap.PlayerIds)
                {
                    if (!newPlayerSet.Contains(oldId))
                    {
                        // Tạo stub player từ snapshot cũ
                        var oldPlayerSnap = oldSnap.Players.GetValueOrDefault(oldId);
                        if (oldPlayerSnap != null)
                        {
                            LobbyEvents.TriggerPlayerLeft(oldSnap.Players[oldId], lobby, "Player left");
                        }
                    }
                }

                // 2) Player updated (ready/name/role change)
                foreach (var playerId in newPlayerSet.Intersect(oldPlayerSet))
                {
                    var oldPlayerSnap = oldSnap.Players.GetValueOrDefault(playerId);
                    var newPlayerSnap = newSnap.Players.GetValueOrDefault(playerId);

                    if (oldPlayerSnap != null && newPlayerSnap != null)
                    {
                        bool changed = oldPlayerSnap.GetPlayerDisplayName() != newPlayerSnap.GetPlayerDisplayName() ||
                                       oldPlayerSnap.IsPlayerReady() != newPlayerSnap.IsPlayerReady();

                        if (changed)
                        {
                            var player = lobby.Players?.Find(x => x.Id == playerId);
                            if (player != null)
                            {
                                LobbyEvents.TriggerPlayerUpdated(player, lobby, "Player updated");
                            }
                        }
                    }
                }

                // 3) Phase change
                if (oldSnap.Phase != newSnap.Phase)
                {
                    Debug.Log($"[LobbyHandler] Phase changed: {oldSnap.Phase} → {newSnap.Phase}");
                }

                // 4) Relay code change
                if (oldSnap.RelayJoinCode != newSnap.RelayJoinCode)
                {
                    Debug.Log($"[LobbyHandler] Relay code updated: {newSnap.RelayJoinCode}");
                }
            }

            // Save snapshot & cache
            _lastSnapshot = newSnap;
            CachedLobby = lobby;
            
            Debug.Log($"[LobbyHandler] Cache updated: " +
                     $"Players={lobby.Players?.Count ?? 0} | " +
                     $"Phase={lobby.GetPhase()} | " +
                     $"RelayCode={(string.IsNullOrEmpty(lobby.GetRelayJoinCode()) ? "N/A" : "Set")}");
        }

        private void ClearCachedLobby()
        {
            CachedLobby = null;
            _lastSnapshot = null;
            Debug.Log("[LobbyHandler] Cache cleared");
        }

        #endregion

        #region Validation

        private bool ValidateService()
        {
            try
            {
                if (LobbyService.Instance == null)
                {
                    Debug.LogError("[LobbyHandler] LobbyService not initialized");
                    return false;
                }

                if (AuthenticationService.Instance == null || !AuthenticationService.Instance.IsSignedIn)
                {
                    Debug.LogError("[LobbyHandler] User not authenticated");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] Service validation failed: {e.Message}");
                return false;
            }
        }

        private bool ValidateInput(string input, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                Debug.LogError($"[LobbyHandler] {fieldName} cannot be null or empty");
                return false;
            }
            return true;
        }

        #endregion

        #region Cleanup

        public void OnDestroy()
        {
            try 
            { 
                if (_isSubscribedToEvents)
                {
                    LobbyEvents.OnLobbyUpdated -= OnLobbyUpdated;
                    _isSubscribedToEvents = false;
                }
                
                _runtime?.StopRuntime(); 
            } 
            catch (Exception e)
            { 
                Debug.LogWarning($"[LobbyHandler] Cleanup error: {e.Message}");
            }
            
            ClearCachedLobby();
        }

        #endregion
    }
}
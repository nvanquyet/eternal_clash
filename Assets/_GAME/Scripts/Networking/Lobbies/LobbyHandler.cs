using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _GAME.Scripts.Config;
using _GAME.Scripts.Data;
using _GAME.Scripts.Lobbies;
using GAME.Scripts.DesignPattern;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking.Lobbies
{
    public class LobbyHandler
    {
        [Header("Lobby Settings")] [SerializeField]
        private float heartbeatInterval = 15f;

        [SerializeField] private float lobbyRefreshInterval = 2f;

        [SerializeField] private LobbyHeartbeat _heartbeat;
        [SerializeField] private LobbyUpdater _updater;
        private bool _isInitialized = false;

        public string CurrentLobbyId => CachedLobby?.Id;
        public Lobby CachedLobby { get; private set; }

        public void InitializeComponents(LobbyHeartbeat heartbeat, LobbyUpdater updater)
        {
            if (_isInitialized) return;

            try
            {
                _heartbeat = heartbeat;
                _updater = updater;

                _isInitialized = true;
                Debug.Log("[LobbyHandler] Components initialized successfully");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] Failed to initialize components: {e.Message}");
            }
        }

        #region Public API

        public async Task<Lobby> CreateLobbyAsync(string lobbyName, int maxPlayers, CreateLobbyOptions options = null)
        {
            if (!ValidateService()) return null;
            if (!ValidateInput(lobbyName, "Lobby name")) return null;
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

                // Mirror CODE + PHASE vào Data (Public + Indexed) để client có thể Query theo CODE
                var code = lobby.LobbyCode?.Trim().ToUpperInvariant() ?? "";

                var update = new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        // CODE index tại S1
                        [LobbyConstants.LobbyKeys.CODE] = new DataObject(
                            DataObject.VisibilityOptions.Public,
                            code,
                            DataObject.IndexOptions.S1),

                        // PHASE index tại S2
                        [LobbyConstants.LobbyKeys.PHASE] = new DataObject(
                            DataObject.VisibilityOptions.Public,
                            SessionPhase.WAITING,
                            DataObject.IndexOptions.S2)
                    }
                };

                lobby = await LobbyService.Instance.UpdateLobbyAsync(lobby.Id, update);
                HandleLobbyCreated(lobby);
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
            Debug.Log($"[LobbyHandler] === PrecheckPhaseThenJoin START ===");
            Debug.Log(
                $"[LobbyHandler] Input code: '{code}', Password: '{(string.IsNullOrEmpty(password) ? "null" : "***")}'");

            if (!ValidateService())
            {
                Debug.LogError("[LobbyHandler] Service validation failed");
                return false;
            }

            if (!ValidateInput(code, "Lobby code"))
            {
                Debug.LogError("[LobbyHandler] Input validation failed");
                return false;
            }

            try
            {
                var normalized = code.Trim().ToUpperInvariant();
                Debug.Log($"[LobbyHandler] Normalized code: '{normalized}'");
                Debug.Log($"[LobbyHandler] Query key: '{LobbyConstants.LobbyKeys.CODE}'");

                var q = new QueryLobbiesOptions
                {
                    Count = 1,
                    Filters = new List<QueryFilter>
                    {
                        new QueryFilter(
                            field: QueryFilter.FieldOptions.S1, // CODE được index ở S1
                            op: QueryFilter.OpOptions.EQ,
                            value: normalized)
                    }
                };

                Debug.Log($"[LobbyHandler] Sending QueryLobbiesAsync with filter S1 = '{normalized}'");
                var startTime = System.DateTime.Now;

                var res = await LobbyService.Instance.QueryLobbiesAsync(q);
                var queryTime = (System.DateTime.Now - startTime).TotalMilliseconds;

                Debug.Log(
                    $"[LobbyHandler] Query completed in {queryTime}ms, Results count: {res?.Results?.Count ?? 0}");

                if (res?.Results != null)
                {
                    for (int i = 0; i < res.Results.Count; i++)
                    {
                        var l = res.Results[i];
                        Debug.Log(
                            $"[LobbyHandler] Result [{i}]: ID='{l.Id}', Name='{l.Name}', Code='{l.LobbyCode}'");

                        if (l.Data != null)
                        {
                            Debug.Log($"[LobbyHandler] Result [{i}] Data keys: {string.Join(", ", l.Data.Keys)}");

                            foreach (var kvp in l.Data)
                            {
                                Debug.Log(
                                    $"[LobbyHandler] Result [{i}] Data['{kvp.Key}'] = '{kvp.Value?.Value}' (Index: {kvp.Value?.Index})");
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"[LobbyHandler] Result [{i}] has no Data");
                        }
                    }
                }

                var hit = res.Results.FirstOrDefault();
                if (hit == null)
                {
                    Debug.LogError("[LobbyHandler] No lobby found in query results");
                    Debug.Log("[LobbyHandler] === Attempting fallback: Query all lobbies to debug ===");

                    // Debug fallback: Query all lobbies to see what's available
                    try
                    {
                        var allQ = new QueryLobbiesOptions { Count = 25 };
                        var allRes = await LobbyService.Instance.QueryLobbiesAsync(allQ);
                        Debug.Log($"[LobbyHandler] All lobbies count: {allRes?.Results?.Count ?? 0}");

                        if (allRes?.Results != null)
                        {
                            foreach (var l in allRes.Results)
                            {
                                var lobbyCode = l.LobbyCode?.Trim()?.ToUpperInvariant() ?? "NULL";
                                var dataCode =
                                    l.Data?.TryGetValue(LobbyConstants.LobbyKeys.CODE, out var codeObj) == true
                                        ? codeObj.Value
                                        : "NOT_FOUND";

                                Debug.Log(
                                    $"[LobbyHandler] Available lobby: '{l.Name}', LobbyCode='{lobbyCode}', DataCode='{dataCode}', Match='{lobbyCode == normalized || dataCode == normalized}'");
                            }
                        }
                    }
                    catch (Exception debugEx)
                    {
                        Debug.LogError($"[LobbyHandler] Debug query failed: {debugEx.Message}");
                    }

                    Fail("Lobby not found");
                    return false;
                }

                Debug.Log($"[LobbyHandler] Found lobby: ID='{hit.Id}', Name='{hit.Name}', Code='{hit.LobbyCode}'");

                var phase = hit.Data != null && hit.Data.TryGetValue(LobbyConstants.LobbyKeys.PHASE, out var phaseObj)
                    ? phaseObj.Value
                    : SessionPhase.WAITING;

                Debug.Log($"[LobbyHandler] Lobby phase: '{phase}' (Expected: '{SessionPhase.WAITING}')");

                if (phase != SessionPhase.WAITING)
                {
                    var reason = phase switch
                    {
                        SessionPhase.STARTING => "Game is starting, cannot join",
                        SessionPhase.PLAYING => "Game is already in progress, cannot join",
                        _ => "Lobby is not accepting new players"
                    };

                    Debug.LogWarning($"[LobbyHandler] Phase check failed: {reason}");
                    Fail(reason);
                    return false;
                }

                Debug.Log("[LobbyHandler] Phase check passed, proceeding to join");

                // Ok → join
                var joinOpts = new JoinLobbyByCodeOptions
                {
                    Player = CreateDefaultPlayerData(),
                    Password = string.IsNullOrEmpty(password) ? null : password
                };

                Debug.Log($"[LobbyHandler] Calling JoinLobbyByCodeAsync with code: '{normalized}'");
                Debug.Log(
                    $"[LobbyHandler] Player data: DisplayName='{joinOpts.Player?.Data?["DisplayName"]?.Value}', IsReady='{joinOpts.Player?.Data?["IsReady"]?.Value}'");

                var joinStartTime = System.DateTime.Now;
                var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(normalized, joinOpts);
                var joinTime = (System.DateTime.Now - joinStartTime).TotalMilliseconds;

                Debug.Log($"[LobbyHandler] JoinLobbyByCodeAsync completed in {joinTime}ms");

                if (lobby == null)
                {
                    Debug.LogError("[LobbyHandler] JoinLobbyByCodeAsync returned null");
                    Fail("Failed to join lobby");
                    return false;
                }

                Debug.Log(
                    $"[LobbyHandler] Join successful: ID='{lobby.Id}', Name='{lobby.Name}', Players={lobby.Players?.Count ?? 0}");
                Debug.Log($"[LobbyHandler] My player ID: '{AuthenticationService.Instance.PlayerId}'");

                if (lobby.Players != null)
                {
                    foreach (var player in lobby.Players)
                    {
                        Debug.Log(
                            $"[LobbyHandler] Player in lobby: ID='{player.Id}', Name='{player.Data?["DisplayName"]?.Value}'");
                    }
                }

                Debug.Log("[LobbyHandler] Calling HandleLobbyJoined");
                HandleLobbyJoined(lobby);

                Debug.Log("[LobbyHandler] === PrecheckPhaseThenJoin SUCCESS ===");
                return true;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyHandler] LobbyServiceException - Reason: {e.Reason}, Message: {e.Message}");
                Debug.LogError($"[LobbyHandler] Exception details: {e}");
                Fail(e.Message);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] Unexpected exception: {e.Message}");
                Debug.LogError($"[LobbyHandler] Stack trace: {e.StackTrace}");
                Fail(e.Message);
                return false;
            }

            void Fail(string msg)
            {
                Debug.LogWarning($"[LobbyHandler] FAIL: {msg}");
                Debug.Log("[LobbyHandler] === PrecheckPhaseThenJoin FAILED ===");
                LobbyEvents.TriggerLobbyJoined(null, false, msg);
            }
        }

        public async Task<bool> JoinLobbyAsync(string lobbyCode, string password = null)
        {
            if (!ValidateService()) return false;
            if (!ValidateInput(lobbyCode, "Lobby code")) return false;
            return await PrecheckPhaseThenJoin(lobbyCode, password);
        }

        public async Task<bool> LeaveLobbyAsync()
        {
            if (!ValidateService()) return false;

            var lobbyId = CurrentLobbyId;
            var playerId = AuthenticationService.Instance.PlayerId;

            if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(playerId))
            {
                Debug.LogWarning("[LobbyHandler] No lobby or player to leave");
                return false;
            }

            try
            {
                bool isHost = NetworkController.Instance.IsHost;

                if (isHost)
                {
                    return await RemoveLobbyAsync();
                }
                else
                {
                    await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
                    HandleLobbyLeft();
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] LeaveLobby failed: {e.Message}");
                LobbyEvents.TriggerLobbyLeft(null, false, e.Message);
                return false;
            }
        }

        public async Task<bool> RemoveLobbyAsync()
        {
            if (!ValidateService()) return false;

            var lobbyId = CurrentLobbyId;
            if (string.IsNullOrEmpty(lobbyId))
            {
                Debug.LogWarning("[LobbyHandler] No lobby to remove");
                return false;
            }

            if (!NetworkController.Instance.IsHost)
            {
                Debug.LogWarning("[LobbyHandler] Only host can remove lobby");
                return false;
            }

            try
            {
                await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
                HandleLobbyRemoved();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] RemoveLobby failed: {e.Message}");
                LobbyEvents.TriggerLobbyRemoved(null, false, e.Message);
                return false;
            }
        }

        public async Task<bool> KickPlayerAsync(string playerId)
        {
            if (!ValidateService()) return false;
            if (!ValidateInput(playerId, "Player ID")) return false;

            var lobbyId = CurrentLobbyId;
            if (string.IsNullOrEmpty(lobbyId))
            {
                Debug.LogWarning("[LobbyHandler] No lobby to kick from");
                return false;
            }

            if (!NetworkController.Instance.IsHost)
            {
                Debug.LogWarning("[LobbyHandler] Only host can kick players");
                return false;
            }

            try
            {
                var player = CachedLobby?.Players?.Find(p => p.Id == playerId);
                await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
                LobbyEvents.TriggerPlayerKicked(player, CachedLobby, $"Kicked player {playerId}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] KickPlayer failed: {e.Message}");
                LobbyEvents.TriggerPlayerKicked(null, null, e.Message);
                return false;
            }
        }

        public async Task<Lobby> GetLobbyInfoAsync(string lobbyId)
        {
            if (!ValidateService()) return null;
            if (!ValidateInput(lobbyId, "Lobby ID")) return null;

            try
            {
                return await LobbyService.Instance.GetLobbyAsync(lobbyId);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] GetLobbyInfo failed: {e.Message}");
                return null;
            }
        }

        public async Task<Lobby> UpdateLobbyAsync(string lobbyId, UpdateLobbyOptions options)
        {
            if (!ValidateService()) return null;
            if (!ValidateInput(lobbyId, "Lobby ID")) return null;

            options ??= new UpdateLobbyOptions();

            try
            {
                var updated = await LobbyService.Instance.UpdateLobbyAsync(lobbyId, options);
                if (updated != null)
                {
                    UpdateCachedLobby(updated);
                    LobbyEvents.TriggerLobbyUpdated(updated, "Lobby updated");
                }

                return updated;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] UpdateLobby failed: {e.Message}");
                LobbyEvents.TriggerLobbyUpdated(null, e.Message);
                return null;
            }
        }

        /// <summary>
        /// Called by LobbyUpdater when lobby data changes
        /// </summary>
        public void OnLobbyUpdated(Lobby lobby)
        {
            if (lobby == null) return; // không update gì khi null

            bool shouldUpdate = false;

            if (CachedLobby == null)
                shouldUpdate = true;
            else if (!string.Equals(CachedLobby.Id, lobby.Id, StringComparison.Ordinal))
                shouldUpdate = true;
            else
            {
                // Ưu tiên so sánh LastUpdated nếu có:
                // (tùy type: DateTime/DateTimeOffset/string ISO UTC)
                try
                {
                    shouldUpdate = lobby.LastUpdated != CachedLobby.LastUpdated;
                }
                catch
                {
                    // Fallback: một vài tín hiệu khác để chắc chắn không bỏ lỡ
                    shouldUpdate =
                        (lobby.Players?.Count ?? 0) != (CachedLobby.Players?.Count ?? 0) ||
                        (lobby.Data?.Count ?? 0) != (CachedLobby.Data?.Count ?? 0);
                }
            }

            Debug.Log($"[LobbyHandler] RaiseLobbyUpdated: shouldUpdate={shouldUpdate}");
            if (!shouldUpdate) return;

            UpdateCachedLobby(lobby);
            LobbyEvents.TriggerLobbyUpdated(lobby, "poll updated");
        }

        #endregion

        #region Event Handlers

        private void HandleLobbyCreated(Lobby lobby)
        {
            UpdateCachedLobby(lobby);
            if (NetworkController.Instance.IsHost)
            {
                _heartbeat?.StartHeartbeat(lobby.Id);
            }

            _updater?.StartUpdating(lobby.Id);

            LobbyEvents.TriggerLobbyCreated(lobby, true, $"Lobby '{lobby.Name}' created");
        }

        private void HandleLobbyJoined(Lobby lobby)
        {
            UpdateCachedLobby(lobby);

            if (NetworkController.Instance.IsHost)
            {
                _heartbeat?.StartHeartbeat(lobby.Id);
            }
            else
            {
                _heartbeat?.StopHeartbeat();
            }

            _updater?.StartUpdating(lobby.Id);
            LobbyEvents.TriggerLobbyJoined(lobby, true, $"Joined lobby '{lobby.Name}'");
        }

        private void HandleLobbyLeft()
        {
            _heartbeat?.StopHeartbeat();
            _updater?.StopUpdating();
            ClearCachedLobby();
            LobbyEvents.TriggerLobbyLeft(null, true, "Left lobby");
        }

        private void HandleLobbyRemoved()
        {
            _heartbeat?.StopHeartbeat();
            _updater?.StopUpdating();
            ClearCachedLobby();
            LobbyEvents.TriggerLobbyRemoved(null, true, "Lobby removed");
        }

        #endregion

        #region Internal Methods

        private void UpdateCachedLobby(Lobby lobby, string message = "Lobby updated")
        {
            if (lobby != null)
            {
                CachedLobby = lobby;
            }

            LobbyEvents.TriggerLobbyUpdated(lobby, message);
        }

        private void ClearCachedLobby()
        {
            CachedLobby = null;
        }

        private Unity.Services.Lobbies.Models.Player CreateDefaultPlayerData()
        {
            var playerId = AuthenticationService.Instance?.PlayerId ?? "Unknown";
            var displayName = LocalData.UserName ?? $"Player_{playerId[..Math.Min(6, playerId.Length)]}";

            return new Unity.Services.Lobbies.Models.Player
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    {
                        LobbyConstants.PlayerData.DISPLAY_NAME,
                        new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, displayName)
                    },
                    {
                        LobbyConstants.PlayerData.IS_READY,
                        new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public,
                            LobbyConstants.Defaults.READY_FALSE)
                    }
                }
            };
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
            _heartbeat?.StopHeartbeat();
            _updater?.StopUpdating();
            ClearCachedLobby();
        }

        #endregion

        #region Debug

        [ContextMenu("Debug Lobby State")]
        private void DebugLobbyState()
        {
            Debug.Log($"[LobbyHandler] State:" +
                      $"\n  Current Lobby ID: {CurrentLobbyId}" +
                      $"\n  Cached Lobby: {(CachedLobby != null ? CachedLobby.Name : "null")}" +
                      $"\n  Is Host: {NetworkController.Instance.IsHost}" +
                      $"\n  Heartbeat Running: {(_heartbeat?.IsActive ?? false)}" +
                      $"\n  Updater Running: {(_updater?.IsRunning ?? false)}");
        }

        #endregion
    }
}
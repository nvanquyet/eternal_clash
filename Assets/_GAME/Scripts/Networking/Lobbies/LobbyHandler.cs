// LobbyHandler.cs — cập nhật sang dùng LobbyRuntime + bổ sung KickPlayerAsync
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _GAME.Scripts.Config;
using _GAME.Scripts.Data;
using _GAME.Scripts.Lobbies;
using _GAME.Scripts.Networking;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking.Lobbies
{
    public class LobbyHandler
    {
        [Header("Lobby Settings")]
        [SerializeField] private float heartbeatInterval = 15f;
        [SerializeField] private float lobbyRefreshInterval = 2f;

        [SerializeField] private LobbyRuntime _runtime;

        private bool _isInitialized = false;

        public string CurrentLobbyId => CachedLobby?.Id;
        public Lobby CachedLobby { get; private set; }

        public void InitializeComponents(LobbyRuntime runtime)
        {
            if (_isInitialized) return;

            try
            {
                _runtime = runtime;
                _isInitialized = true;
                Debug.Log("[LobbyHandler] Runtime initialized successfully");
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
                        [LobbyConstants.LobbyKeys.CODE] = new DataObject(
                            DataObject.VisibilityOptions.Public,
                            code,
                            DataObject.IndexOptions.S1),

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
            Debug.Log($"[LobbyHandler] Input code: '{code}', Password: '{(string.IsNullOrEmpty(password) ? "null" : "***")}'");

            if (!ValidateService()) return false;
            if (!ValidateInput(code, "Lobby code")) return false;

            try
            {
                var normalized = code.Trim().ToUpperInvariant();
                var q = new QueryLobbiesOptions
                {
                    Count = 1,
                    Filters = new List<QueryFilter>
                    {
                        // Sửa thứ tự tham số: field, op, value
                        new QueryFilter(QueryFilter.FieldOptions.S1, normalized, QueryFilter.OpOptions.EQ)
                    }
                };

                var res = await LobbyService.Instance.QueryLobbiesAsync(q);
                var hit = res.Results.FirstOrDefault();
                if (hit == null)
                {
                    Fail("Lobby not found");
                    return false;
                }

                var phase = hit.Data != null && hit.Data.TryGetValue(LobbyConstants.LobbyKeys.PHASE, out var phaseObj)
                    ? phaseObj.Value
                    : SessionPhase.WAITING;

                if (phase != SessionPhase.WAITING)
                {
                    var reason = phase switch
                    {
                        SessionPhase.STARTING => "Game is starting, cannot join",
                        SessionPhase.PLAYING => "Game is already in progress, cannot join",
                        _ => "Lobby is not accepting new players"
                    };
                    Fail(reason);
                    return false;
                }

                var joinOpts = new JoinLobbyByCodeOptions
                {
                    Player = CreateDefaultPlayerData(),
                    Password = string.IsNullOrEmpty(password) ? null : password
                };

                var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(normalized, joinOpts);
                if (lobby == null)
                {
                    Fail("Failed to join lobby");
                    return false;
                }

                HandleLobbyJoined(lobby);
                return true;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyHandler] LobbyServiceException - Reason: {e.Reason}, Message: {e.Message}");
                Fail(e.Message);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] Unexpected exception: {e.Message}");
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

        /// <summary>Được gọi bởi LobbyRuntime thông qua LobbyManager</summary>
        public void OnLobbyUpdated(Lobby lobby)
        {
            if (lobby == null) return;

            bool shouldUpdate = false;

            if (CachedLobby == null)
                shouldUpdate = true;
            else if (!string.Equals(CachedLobby.Id, lobby.Id, StringComparison.Ordinal))
                shouldUpdate = true;
            else
            {
                try
                {
                    shouldUpdate = lobby.LastUpdated != CachedLobby.LastUpdated;
                }
                catch
                {
                    shouldUpdate =
                        (lobby.Players?.Count ?? 0) != (CachedLobby.Players?.Count ?? 0) ||
                        (lobby.Data?.Count ?? 0) != (CachedLobby.Data?.Count ?? 0);
                }
            }

            if (!shouldUpdate) return;

            UpdateCachedLobby(lobby, "poll updated");
            LobbyEvents.TriggerLobbyUpdated(lobby, "poll updated");
        }

        /// <summary>
        /// Kick một player khỏi lobby (host-only).
        /// </summary>
        public async Task<bool> KickPlayerAsync(string targetPlayerId)
        {
            if (!ValidateService()) return false;

            var lobby = CachedLobby;
            if (lobby == null || string.IsNullOrEmpty(lobby.Id))
            {
                Debug.LogWarning("[LobbyHandler] Kick failed: Not in a lobby");
                return false;
            }

            if (!NetworkController.Instance.IsHost || lobby.HostId != AuthenticationService.Instance.PlayerId)
            {
                Debug.LogWarning("[LobbyHandler] Kick failed: Only host can kick players");
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetPlayerId))
            {
                Debug.LogWarning("[LobbyHandler] Kick failed: target player id is empty");
                return false;
            }

            if (targetPlayerId == AuthenticationService.Instance.PlayerId)
            {
                Debug.LogWarning("[LobbyHandler] Kick failed: host cannot kick self. Use RemoveLobbyAsync()");
                return false;
            }

            if (targetPlayerId == lobby.HostId)
            {
                Debug.LogWarning("[LobbyHandler] Kick failed: cannot kick the host");
                return false;
            }

            var oldPlayer = lobby.Players?.FirstOrDefault(p => p.Id == targetPlayerId);

            try
            {
                await LobbyService.Instance.RemovePlayerAsync(lobby.Id, targetPlayerId);

                // Lấy snapshot mới để cập nhật cache & bắn event chính xác
                Lobby updated = null;
                try
                {
                    updated = await LobbyService.Instance.GetLobbyAsync(lobby.Id);
                }
                catch
                {
                    // nếu lỗi tạm thời, vẫn tiếp tục với cache cũ đã loại player
                }

                if (updated != null)
                    UpdateCachedLobby(updated, "player kicked");
                else
                    LobbyEvents.TriggerLobbyUpdated(lobby, "player kicked");

                if (oldPlayer != null && updated != null)
                    LobbyEvents.TriggerPlayerLeft(oldPlayer, updated, "Player kicked by host");
                else
                    LobbyEvents.TriggerLobbyUpdated(updated ?? lobby, "Player kicked by host");

                Debug.Log($"[LobbyHandler] Kicked player {targetPlayerId}");
                return true;
            }
            catch (LobbyServiceException ex)
            {
                Debug.LogError($"[LobbyHandler] Kick failed: {ex.Reason} - {ex.Message}");
                LobbyEvents.TriggerLobbyUpdated(lobby, $"Kick failed: {ex.Message}");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] Kick failed: {e.Message}");
                LobbyEvents.TriggerLobbyUpdated(lobby, $"Kick failed: {e.Message}");
                return false;
            }
        }

        #endregion

        #region Event Handlers

        private void HandleLobbyCreated(Lobby lobby)
        {
            UpdateCachedLobby(lobby);

            // Bắt đầu runtime cho host
            _runtime?.StartRuntime(lobby.Id, true);

            LobbyEvents.TriggerLobbyCreated(lobby, true, $"Lobby '{lobby.Name}' created");
        }

        private void HandleLobbyJoined(Lobby lobby)
        {
            UpdateCachedLobby(lobby);

            var isHost = NetworkController.Instance.IsHost;
            _runtime?.StartRuntime(lobby.Id, isHost);

            LobbyEvents.TriggerLobbyJoined(lobby, true, $"Joined lobby '{lobby.Name}'");
        }

        private void HandleLobbyLeft()
        {
            _runtime?.StopRuntime();
            ClearCachedLobby();
            LobbyEvents.TriggerLobbyLeft(null, true, "Left lobby");
        }

        private void HandleLobbyRemoved()
        {
            _runtime?.StopRuntime();
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
            var playerId    = AuthenticationService.Instance?.PlayerId ?? "Unknown";
            var displayName = LocalData.UserName ?? $"Player_{playerId[..Math.Min(6, playerId.Length)]}";

            return new Unity.Services.Lobbies.Models.Player
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { LobbyConstants.PlayerData.DISPLAY_NAME, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, displayName) },
                    { LobbyConstants.PlayerData.IS_READY,     new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, LobbyConstants.Defaults.READY_FALSE) }
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

        #region Debug & Cleanup

        [ContextMenu("Debug Lobby State")]
        private void DebugLobbyState()
        {
            Debug.Log($"[LobbyHandler] State:" +
                      $"\n  Current Lobby ID: {CurrentLobbyId}" +
                      $"\n  Cached Lobby: {(CachedLobby != null ? CachedLobby.Name : "null")}" +
                      $"\n  Is Host: {NetworkController.Instance.IsHost}" +
                      $"\n  Runtime Running: {(_runtime?.IsRunning ?? false)}");
        }

        // Cho LobbyManager gọi khi dispose hệ thống
        public void OnDestroy()
        {
            try { _runtime?.StopRuntime(); } catch { /* no-op */ }
            ClearCachedLobby();
        }

        #endregion
    }
}

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
    public interface ILobbyOperations
    {
        Task<Lobby> CreateLobbyAsync(string lobbyName, int maxPlayers, CreateLobbyOptions options = null);
        Task<bool> JoinLobbyAsync(string lobbyCode, string password = null);
        Task<bool> LeaveLobbyAsync();
        Task<bool> RemoveLobbyAsync();
        Task<bool> KickPlayerAsync(string playerId);
        Task<Lobby> GetLobbyInfoAsync(string lobbyId);
        Task<Lobby> UpdateLobbyAsync(string lobbyId, UpdateLobbyOptions options);
    }

    public class LobbyHandler : SingletonDontDestroy<LobbyHandler>, ILobbyOperations
    {
        [Header("Lobby Settings")] [SerializeField]
        private float heartbeatInterval = 15f;

        [SerializeField] private float lobbyRefreshInterval = 2f;

        [SerializeField] private LobbyHeartbeat _heartbeat;
        [SerializeField] private LobbyUpdater _updater;
        private bool _isInitialized = false;

        public string CurrentLobbyId => NetIdHub.LobbyId;
        public Lobby CachedLobby { get; private set; }

        protected override void OnAwake()
        {
            base.OnAwake();
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            if (_isInitialized) return;

            try
            {
                _heartbeat ??= GetComponent<LobbyHeartbeat>() ?? gameObject.AddComponent<LobbyHeartbeat>();
                _updater ??= GetComponent<LobbyUpdater>() ?? gameObject.AddComponent<LobbyUpdater>();

                _heartbeat.Initialize(this, heartbeatInterval);
                _updater.Initialize(this, lobbyRefreshInterval);

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
                await HandleLobbyCreated(lobby);
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
                        new QueryFilter(
                            field: QueryFilter.FieldOptions.S1, // CODE được index ở S1
                            op: QueryFilter.OpOptions.EQ,
                            value: normalized)
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
                    Fail(phase switch
                    {
                        SessionPhase.STARTING => "Game is starting, cannot join",
                        SessionPhase.PLAYING => "Game is already in progress, cannot join",
                        _ => "Lobby is not accepting new players"
                    });
                    return false;
                }

                // Ok → join
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

                await HandleLobbyJoined(lobby);
                return true;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyHandler] PrecheckPhaseThenJoin failed: {e.Reason} - {e.Message}");
                Fail(e.Message);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHandler] PrecheckPhaseThenJoin failed: {e.Message}");
                Fail(e.Message);
                return false;
            }

            void Fail(string msg)
            {
                Debug.LogWarning($"[LobbyHandler] {msg}");
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
                bool isHost = NetIdHub.IsLocalHost();

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

            if (!NetIdHub.IsLocalHost())
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

            if (!NetIdHub.IsLocalHost())
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

        public void RaiseLobbyUpdated(Lobby lobby)
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

        private async Task HandleLobbyCreated(Lobby lobby)
        {
            UpdateCachedLobby(lobby);
            NetIdHub.BindLobby(lobby);

            if (NetIdHub.IsLocalHost())
            {
                _heartbeat?.StartHeartbeat(lobby.Id);
            }

            _updater?.StartUpdating(lobby.Id);
            
           // await LobbyDataExtensions.UpdateLobbyDataValueAsync(
           //      lobby.Id,
           //      LobbyConstants.LobbyKeys.CODE,
           //      lobby.LobbyCode,
           //      DataObject.IndexOptions.S1,
           //      DataObject.VisibilityOptions.Public
           //  );
           //
           // await LobbyDataExtensions.UpdateLobbyDataValueAsync(
           //     lobby.Id,
           //     LobbyConstants.LobbyKeys.PHASE,
           //     SessionPhase.WAITING,
           //     DataObject.IndexOptions.S2,
           //     DataObject.VisibilityOptions.Public
           // );
            
            LobbyEvents.TriggerLobbyCreated(lobby, true, $"Lobby '{lobby.Name}' created");
        }

        private async Task HandleLobbyJoined(Lobby lobby)
        {
            UpdateCachedLobby(lobby);
            NetIdHub.BindLobby(lobby);

            if (NetIdHub.IsLocalHost())
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

        /// <summary>
        /// Called by LobbyUpdater when lobby data changes
        /// </summary>
        public void OnLobbyUpdated(Lobby lobby, string message = "Lobby updated")
        {
            if (lobby != null)
            {
                UpdateCachedLobby(lobby);
                NetIdHub.BindLobby(lobby);
            }

            LobbyEvents.TriggerLobbyUpdated(lobby, message);
        }

        private void UpdateCachedLobby(Lobby lobby)
        {
            if (lobby != null)
            {
                CachedLobby = lobby;
            }
        }

        private void ClearCachedLobby()
        {
            CachedLobby = null;
            NetIdHub.Clear();
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

        protected override void OnDestroy()
        {
            if (Instance == this)
            {
                _heartbeat?.StopHeartbeat();
                _updater?.StopUpdating();
                ClearCachedLobby();
                base.OnDestroy();
            }
        }

        #endregion

        #region Debug

        [ContextMenu("Debug Lobby State")]
        private void DebugLobbyState()
        {
            Debug.Log($"[LobbyHandler] State:" +
                      $"\n  Current Lobby ID: {CurrentLobbyId}" +
                      $"\n  Cached Lobby: {(CachedLobby != null ? CachedLobby.Name : "null")}" +
                      $"\n  Is Host: {NetIdHub.IsLocalHost()}" +
                      $"\n  Heartbeat Running: {(_heartbeat?.IsActive ?? false)}" +
                      $"\n  Updater Running: {(_updater?.IsRunning ?? false)}");
        }

        #endregion


        public void StopUpdater() => _updater.StopUpdating();

        public void StopHeartbeat() => _heartbeat.StopHeartbeat();
    }
}
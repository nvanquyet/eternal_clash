using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _GAME.Scripts.Data;
using _GAME.Scripts.Networking.Lobbies;
using GAME.Scripts.DesignPattern;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Lobbies
{
    public interface ILobbyOperations
    {
        Task<Lobby> CreateLobbyAsync(string lobbyName, int maxPlayers, CreateLobbyOptions options);
        Task<bool> JoinLobbyAsync(string lobbyCode, string password);
        Task<bool> LeaveLobbyAsync(string lobbyId, string playerId, bool isHost = false);
        Task<bool> RemoveLobbyAsync(string lobbyId);
        Task<bool> KickPlayerAsync(string lobbyId, string playerId);
        Task<Lobby> GetLobbyInfoAsync(string lobbyId);

        // Convenience updates (khớp với UI)
        Task<Lobby> UpdateLobbyAsync(string lobbyId, UpdateLobbyOptions updateOptions = null);
        Task<Lobby> UpdateLobbyNameAsync(string lobbyId, string newName);
        Task<Lobby> UpdateLobbyMaxPlayersAsync(string lobbyId, int maxPlayers);
        Task<Lobby> UpdateLobbyPasswordInDataAsync(string lobbyId, string newPassword, bool visibleToMembers = true, bool alsoSetBuiltIn = true);
        Task<Lobby> ToggleReadyAsync(string lobbyId, bool isReady);
        Task<Lobby> SetDisplayNameAsync(string lobbyId, string displayName);
    }

    public class LobbyHandler : SingletonDontDestroy<LobbyHandler>, ILobbyOperations
    {
        [Header("Lobby Settings")]
        [SerializeField] private float heartbeatInterval = 15f;
        [SerializeField] private float lobbyRefreshInterval = 2f;

        // NO MORE EVENT DEFINITIONS HERE - All events go through LobbyEvents

        private LobbyHeartbeat _heartbeat;
        private LobbyUpdater _updater;

        private string _currentLobbyId;
        
        public string CurrentLobbyId => _currentLobbyId;

        public bool IsInLobby => !string.IsNullOrEmpty(_currentLobbyId);
        public Lobby CachedLobby { get; private set; }

        public bool IsHost()
        {
            if (string.IsNullOrEmpty(_currentLobbyId)) return false;
            // You might want to cache the current lobby or check host status
            // For now, this is a simple check - you may need to enhance this
            return _heartbeat.IsActive; // Host is usually the one sending heartbeat
        }
        
        protected override void OnAwake()
        {
            base.OnAwake();
            _heartbeat = gameObject.AddComponent<LobbyHeartbeat>();
            _updater   = gameObject.AddComponent<LobbyUpdater>();
            _heartbeat.Initialize(this, heartbeatInterval);
            _updater.Initialize(this, lobbyRefreshInterval);
        }

        // -------- Create / Join / Leave / Remove --------

        public async Task<Lobby> CreateLobbyAsync(string lobbyName, int maxPlayers, CreateLobbyOptions options)
        {
            try
            {
                // Bảo đảm Player khởi tạo có dữ liệu khớp UI
                options ??= new CreateLobbyOptions();
                options.Player ??= GetDefaultPlayerData();

                var lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);

                _currentLobbyId = lobby.Id;

                // Heartbeat chỉ host
                if (lobby.HostId == AuthenticationService.Instance.PlayerId)
                    _heartbeat.StartHeartbeat(lobby.Id);

                _updater.StartUpdating(lobby.Id);

                // Fire event through LobbyEvents
                LobbyEvents.TriggerLobbyCreated(lobby, true, $"Lobby '{lobby.Name}' created");

                return lobby;
            }
            catch (Exception e)
            {
                Debug.LogError($"CreateLobby failed: {e}");
                LobbyEvents.TriggerLobbyCreated(null, false, e.Message);
                return null;
            }
        }

        public async Task<bool> JoinLobbyAsync(string lobbyCode, string password)
        {
            try
            {
                var joinOptions = new JoinLobbyByCodeOptions
                {
                    Player = GetDefaultPlayerData(),
                    Password = password // built-in protect
                };

                var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, joinOptions);
                if (lobby != null)
                {
                    _currentLobbyId = lobby.Id;

                    // Chỉ host mới heartbeat
                    if (lobby.HostId == AuthenticationService.Instance.PlayerId)
                        _heartbeat.StartHeartbeat(lobby.Id);
                    else
                        _heartbeat.StopHeartbeat();

                    _updater.StartUpdating(lobby.Id);

                    LobbyEvents.TriggerLobbyJoined(lobby, true, $"Joined lobby '{lobby.Name}'");
                    return true;
                }

                LobbyEvents.TriggerLobbyJoined(null, false, "Lobby not found / full / wrong password");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"JoinLobby failed: {e}");
                LobbyEvents.TriggerLobbyJoined(null, false, e.Message);
                return false;
            }
        }

        public async Task<bool> LeaveLobbyAsync(string lobbyId, string playerId, bool isHost = false)
        {
            try
            {
                if (string.IsNullOrEmpty(lobbyId)) lobbyId = _currentLobbyId;

                if (isHost)
                {
                    await RemoveLobbyAsync(lobbyId);
                }
                else
                {
                    await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
                    _updater.StopUpdating();
                }

                if (_currentLobbyId == lobbyId)
                {
                    _currentLobbyId = null;
                    _heartbeat.StopHeartbeat();
                    _updater.StopUpdating();
                }

                LobbyEvents.TriggerLobbyLeft(null, true, "Left lobby");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"LeaveLobby failed: {e}");
                LobbyEvents.TriggerLobbyLeft(null, false, e.Message);
                return false;
            }
        }

        public async Task<bool> RemoveLobbyAsync(string lobbyId)
        {
            try
            {
                if (string.IsNullOrEmpty(lobbyId)) lobbyId = _currentLobbyId;

                await LobbyService.Instance.DeleteLobbyAsync(lobbyId);

                if (_currentLobbyId == lobbyId)
                {
                    _currentLobbyId = null;
                    _heartbeat.StopHeartbeat();
                    _updater.StopUpdating();
                }

                LobbyEvents.TriggerLobbyRemoved(null, true, "Lobby removed");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"RemoveLobby failed: {e}");
                LobbyEvents.TriggerLobbyRemoved(null, false, e.Message);
                return false;
            }
        }

        public async Task<bool> KickPlayerAsync(string lobbyId, string playerId)
        {
            try
            {
                await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
                LobbyEvents.TriggerPlayerKicked(null, null, $"Kicked {playerId}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"KickPlayer failed: {e}");
                LobbyEvents.TriggerPlayerKicked(null, null, e.Message);
                return false;
            }
        }

        public async Task<Lobby> GetLobbyInfoAsync(string lobbyId)
        {
            try
            {
                var lobby = await LobbyService.Instance.GetLobbyAsync(lobbyId);
                return lobby;
            }
            catch (Exception e)
            {
                Debug.LogError($"GetLobbyInfo failed: {e}");
                return null;
            }
        }

        // -------- Update helpers (khớp với UI) --------
        
        /// <summary>
        /// Method for LobbyUpdater to call when lobby snapshot changes
        /// </summary>
        public void RaiseLobbyUpdated(Lobby lobby, string message = "")
        {
            if (lobby != null)
            {
                CachedLobby = lobby;            // giữ snapshot mới nhất cho Extensions/UI
                _currentLobbyId = lobby.Id;     // đảm bảo id nhất quán
            }
            LobbyEvents.TriggerLobbyUpdated(lobby, message);
        }
        
        public async Task<Lobby> UpdateLobbyAsync(string lobbyId, UpdateLobbyOptions updateOptions = null)
        {
            try
            {
                var updated = await LobbyService.Instance.UpdateLobbyAsync(lobbyId, updateOptions ?? new UpdateLobbyOptions());
                LobbyEvents.TriggerLobbyUpdated(updated, "Lobby updated");
                return updated;
            }
            catch (Exception e)
            {
                Debug.LogError($"UpdateLobby failed: {e}");
                LobbyEvents.TriggerLobbyUpdated(null, e.Message);
                return null;
            }
         }

        public Task<Lobby> UpdateLobbyNameAsync(string lobbyId, string newName)
        {
            var opts = new UpdateLobbyOptions { Name = newName };
            return UpdateLobbyAsync(lobbyId, opts);
        }

        public Task<Lobby> UpdateLobbyMaxPlayersAsync(string lobbyId, int maxPlayers)
        {
            var opts = new UpdateLobbyOptions { MaxPlayers = maxPlayers };
            return UpdateLobbyAsync(lobbyId, opts);
        }

        /// <summary>
        /// Cập nhật mật khẩu để HIỂN THỊ CHO MỌI NGƯỜI qua Lobby.Data["Password"].
        /// alsoSetBuiltIn = true: đồng bộ luôn built-in Password (để bảo vệ join).
        /// visibleToMembers = true: dùng Member visibility (chỉ người trong lobby thấy). Nếu false → Public.
        /// </summary>
        public async Task<Lobby> UpdateLobbyPasswordInDataAsync(string lobbyId, string newPassword, bool visibleToMembers = true, bool alsoSetBuiltIn = true)
        {
            try
            {
                var visibility = visibleToMembers ? DataObject.VisibilityOptions.Member : DataObject.VisibilityOptions.Public;

                var data = new Dictionary<string, DataObject>
                {
                    { LobbyConstants.LobbyData.PASSWORD, new DataObject(visibility, newPassword ?? string.Empty) }
                };

                var opts = new UpdateLobbyOptions
                {
                    Data = data
                };

                if (alsoSetBuiltIn)
                {
                    // Built-in: string.Empty để xóa pass
                    opts.Password = newPassword ?? string.Empty;
                }

                var updated = await LobbyService.Instance.UpdateLobbyAsync(lobbyId, opts);
                LobbyEvents.TriggerLobbyUpdated(updated, "Password updated");
                return updated;
            }
            catch (Exception e)
            {
                Debug.LogError($"Update password in data failed: {e}");
                LobbyEvents.TriggerLobbyUpdated(null, e.Message);
                return null;
            }
        }

        public async Task<Lobby> ToggleReadyAsync(string lobbyId, bool isReady)
        {
            try
            {
                var meId = AuthenticationService.Instance.PlayerId;
                var playerOpts = new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { LobbyConstants.PlayerData.IS_READY, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, isReady ? LobbyConstants.Defaults.READY_TRUE : LobbyConstants.Defaults.READY_FALSE) }
                    }
                };

                var updated = await LobbyService.Instance.UpdatePlayerAsync(lobbyId, meId, playerOpts);
                
                // Find current player and trigger event
                var currentPlayer = updated.Players.Find(p => p.Id == meId);
                if (currentPlayer != null)
                {
                    LobbyEvents.TriggerPlayerUpdated(currentPlayer, updated, $"Player ready status: {isReady}");
                }

                return updated;
            }
            catch (Exception e)
            {
                Debug.LogError($"ToggleReady failed: {e}");
                return null;
            }
        }

        public async Task<Lobby> SetDisplayNameAsync(string lobbyId, string displayName)
        {
            try
            {
                var meId = AuthenticationService.Instance.PlayerId;
                var playerOpts = new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { LobbyConstants.PlayerData.DISPLAY_NAME, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, displayName ?? string.Empty) }
                    }
                };

                var updated = await LobbyService.Instance.UpdatePlayerAsync(lobbyId, meId, playerOpts);
                
                // Find current player and trigger event
                var currentPlayer = updated.Players.Find(p => p.Id == meId);
                if (currentPlayer != null)
                {
                    LobbyEvents.TriggerPlayerUpdated(currentPlayer, updated, $"Display name changed: {displayName}");
                }

                return updated;
            }
            catch (Exception e)
            {
                Debug.LogError($"SetDisplayName failed: {e}");
                return null;
            }
        }

        // -------- Defaults / Cleanup --------

        private Unity.Services.Lobbies.Models.Player GetDefaultPlayerData()
        {
            // KEY khớp với UI: "DisplayName", "IsReady"
            return new Unity.Services.Lobbies.Models.Player
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { LobbyConstants.PlayerData.DISPLAY_NAME, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, $"{LocalData.UserName}") },
                    { LobbyConstants.PlayerData.IS_READY, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, LobbyConstants.Defaults.READY_FALSE) }
                }
            };
        }

        protected override void OnDestroy()
        {
            if (Instance == this)
            {
                _heartbeat?.StopHeartbeat();
                _updater?.StopUpdating();
                base.OnDestroy();
            }
        }
    }
}
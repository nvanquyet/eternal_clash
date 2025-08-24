using System;
using System.Threading;
using System.Threading.Tasks;
using _GAME.Scripts.Networking.Lobbies;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Lobbies
{
    /// <summary>
    /// Cập nhật snapshot lobby định kỳ (poll) và phát hiện thay đổi.
    /// Updater chạy cho cả host và member.
    /// </summary>
    public class LobbyUpdater : MonoBehaviour
    {
        private LobbyHandler _lobbyManager;
        private LobbyHeartbeat _heartbeat;

        [SerializeField] private float _updateInterval = 4f; // thưa hơn để giảm request
        private string _currentLobbyId;
        private CancellationTokenSource _cts;
        private bool _isUpdating;

        // state
        private Lobby _lastSnapshot;
        private bool _wasHost;
        private float _backoffSec;// tăng khi lỗi/429
        
        public bool IsRunning => _isUpdating;

        public void Initialize(LobbyHandler lobbyManager, float interval = 4f)
        {
            _lobbyManager = lobbyManager;
            _heartbeat = lobbyManager.GetComponent<LobbyHeartbeat>();
            _updateInterval = interval;
        }

        public void StartUpdating(string lobbyId)
        {
            if (_isUpdating && _currentLobbyId == lobbyId) return; // idempotent
            StopUpdating();

            _currentLobbyId = lobbyId;
            _lastSnapshot = null;
            _backoffSec = 0f;
            _wasHost = false;

            _cts = new CancellationTokenSource();
            _isUpdating = true;

            _ = UpdateLoop(_cts.Token);
            Debug.Log($"[LobbyUpdater] Started updates for: {lobbyId}");
        }

        public void StopUpdating()
        {
            if (!_isUpdating) return;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _isUpdating = false;
            _currentLobbyId = null;
            _lastSnapshot = null;
            _backoffSec = 0f;

            Debug.Log("[LobbyUpdater] Stopped updates");
        }

        private async Task UpdateLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _isUpdating)
                {
                    var wait = Mathf.Max(_updateInterval, _backoffSec);
                    await Task.Delay(TimeSpan.FromSeconds(wait), token);
                    if (token.IsCancellationRequested || !_isUpdating) break;
                    if (string.IsNullOrEmpty(_currentLobbyId)) continue;

                    Lobby lobby = null;
                    try
                    {
                        lobby = await LobbyService.Instance.GetLobbyAsync(_currentLobbyId);
                        _backoffSec = 0f; // reset backoff khi thành công
                    }
                    catch (LobbyServiceException ex)
                    {
                        // 404/410 → lobby không còn
                        if (ex.Reason == LobbyExceptionReason.LobbyNotFound)
                        {
                            Debug.LogWarning("[LobbyUpdater] Lobby removed");
                            StopUpdating();
                            LobbyEvents.TriggerLobbyRemoved(null, false, "Lobby not found or removed");
                            break;
                        }

                        // 429 hoặc lỗi mạng → backoff
                        _backoffSec = Mathf.Clamp(_backoffSec <= 0 ? 2f : _backoffSec * 1.5f, 0f, 30f);
                        Debug.LogWarning($"[LobbyUpdater] GetLobbyAsync error ({ex.Reason}), backoff={_backoffSec:0.0}s");
                        continue;
                    }
                    catch (Exception e)
                    {
                        _backoffSec = Mathf.Clamp(_backoffSec <= 0 ? 2f : _backoffSec * 1.5f, 0f, 30f);
                        Debug.LogError($"[LobbyUpdater] Unexpected error: {e.Message}, backoff={_backoffSec:0.0}s");
                        continue;
                    }

                    if (lobby == null) continue;

                    // 1) Diff players & fire events
                    CheckForPlayerChanges(lobby);

                    // 2) Game state flags (ví dụ)
                    CheckForGameStart(lobby);

                    // 3) Host migration → chỉ đổi heartbeat khi trạng thái thay đổi
                    bool iAmHost = lobby.HostId == AuthenticationService.Instance.PlayerId;
                    if (iAmHost != _wasHost)
                    {
                        if (iAmHost) _heartbeat?.StartHeartbeat(lobby.Id);
                        else         _heartbeat?.StopHeartbeat();
                        _wasHost = iAmHost;
                    }

                    // 4) Raise snapshot mới cho UI
                    _lobbyManager?.RaiseLobbyUpdated(lobby);
                    
                    _lastSnapshot = lobby;
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[LobbyUpdater] Update loop cancelled");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyUpdater] Update loop error: {e}");
                _isUpdating = false;
            }
        }

        private void CheckForPlayerChanges(Lobby newLobby)
        {
            if (_lastSnapshot == null) return;

            var oldSet = new System.Collections.Generic.HashSet<string>();
            foreach (var p in _lastSnapshot.Players) oldSet.Add(p.Id);

            var newSet = new System.Collections.Generic.HashSet<string>();
            foreach (var p in newLobby.Players) newSet.Add(p.Id);

            // Join
            foreach (var p in newLobby.Players)
                if (!oldSet.Contains(p.Id))
                    LobbyEvents.TriggerPlayerJoined(p, newLobby, "Player joined");

            // Leave
            foreach (var p in _lastSnapshot.Players)
                if (!newSet.Contains(p.Id))
                    LobbyEvents.TriggerPlayerLeft(p, newLobby, "Player left");

            // Data changes (ready/name)
            foreach (var p in newLobby.Players)
            {
                var before = _lastSnapshot.Players.Find(x => x.Id == p.Id);
                if (before == null) continue;

                string oldReady = before.Data != null && before.Data.TryGetValue("IsReady", out var oR) ? oR.Value : null;
                string newReady = p.Data     != null && p.Data.TryGetValue("IsReady", out var nR)      ? nR.Value : null;
                string oldName  = before.Data != null && before.Data.TryGetValue("DisplayName", out var oN) ? oN.Value : null;
                string newName  = p.Data     != null && p.Data.TryGetValue("DisplayName", out var nN)      ? nN.Value : null;

                if (oldReady != newReady || oldName != newName)
                    LobbyEvents.TriggerPlayerUpdated(p, newLobby, "Player data changed");
            }
        }

        private void CheckForGameStart(Lobby lobby)
        {
            if (lobby.Data != null && lobby.Data.TryGetValue("gameStarted", out var value))
            {
                if (value.Value == "true")
                {
                    Debug.Log("[LobbyUpdater] Game started");
                    StopUpdating();
                }
            }
        }

        private void OnDestroy() => StopUpdating();

        // private void OnApplicationPause(bool pauseStatus)
        // {
        //     // Giảm rủi ro vòng lặp bị restart vô hạn → chỉ dừng khi pause,
        //     // resume do Handler quyết định (không auto start ở đây).
        //     if (pauseStatus) StopUpdating();
        //     else
        //     {
        //         // Khi resume, có thể cần khởi động lại nếu vẫn đang trong lobby
        //         if (_isUpdating && !string.IsNullOrEmpty(_currentLobbyId))
        //         {
        //             StartUpdating(_currentLobbyId);
        //         }
        //     }
        // }
    }
}

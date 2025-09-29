// LobbyRuntime.cs — gộp LobbyHeartbeat + LobbyUpdater thành 1 MonoBehaviour

using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking.Lobbies
{
    /// <summary>
    /// Runtime duy nhất quản lý cả Heartbeat (host) & Polling (tất cả)
    /// </summary>
    public class LobbyRuntime : MonoBehaviour
    {
        [Header("Intervals")]
        [SerializeField] private float _heartbeatInterval = 15f; // giây
        [SerializeField] private float _updateInterval = 4f;     // giây (poll)

        private string _currentLobbyId;
        private CancellationTokenSource _cts;
        private bool _isRunning;

        // state
        private Lobby _lastSnapshot;
        private bool _weAreHost;
        private float _backoffSec; // tăng khi lỗi/429

        public bool IsRunning => _isRunning;

        /// <summary>Thiết lập interval (tuỳ chọn, có thể dùng SerializeField)</summary>
        public void Initialize(float heartbeatInterval, float updateInterval)
        {
            _heartbeatInterval = heartbeatInterval;
            _updateInterval    = updateInterval;
        }

        /// <summary>Start cả heartbeat (nếu host) & updater</summary>
        public void StartRuntime(string lobbyId, bool isHost)
        {
            if (_isRunning && _currentLobbyId == lobbyId)
            {
                // chỉ update vai trò host nếu lobbyId không đổi
                _weAreHost = isHost;
                return;
            }

            StopRuntime();

            _currentLobbyId = lobbyId;
            _weAreHost      = isHost;
            _backoffSec     = 0f;
            _lastSnapshot   = null;

            _cts = new CancellationTokenSource();
            _isRunning = true;
            _ = RuntimeLoop(_cts.Token);

            Debug.Log($"[LobbyRuntime] Started runtime for Lobby: {lobbyId} (host={isHost})");
        }

        /// <summary>Dừng tất cả vòng lặp</summary>
        public void StopRuntime()
        {
            if (!_isRunning) return;

            try { _cts?.Cancel(); } catch { /* no-op */ }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }

            _isRunning     = false;
            _currentLobbyId= null;
            _lastSnapshot  = null;
            _backoffSec    = 0f;

            Debug.Log("[LobbyRuntime] Stopped runtime");
        }

        private async Task RuntimeLoop(CancellationToken token)
        {
            float lastHeartbeatTime = Time.realtimeSinceStartup;

            try
            {
                while (!token.IsCancellationRequested && _isRunning)
                {
                    // 1) Heartbeat (host-only) theo interval
                    if (_weAreHost && Time.realtimeSinceStartup - lastHeartbeatTime >= _heartbeatInterval)
                    {
                        await SendHeartbeat(token);
                        lastHeartbeatTime = Time.realtimeSinceStartup;
                    }

                    // 2) Đợi update interval / backoff rồi poll
                    var wait = Mathf.Max(_updateInterval, _backoffSec);
                    await Task.Delay(TimeSpan.FromSeconds(wait), token);
                    if (token.IsCancellationRequested || !_isRunning) break;

                    await PollLobby(token);
                    _backoffSec = 0f; // reset backoff khi thành công
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[LobbyRuntime] Runtime loop cancelled");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyRuntime] Runtime loop error: {e}");
                _isRunning = false;
            }
        }

        private async Task SendHeartbeat(CancellationToken token)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentLobbyId)) return;
                await LobbyService.Instance.SendHeartbeatPingAsync(_currentLobbyId);
                // giảm log spam
                // Debug.Log($"[LobbyRuntime] Heartbeat sent for {_currentLobbyId}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyRuntime] Heartbeat failed: {e}");
                // Nếu lỗi heartbeat → nhiều khả năng lobby đã bị remove
                StopRuntime();
                LobbyEvents.TriggerLobbyNotFound();
            }
        }

        private async Task PollLobby(CancellationToken token)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentLobbyId)) return;

                var lobby = await LobbyService.Instance.GetLobbyAsync(_currentLobbyId);

                // 3) Host migration: tự động bật/tắt heartbeat theo vai trò mới
                bool iAmHost = lobby.HostId == AuthenticationService.Instance.PlayerId;
                if (iAmHost != _weAreHost)
                {
                    _weAreHost = iAmHost;
                    Debug.Log($"[LobbyRuntime] Host role changed. Now host={_weAreHost}");
                }

                // 4) Bắn snapshot mới cho hệ thống (giống Updater trước đây)
                LobbyEvents.TriggerLobbyUpdated(lobby);

                _lastSnapshot = lobby;
            }
            catch (LobbyServiceException ex)
            {
                if (ex.Reason == LobbyExceptionReason.LobbyNotFound)
                {
                    Debug.LogWarning("[LobbyRuntime] Lobby not found (removed?)");
                    StopRuntime();
                    LobbyEvents.TriggerLobbyNotFound();
                    return;
                }

                // backoff khi lỗi mạng/429
                _backoffSec = Mathf.Clamp(_backoffSec <= 0 ? 2f : _backoffSec * 1.5f, 0f, 30f);
                Debug.LogWarning($"[LobbyRuntime] Poll error ({ex.Reason}), backoff={_backoffSec:0.0}s");
            }
            catch (Exception e)
            {
                _backoffSec = Mathf.Clamp(_backoffSec <= 0 ? 2f : _backoffSec * 1.5f, 0f, 30f);
                Debug.LogError($"[LobbyRuntime] Unexpected poll error: {e.Message}, backoff={_backoffSec:0.0}s");
            }
        }

        private void OnDestroy() => StopRuntime();

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                // dừng khi app pause để an toàn/tiết kiệm
                StopRuntime();
            }
        }
    }
}

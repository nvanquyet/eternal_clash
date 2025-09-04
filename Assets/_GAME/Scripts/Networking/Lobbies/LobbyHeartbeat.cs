using System;
using System.Threading;
using System.Threading.Tasks;
using _GAME.Scripts.Networking;
using _GAME.Scripts.Networking.Lobbies;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Lobbies
{
    public class LobbyHeartbeat : MonoBehaviour
    {
        private float _heartbeatInterval = 15f;
        private string _currentLobbyId;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isHeartbeatActive = false;

        public bool IsActive => _isHeartbeatActive;

        public void Initialize(float interval = 15f)
        {
            _heartbeatInterval = interval;
        }
        

        public void StartHeartbeat(string lobbyId)
        {
            if (_isHeartbeatActive && _currentLobbyId == lobbyId) return;

            StopHeartbeat();
            _currentLobbyId = lobbyId;
            _cancellationTokenSource = new CancellationTokenSource();
            _isHeartbeatActive = true;

            _ = HeartbeatLoop(_cancellationTokenSource.Token);
            Debug.Log($"[LobbyHeartbeat] Started heartbeat for lobby: {lobbyId}"); // <<< spacing fix
        }

        public void StopHeartbeat()
        {
            if (!_isHeartbeatActive) return;

            try
            {
                _cancellationTokenSource?.Cancel();
            }
            catch { /* no-op */ }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }

            _isHeartbeatActive = false;
            _currentLobbyId = null;
            Debug.Log("[LobbyHeartbeat] Stopped lobby heartbeat");
        }

        private async Task HeartbeatLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _isHeartbeatActive)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_heartbeatInterval), cancellationToken);
                    if (cancellationToken.IsCancellationRequested || !_isHeartbeatActive) break;

                    await SendHeartbeat();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[LobbyHeartbeat] Heartbeat loop cancelled");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHeartbeat] Heartbeat loop error: {e}");
                _isHeartbeatActive = false;
            }
        }

        private async Task SendHeartbeat()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentLobbyId))
                {
                    Debug.LogWarning("[LobbyHeartbeat] No lobby ID for heartbeat");
                    return;
                }

                await LobbyService.Instance.SendHeartbeatPingAsync(_currentLobbyId);
                // Debug: tắt log spam
                // Debug.Log($"[LobbyHeartbeat] Heartbeat sent for lobby: {_currentLobbyId}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyHeartbeat] Failed to send heartbeat: {e}");
                _isHeartbeatActive = false;

                // Thông báo cho hệ thống biết lobby có thể đã bị remove
                LobbyEvents.TriggerLobbyRemoved(null, false, "Heartbeat failed - lobby may be removed");
            }
        }

        private void OnDestroy()
        {
            StopHeartbeat();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                // dừng khi app pause để tiết kiệm và an toàn
                StopHeartbeat();
            }
            else
            {
                // KHÔNG tự start lại ở đây.
                // Updater sẽ kiểm tra vai trò host và chủ động bật/tắt.
            }
        }
    }
}

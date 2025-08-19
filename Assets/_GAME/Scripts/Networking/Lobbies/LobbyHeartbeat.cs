using System;
using System.Threading;
using System.Threading.Tasks;
using _GAME.Scripts.Networking.Lobbies;
using Unity.Services.Lobbies;
using UnityEngine;

namespace _GAME.Scripts.Lobbies
{
    /// <summary>
    /// Component để duy trì kết nối với lobby thông qua heartbeat
    /// Chỉ host mới cần gửi heartbeat
    /// </summary>
    public class LobbyHeartbeat : MonoBehaviour
    {
        private LobbyHandler _lobbyManager;
        private float _heartbeatInterval = 15f;
        private string _currentLobbyId;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isHeartbeatActive = false;
        
        public bool IsActive => _isHeartbeatActive;
        public string ActiveLobbyId => _currentLobbyId;

        public void Initialize(LobbyHandler lobbyManager, float interval = 15f)
        {
            _lobbyManager = lobbyManager;
            _heartbeatInterval = interval;
        }

        public void StartHeartbeat(string lobbyId)
        {
            // ĐÃ chạy đúng lobby rồi thì bỏ qua
            if (_isHeartbeatActive && _currentLobbyId == lobbyId) return;

            StopHeartbeat(); // đảm bảo clean trước khi start
            _currentLobbyId = lobbyId;
            _cancellationTokenSource = new CancellationTokenSource();
            _isHeartbeatActive = true;

            _ = HeartbeatLoop(_cancellationTokenSource.Token);
            Debug.Log($"[Lobby Heartbeat]Started heartbeat for lobby: {lobbyId}");
        }
 
        public void StopHeartbeat()
        {
            if (_isHeartbeatActive)
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _isHeartbeatActive = false;
                _currentLobbyId = null;
                Debug.Log("Stopped lobby heartbeat");
            }
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
                Debug.Log("Heartbeat loop cancelled");
            }
            catch (Exception e)
            {
                Debug.LogError($"Heartbeat loop error: {e}");
                _isHeartbeatActive = false;
            }
        }

        private async Task SendHeartbeat()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentLobbyId))
                {
                    Debug.LogWarning("No lobby ID for heartbeat");
                    return;
                }

                await LobbyService.Instance.SendHeartbeatPingAsync(_currentLobbyId);
                // Chỉ log khi debug, không spam console
                // Debug.Log($"Heartbeat sent for lobby: {_currentLobbyId}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to send heartbeat: {e}");
                // Có thể lobby đã bị xóa hoặc có lỗi khác
                _isHeartbeatActive = false;
                
                // Thông báo cho LobbyHandler biết heartbeat failed
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
                StopHeartbeat();
            }
            else if (_lobbyManager != null && !string.IsNullOrEmpty(_currentLobbyId))
            {
                StartHeartbeat(_currentLobbyId);
            }
        }
    }
}
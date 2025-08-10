using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using UnityEngine;

namespace _GAME.Scripts.Lobbies
{
    /// <summary>
    /// Component để tự động cập nhật thông tin lobby
    /// Cả host và member đều cần cập nhật để nhận thông tin mới nhất
    /// </summary>
    public class LobbyUpdater : MonoBehaviour
    {
        private LobbyHandler _lobbyManager;
        private float _updateInterval = 2f;
        private string _currentLobbyId;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isUpdating = false;

        public void Initialize(LobbyHandler lobbyManager, float interval = 2f)
        {
            _lobbyManager = lobbyManager;
            _updateInterval = interval;
        }

        public void StartUpdating(string lobbyId)
        {
            if (_isUpdating)
            {
                StopUpdating();
            }

            _currentLobbyId = lobbyId;
            _cancellationTokenSource = new CancellationTokenSource();
            _isUpdating = true;

            _ = UpdateLoop(_cancellationTokenSource.Token);
            Debug.Log($"Started lobby updates for: {lobbyId}");
        }

        public void StopUpdating()
        {
            if (_isUpdating)
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _isUpdating = false;
                _currentLobbyId = null;
                Debug.Log("Stopped lobby updates");
            }
        }

        private async Task UpdateLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _isUpdating)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_updateInterval), cancellationToken);
                    
                    if (cancellationToken.IsCancellationRequested) break;
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Update loop cancelled");
            }
            catch (Exception e)
            {
                Debug.LogError($"Update loop error: {e}");
                _isUpdating = false;
            }
        }

        private void CheckForPlayerChanges(Unity.Services.Lobbies.Models.Lobby newLobby)
        {
           
        }

        private void CheckForGameStart(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            if (lobby.Data != null && lobby.Data.TryGetValue("gameStarted", out var value))
            {
                var gameStarted = value.Value;
                if (gameStarted == "true")
                {
                    Debug.Log("Game has started!");
                    // Có thể trigger event game started
                    StopUpdating(); // Stop updating khi game bắt đầu
                }
            }
        }

        private string GetPlayerName(Unity.Services.Lobbies.Models.Player player)
        {
            if (player.Data != null && player.Data.TryGetValue("playerName", out var value))
            {
                return value.Value;
            }
            return $"Player_{player.Id}";
        }

        private void OnDestroy()
        {
            StopUpdating();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                StopUpdating();
            }
            else if (_lobbyManager != null && !string.IsNullOrEmpty(_currentLobbyId))
            {
                StartUpdating(_currentLobbyId);
            }
        }
    }
}
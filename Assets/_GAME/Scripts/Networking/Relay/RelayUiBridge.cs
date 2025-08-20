using _GAME.Scripts.Networking.Lobbies;
using _GAME.Scripts.UI;
using GAME.Scripts.DesignPattern;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Networking.Relay
{
    /// <summary>
    /// Lắng nghe relay events và cập nhật UI/PlayerEvent.
    /// Gắn script này vào một GameObject tồn tại trong các scene có relay flow.
    /// </summary>
    public class RelayUiBridge : SingletonDontDestroy<RelayUiBridge>
    {
        private void OnEnable()
        {
            LobbyEvents.OnRelayHostReady   += HandleHostReady;
            LobbyEvents.OnRelayClientReady += HandleClientReady;
            LobbyEvents.OnRelayError       += HandleError;
        }

        private void OnDisable()
        {
            LobbyEvents.OnRelayHostReady   -= HandleHostReady;
            LobbyEvents.OnRelayClientReady -= HandleClientReady;
            LobbyEvents.OnRelayError       -= HandleError;
        }

        private void HandleHostReady(string joinCode)
        {
            LoadingUI.Instance.SetProgress(1f, 1f, "Host setup completed, transitioning to waiting room...", () =>
            {
                LoadingUI.Instance.Complete(() =>
                {
                    Debug.Log("[RelayUiBridge] Host setup completed, transitioning to waiting room...");
                });
            });
        }

        private void HandleClientReady()
        {
            LoadingUI.Instance.SetProgress(1f, 1f, "Client connected, waiting for scene sync...", () =>
            {
                LoadingUI.Instance.Complete(() =>
                {
                    Debug.Log("[RelayUiBridge] Client connected, waiting for scene sync...");
                });
            });
        }

        private void HandleError(string message)
        {
            Debug.LogError($"[RelayUiBridge] Relay error: {message}");
            // TODO: bạn có thể show toast/dialog ở đây nếu muốn.
        }
    }
}
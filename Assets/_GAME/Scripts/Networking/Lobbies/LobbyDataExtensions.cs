using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _GAME.Scripts.Lobbies;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking.Lobbies
{
    /// <summary>
    /// Helper đọc/ghi Lobby.Data dùng chung cho mọi extension (giảm trùng lặp)
    /// </summary>
    public static class LobbyDataExtensions
    {
        public static string GetDataValue(this Lobby lobby, string key, string @default = "")
        {
            if (lobby?.Data != null &&
                lobby.Data.TryGetValue(key, out var obj) &&
                obj != null &&
                !string.IsNullOrEmpty(obj.Value))
                return obj.Value;
            return @default;
        }

        public static async Task<Lobby> SetDataValuesAsync(string lobbyId, Dictionary<string, DataObject> data)
        {
            try
            {
                if (LobbyHandler.Instance == null)
                {
                    Debug.LogError("[LobbyDataExtensions] LobbyHandler not available");
                    return null;
                }

                var updated = await LobbyHandler.Instance.UpdateLobbyAsync(lobbyId, new UpdateLobbyOptions { Data = data });
                if (updated != null)
                {
                    LobbyEvents.TriggerLobbyUpdated(updated, "Lobby.Data updated");
                }
                return updated;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyDataExtensions] SetDataValuesAsync failed: {e.Message}");
                return null;
            }
        }

        public static async Task<bool> SetDataValueAsync(string lobbyId, string key, string value,
            DataObject.VisibilityOptions visibility = DataObject.VisibilityOptions.Member)
        {
            var updated = await SetDataValuesAsync(lobbyId, new Dictionary<string, DataObject>
            {
                { key, new DataObject(visibility, value ?? string.Empty) }
            });
            return updated != null;
        }

        // ====== Conveniences cho Relay & Network Status ======
        private const string KEY_RELAY_JOIN    = LobbyConstants.LobbyData.RELAY_JOIN_CODE;
        private const string KEY_NETWORK_STATUS= LobbyConstants.LobbyData.NETWORK_STATUS;

        public static string GetRelayJoinCode(this Lobby lobby)
            => lobby.GetDataValue(KEY_RELAY_JOIN);

        public static string GetNetworkStatus(this Lobby lobby)
            => lobby.GetDataValue(KEY_NETWORK_STATUS, LobbyConstants.NetworkStatus.CONNECTING);

        public static Task<bool> SetRelayJoinCodeAsync(string lobbyId, string joinCode)
        {
            NetIdHub.SetRelayJoinCode(joinCode ?? "");
            return SetDataValueAsync(lobbyId, KEY_RELAY_JOIN, joinCode ?? "");
        }

        public static Task<bool> SetNetworkStatusAsync(string lobbyId, string status)
            => SetDataValueAsync(lobbyId, KEY_NETWORK_STATUS, status);

        public static bool IsNetworkReady(this Lobby lobby)
            => lobby.GetNetworkStatus() == LobbyConstants.NetworkStatus.READY;

        public static bool HasValidRelayCode(this Lobby lobby)
            => !string.IsNullOrWhiteSpace(lobby.GetRelayJoinCode());
    }
}

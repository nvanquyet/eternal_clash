using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _GAME.Scripts.Lobbies;
using _GAME.Scripts.Networking.Lobbies;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking.Relay
{
    public static class RelayExtension
    {
        private const string KEY_RELAY_JOIN = LobbyConstants.LobbyData.RELAY_JOIN_CODE;
        private const string KEY_NETWORK_STATUS = LobbyConstants.LobbyData.NETWORK_STATUS;

        // Utility methods
        private static string GetDataValue(this Lobby lobby, string key, string @default = "")
        {
            if (lobby?.Data != null && 
                lobby.Data.TryGetValue(key, out var obj) && 
                obj != null && 
                !string.IsNullOrEmpty(obj.Value))
                return obj.Value;
            return @default;
        }

        private static async Task<bool> SetDataValueAsync(string lobbyId, string key, string value,
            DataObject.VisibilityOptions visibility = DataObject.VisibilityOptions.Member)
        {
            try
            {
                if (LobbyHandler.Instance == null)
                {
                    Debug.LogError("[RelayExtension] LobbyHandler not available");
                    return false;
                }

                var data = new Dictionary<string, DataObject> {
                    { key, new DataObject(visibility, value ?? string.Empty) }
                };
                
                var updated = await LobbyHandler.Instance.UpdateLobbyAsync(lobbyId, 
                    new UpdateLobbyOptions() { Data = data });
                
                if (updated != null)
                {
                    LobbyEvents.TriggerLobbyUpdated(updated, $"Set {key} = {value}");
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[RelayExtension] SetDataValueAsync({key}) failed: {e.Message}");
            }
            return false;
        }

        // Public API
        public static string GetRelayJoinCode(this Lobby lobby) 
            => lobby.GetDataValue(KEY_RELAY_JOIN);

        public static string GetNetworkStatus(this Lobby lobby)
            => lobby.GetDataValue(KEY_NETWORK_STATUS, LobbyConstants.NetworkStatus.CONNECTING);

        public static Task<bool> SetRelayJoinCodeAsync(string lobbyId, string joinCode)
            => SetDataValueAsync(lobbyId, KEY_RELAY_JOIN, joinCode ?? "");

        public static Task<bool> SetNetworkStatusAsync(string lobbyId, string status)
            => SetDataValueAsync(lobbyId, KEY_NETWORK_STATUS, status);

        // Validation helpers
        public static bool IsNetworkReady(this Lobby lobby)
            => lobby.GetNetworkStatus() == LobbyConstants.NetworkStatus.READY;

        public static bool HasValidRelayCode(this Lobby lobby)
            => !string.IsNullOrWhiteSpace(lobby.GetRelayJoinCode());
    }
}
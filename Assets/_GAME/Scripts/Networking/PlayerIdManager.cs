using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;
using _GAME.Scripts.Networking.Relay;
using _GAME.Scripts.Networking.Lobbies;
using UnityEngine;

namespace _GAME.Scripts.Networking
{
    /// <summary>
    /// Trung tâm đồng bộ và cung cấp ID (PlayerId/LobbyId/LobbyCode/HostId/RelayJoinCode/LocalClientId).
    /// Không gọi SDK trực tiếp bên ngoài; mọi nơi khác chỉ đọc từ đây.
    /// </summary>
    public static class PlayerIdManager
    {
        // --- Sources ---
        public static string PlayerId => AuthenticationService.Instance?.PlayerId;

        public static ulong LocalClientId =>
            NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
        
        public static bool IsMe(string ugsPlayerId)
        {
            return ugsPlayerId == PlayerId;
        }
    }
}
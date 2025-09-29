using System;
using Unity.Services.Lobbies.Models;

namespace _GAME.Scripts.Networking.Lobbies
{
    public static class LobbyEvents
    {
        // ===== LOBBY LIFECYCLE =====
        public static event Action<Lobby, bool, string> OnLobbyCreated;
        public static event Action<Lobby, bool, string> OnLobbyJoined;
        public static event Action<Lobby>       OnLobbyUpdated;
        public static event Action       OnLobbyNotFound;

        // ===== PLAYER =====
        public static event Action<Unity.Services.Lobbies.Models.Player, Lobby, string> OnPlayerJoined;
        public static event Action<Unity.Services.Lobbies.Models.Player, Lobby, string> OnPlayerLeft;
        public static event Action<Unity.Services.Lobbies.Models.Player, Lobby, string> OnPlayerUpdated;
        
        // ===== TRIGGERS =====
        public static void TriggerLobbyCreated(Lobby lobby, bool success, string message) => OnLobbyCreated?.Invoke(lobby, success, message);
        public static void TriggerLobbyJoined (Lobby lobby, bool success, string message) => OnLobbyJoined?.Invoke(lobby, success, message);
        public static void TriggerLobbyUpdated(Lobby lobby)               => OnLobbyUpdated?.Invoke(lobby);
        public static void TriggerLobbyNotFound()               => OnLobbyNotFound?.Invoke();

        public static void TriggerPlayerJoined (Unity.Services.Lobbies.Models.Player p, Lobby lobby, string msg) => OnPlayerJoined?.Invoke(p, lobby, msg);
        public static void TriggerPlayerLeft   (Unity.Services.Lobbies.Models.Player p, Lobby lobby, string msg) => OnPlayerLeft?.Invoke(p, lobby, msg);
        public static void TriggerPlayerUpdated(Unity.Services.Lobbies.Models.Player p, Lobby lobby, string msg) => OnPlayerUpdated?.Invoke(p, lobby, msg);
        

        // ===== UTILITY =====
        public static void ClearAllEvents()
        {
            OnLobbyCreated = null;
            OnLobbyJoined  = null;
            OnLobbyUpdated = null;

            OnPlayerJoined  = null;
            OnPlayerLeft    = null;
            OnPlayerUpdated = null;
        }
    }
}

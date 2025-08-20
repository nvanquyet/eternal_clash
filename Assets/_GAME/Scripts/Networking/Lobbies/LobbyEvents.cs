using System;
using Unity.Services.Lobbies.Models;

namespace _GAME.Scripts.Networking.Lobbies
{
    public static class LobbyEvents
    {
        // ===== LOBBY LIFECYCLE =====
        public static event Action<Lobby, bool, string> OnLobbyCreated;
        public static event Action<Lobby, bool, string> OnLobbyJoined;
        public static event Action<Lobby, bool, string> OnLobbyLeft;
        public static event Action<Lobby, bool, string> OnLobbyRemoved;
        public static event Action<Lobby, string>       OnLobbyUpdated;

        // ===== PLAYER =====
        public static event Action<Unity.Services.Lobbies.Models.Player, Lobby, string> OnPlayerJoined;
        public static event Action<Unity.Services.Lobbies.Models.Player, Lobby, string> OnPlayerLeft;
        public static event Action<Unity.Services.Lobbies.Models.Player, Lobby, string> OnPlayerUpdated;
        public static event Action<Unity.Services.Lobbies.Models.Player, Lobby, string> OnPlayerKicked;

        // ===== GAME =====
        public static event Action<Lobby, string> OnGameStarted;

        // ===== RELAY (mới: gộp vào đây) =====
        public static event Action<string> OnRelayHostReady;   // joinCode
        public static event Action         OnRelayClientReady; // sau khi set transport
        public static event Action<string> OnRelayError;       // message

        // ===== TRIGGERS =====
        public static void TriggerLobbyCreated(Lobby lobby, bool success, string message) => OnLobbyCreated?.Invoke(lobby, success, message);
        public static void TriggerLobbyJoined (Lobby lobby, bool success, string message) => OnLobbyJoined?.Invoke(lobby, success, message);
        public static void TriggerLobbyLeft   (Lobby lobby, bool success, string message) => OnLobbyLeft?.Invoke(lobby, success, message);
        public static void TriggerLobbyRemoved(Lobby lobby, bool success, string message) => OnLobbyRemoved?.Invoke(lobby, success, message);
        public static void TriggerLobbyUpdated(Lobby lobby, string message)               => OnLobbyUpdated?.Invoke(lobby, message);

        public static void TriggerPlayerJoined (Unity.Services.Lobbies.Models.Player p, Lobby lobby, string msg) => OnPlayerJoined?.Invoke(p, lobby, msg);
        public static void TriggerPlayerLeft   (Unity.Services.Lobbies.Models.Player p, Lobby lobby, string msg) => OnPlayerLeft?.Invoke(p, lobby, msg);
        public static void TriggerPlayerUpdated(Unity.Services.Lobbies.Models.Player p, Lobby lobby, string msg) => OnPlayerUpdated?.Invoke(p, lobby, msg);
        public static void TriggerPlayerKicked (Unity.Services.Lobbies.Models.Player p, Lobby lobby, string msg) => OnPlayerKicked?.Invoke(p, lobby, msg);

        public static void TriggerGameStarted(Lobby lobby, string message) => OnGameStarted?.Invoke(lobby, message);

        // Relay triggers (mới)
        public static void TriggerRelayHostReady(string code) => OnRelayHostReady?.Invoke(code);
        public static void TriggerRelayClientReady()          => OnRelayClientReady?.Invoke();
        public static void TriggerRelayError(string message)  => OnRelayError?.Invoke(message);

        // ===== UTILITY =====
        public static void ClearAllEvents()
        {
            OnLobbyCreated = null;
            OnLobbyJoined  = null;
            OnLobbyLeft    = null;
            OnLobbyRemoved = null;
            OnLobbyUpdated = null;

            OnPlayerJoined  = null;
            OnPlayerLeft    = null;
            OnPlayerUpdated = null;
            OnPlayerKicked  = null;

            OnGameStarted = null;

            OnRelayHostReady  = null;
            OnRelayClientReady= null;
            OnRelayError      = null;
        }

        public static void LogSubscriberCounts()
        {
            UnityEngine.Debug.Log($"[LobbyEvents] Subscribers:" +
                $"\n  OnLobbyCreated: {OnLobbyCreated?.GetInvocationList()?.Length ?? 0}" +
                $"\n  OnLobbyJoined: {OnLobbyJoined?.GetInvocationList()?.Length ?? 0}" +
                $"\n  OnLobbyLeft: {OnLobbyLeft?.GetInvocationList()?.Length ?? 0}" +
                $"\n  OnLobbyRemoved: {OnLobbyRemoved?.GetInvocationList()?.Length ?? 0}" +
                $"\n  OnLobbyUpdated: {OnLobbyUpdated?.GetInvocationList()?.Length ?? 0}" +
                $"\n  OnPlayerJoined: {OnPlayerJoined?.GetInvocationList()?.Length ?? 0}" +
                $"\n  OnPlayerLeft: {OnPlayerLeft?.GetInvocationList()?.Length ?? 0}" +
                $"\n  OnPlayerUpdated: {OnPlayerUpdated?.GetInvocationList()?.Length ?? 0}" +
                $"\n  OnPlayerKicked: {OnPlayerKicked?.GetInvocationList()?.Length ?? 0}" +
                $"\n  OnGameStarted: {OnGameStarted?.GetInvocationList()?.Length ?? 0}" +
                $"\n  OnRelayHostReady: {OnRelayHostReady?.GetInvocationList()?.Length ?? 0}" +
                $"\n  OnRelayClientReady: {OnRelayClientReady?.GetInvocationList()?.Length ?? 0}" +
                $"\n  OnRelayError: {OnRelayError?.GetInvocationList()?.Length ?? 0}");
        }
    }
}

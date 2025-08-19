using System;
using Unity.Services.Lobbies.Models;

namespace _GAME.Scripts.Networking.Lobbies
{
    /// <summary>
    /// Centralized static event manager cho toàn bộ lobby system
    /// Tất cả events được định nghĩa ở đây, các component khác chỉ trigger/listen
    /// </summary>
    public static class LobbyEvents
    {
        // ========== LOBBY LIFECYCLE EVENTS ==========
        
        /// <summary>Fired when lobby is created (success/fail)</summary>
        public static event Action<Lobby, bool, string> OnLobbyCreated;
        
        /// <summary>Fired when player joins a lobby (success/fail)</summary>
        public static event Action<Lobby, bool, string> OnLobbyJoined;
        
        /// <summary>Fired when player leaves a lobby (success/fail)</summary>
        public static event Action<Lobby, bool, string> OnLobbyLeft;
        
        /// <summary>Fired when lobby is removed/deleted (success/fail)</summary>
        public static event Action<Lobby, bool, string> OnLobbyRemoved;
        
        /// <summary>Fired when lobby data changes (from polling or manual updates)</summary>
        public static event Action<Lobby, string> OnLobbyUpdated;

        // ========== PLAYER EVENTS ==========
        
        /// <summary>Fired when a player joins the lobby</summary>
        public static event Action<Unity.Services.Lobbies.Models.Player, Lobby, string> OnPlayerJoined;
        
        /// <summary>Fired when a player leaves the lobby</summary>
        public static event Action<Unity.Services.Lobbies.Models.Player, Lobby, string> OnPlayerLeft;
        
        /// <summary>Fired when player data changes (ready status, display name, etc.)</summary>
        public static event Action<Unity.Services.Lobbies.Models.Player, Lobby, string> OnPlayerUpdated;
        
        /// <summary>Fired when a player is kicked from the lobby</summary>
        public static event Action<Unity.Services.Lobbies.Models.Player, Lobby, string> OnPlayerKicked;

        // ========== GAME EVENTS ==========
        
        /// <summary>Fired when game starts</summary>
        public static event Action<Lobby, string> OnGameStarted;

        // ========== TRIGGER METHODS - Called by LobbyHandler/LobbyUpdater ==========

        public static void TriggerLobbyCreated(Lobby lobby, bool success, string message)
        {
            OnLobbyCreated?.Invoke(lobby, success, message);
        }

        public static void TriggerLobbyJoined(Lobby lobby, bool success, string message)
        {
            OnLobbyJoined?.Invoke(lobby, success, message);
        }

        public static void TriggerLobbyLeft(Lobby lobby, bool success, string message)
        {
            OnLobbyLeft?.Invoke(lobby, success, message);
        }

        public static void TriggerLobbyRemoved(Lobby lobby, bool success, string message)
        {
            OnLobbyRemoved?.Invoke(lobby, success, message);
        }

        public static void TriggerLobbyUpdated(Lobby lobby, string message)
        {
            OnLobbyUpdated?.Invoke(lobby, message);
        }

        public static void TriggerPlayerJoined(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message)
        {
            OnPlayerJoined?.Invoke(player, lobby, message);
        }

        public static void TriggerPlayerLeft(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message)
        {
            OnPlayerLeft?.Invoke(player, lobby, message);
        }

        public static void TriggerPlayerUpdated(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message)
        {
            OnPlayerUpdated?.Invoke(player, lobby, message);
        }

        public static void TriggerPlayerKicked(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message)
        {
            OnPlayerKicked?.Invoke(player, lobby, message);
        }

        public static void TriggerGameStarted(Lobby lobby, string message)
        {
            OnGameStarted?.Invoke(lobby, message);
        }

        // ========== UTILITY ==========

        /// <summary>
        /// Clear tất cả event subscriptions - useful khi restart game hoặc change scene
        /// </summary>
        public static void ClearAllEvents()
        {
            OnLobbyCreated = null;
            OnLobbyJoined = null;
            OnLobbyLeft = null;
            OnLobbyRemoved = null;
            OnLobbyUpdated = null;
            OnPlayerJoined = null;
            OnPlayerLeft = null;
            OnPlayerUpdated = null;
            OnPlayerKicked = null;
            OnGameStarted = null;
        }

        /// <summary>
        /// Debugging: log current number of subscribers for each event
        /// </summary>
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
                $"\n  OnGameStarted: {OnGameStarted?.GetInvocationList()?.Length ?? 0}");
        }
    }
}
using System;
using Unity.Services.Lobbies.Models;

namespace _GAME.Scripts.Lobbies
{
    /// <summary>
    /// Static event system để giao tiếp giữa các component trong game
    /// Sử dụng khi bạn không muốn tham chiếu trực tiếp đến LobbyManager
    /// </summary>
    public static class LobbyEvents
    {
        // Lobby Events
        public static event Action<LobbyEventData> OnLobbyCreated;
        public static event Action<LobbyEventData> OnLobbyJoined;
        public static event Action<LobbyEventData> OnLobbyLeft;
        public static event Action<LobbyEventData> OnLobbyUpdated;
        public static event Action<LobbyEventData> OnLobbyRemoved;
        public static event Action<string> OnLobbyError;

        // Player Events
        public static event Action<PlayerEventData> OnPlayerJoined;
        public static event Action<PlayerEventData> OnPlayerLeft;
        public static event Action<PlayerEventData> OnPlayerKicked;
        public static event Action<PlayerEventData> OnPlayerUpdated;

        // Game Events
        public static event Action<GameEventData> OnGameStarted;
        public static event Action<GameEventData> OnGameEnded;
        public static event Action<string> OnGameStateChanged;

        // Connection Events
        public static event Action OnConnected;
        public static event Action<string> OnDisconnected;
        public static event Action<string> OnConnectionError;

        #region Trigger Methods

        public static void TriggerLobbyCreated(Lobby lobby, bool success, string message)
        {
            OnLobbyCreated?.Invoke(new LobbyEventData
            {
                Lobby = lobby,
                Success = success,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        public static void TriggerLobbyJoined(Lobby lobby, bool success, string message)
        {
            OnLobbyJoined?.Invoke(new LobbyEventData
            {
                Lobby = lobby,
                Success = success,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        public static void TriggerLobbyLeft(Lobby lobby, bool success, string message)
        {
            OnLobbyLeft?.Invoke(new LobbyEventData
            {
                Lobby = lobby,
                Success = success,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        public static void TriggerLobbyUpdated(Lobby lobby, bool success, string message)
        {
            OnLobbyUpdated?.Invoke(new LobbyEventData
            {
                Lobby = lobby,
                Success = success,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        public static void TriggerLobbyRemoved(Lobby lobby, bool success, string message)
        {
            OnLobbyRemoved?.Invoke(new LobbyEventData
            {
                Lobby = lobby,
                Success = success,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        public static void TriggerLobbyError(string errorMessage)
        {
            OnLobbyError?.Invoke(errorMessage);
        }

        public static void TriggerPlayerJoined(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message = "")
        {
            OnPlayerJoined?.Invoke(new PlayerEventData
            {
                Player = player,
                Lobby = lobby,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        public static void TriggerPlayerLeft(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message = "")
        {
            OnPlayerLeft?.Invoke(new PlayerEventData
            {
                Player = player,
                Lobby = lobby,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        public static void TriggerPlayerKicked(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message = "")
        {
            OnPlayerKicked?.Invoke(new PlayerEventData
            {
                Player = player,
                Lobby = lobby,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        public static void TriggerPlayerUpdated(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message = "")
        {
            OnPlayerUpdated?.Invoke(new PlayerEventData
            {
                Player = player,
                Lobby = lobby,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        public static void TriggerGameStarted(Lobby lobby, string gameMode = "")
        {
            OnGameStarted?.Invoke(new GameEventData
            {
                Lobby = lobby,
                GameMode = gameMode,
                Timestamp = DateTime.Now
            });
        }

        public static void TriggerGameEnded(Lobby lobby, string reason = "")
        {
            OnGameEnded?.Invoke(new GameEventData
            {
                Lobby = lobby,
                Reason = reason,
                Timestamp = DateTime.Now
            });
        }

        public static void TriggerGameStateChanged(string newState)
        {
            OnGameStateChanged?.Invoke(newState);
        }

        public static void TriggerConnected()
        {
            OnConnected?.Invoke();
        }

        public static void TriggerDisconnected(string reason)
        {
            OnDisconnected?.Invoke(reason);
        }

        public static void TriggerConnectionError(string error)
        {
            OnConnectionError?.Invoke(error);
        }

        #endregion

        #region Cleanup Methods

        /// <summary>
        /// Clear tất cả event subscriptions - sử dụng khi cần reset
        /// </summary>
        public static void ClearAllEvents()
        {
            OnLobbyCreated = null;
            OnLobbyJoined = null;
            OnLobbyLeft = null;
            OnLobbyUpdated = null;
            OnLobbyRemoved = null;
            OnLobbyError = null;

            OnPlayerJoined = null;
            OnPlayerLeft = null;
            OnPlayerKicked = null;
            OnPlayerUpdated = null;

            OnGameStarted = null;
            OnGameEnded = null;
            OnGameStateChanged = null;

            OnConnected = null;
            OnDisconnected = null;
            OnConnectionError = null;
        }

        /// <summary>
        /// Clear lobby events only
        /// </summary>
        public static void ClearLobbyEvents()
        {
            OnLobbyCreated = null;
            OnLobbyJoined = null;
            OnLobbyLeft = null;
            OnLobbyUpdated = null;
            OnLobbyRemoved = null;
            OnLobbyError = null;
        }

        /// <summary>
        /// Clear player events only
        /// </summary>
        public static void ClearPlayerEvents()
        {
            OnPlayerJoined = null;
            OnPlayerLeft = null;
            OnPlayerKicked = null;
            OnPlayerUpdated = null;
        }

        /// <summary>
        /// Clear game events only
        /// </summary>
        public static void ClearGameEvents()
        {
            OnGameStarted = null;
            OnGameEnded = null;
            OnGameStateChanged = null;
        }

        #endregion
    }

    #region Event Data Classes

    [System.Serializable]
    public class LobbyEventData
    {
        public Lobby Lobby;
        public bool Success;
        public string Message;
        public DateTime Timestamp;
    }

    [System.Serializable]
    public class PlayerEventData
    {
        public Unity.Services.Lobbies.Models.Player Player;
        public Lobby Lobby;
        public string Message;
        public DateTime Timestamp;
    }

    [System.Serializable]
    public class GameEventData
    {
        public Lobby Lobby;
        public string GameMode;
        public string Reason;
        public DateTime Timestamp;
    }

    #endregion
}
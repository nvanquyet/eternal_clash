using _GAME.Scripts.Networking.Lobbies;
using GAME.Scripts.DesignPattern;
using UnityEngine;
using UnityEngine.Serialization;

namespace _GAME.Scripts.Config
{
    #region Phases
        
    /// <summary>
    /// Game phase values used to track lobby state
    /// </summary>
    public static class SessionPhase
    {
        public const string WAITING = "Waiting";
        public const string STARTING = "Starting";
        public const string PLAYING = "Playing";
        public const string FINISHED = "Finished";
        public const string CANCELLED = "Cancelled";
    }
        
    #endregion
    public class GameConfig : SingletonDontDestroy<GameConfig>
    {
        [Header("Lobby Configuration")] 
        public int[] maxPlayersPerLobby = { 4, 6, 8, 10 };
        
        public string defaultNameLobby = "Lobby";
        public int DefaultMaxPlayer => (maxPlayersPerLobby != null && maxPlayersPerLobby.Length > 0) ? maxPlayersPerLobby[0] : 4;
        public string defaultPassword = "12345678";
    }
}
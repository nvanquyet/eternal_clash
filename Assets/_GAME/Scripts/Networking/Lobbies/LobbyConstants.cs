// LobbyConstants.cs
using System;
using _GAME.Scripts.Config;
using _GAME.Scripts.HideAndSeek;

namespace _GAME.Scripts.Networking.Lobbies
{
    /// <summary>
    /// Centralized constants for lobby system to avoid magic strings and ensure consistency
    /// </summary>
    public static class LobbyConstants
    {
        public enum MaxPlayerLobby
        {
            Four = 4,
            Six = 6,
            Eight = 8,
            Ten = 10
        }
        
        #region Lobby Data Keys
        
        public static class LobbyKeys
        {
            public const string CODE  = "CODE";
            public const string PHASE = "PHASE"; 
        }

        /// <summary>
        /// Keys used in Lobby.Data dictionary
        /// </summary>
        public static class LobbyData
        {
            public const string PASSWORD = "Password";
            public const string PHASE = "Phase"; // Key CHUẨN dùng cho phase
            public const string RELAY_JOIN_CODE = "RelayJoinCode";
            //public const string RELAY_ALLOCATION_ID = "RelayAllocationId";
            public const string GAME_MODE = "GameMode";
        }
        
        #endregion

        #region Player Data Keys
        
        public static class PlayerData
        {
            public const string DISPLAY_NAME = "DisplayName";
            public const string IS_READY = "IsReady";
            public const string PLAYER_ROLE = "PlayerRole";
            public const string TEAM_ID = "TeamId";
        }
        
        #endregion

        #region Player Roles
        
        public static class PlayerRoles
        {
            public const string NONE = "Player";
            public const string HIDER = "Hider";
            public const string SEEKER = "Seeker";
        }
        
        #endregion

        #region Default Values
        
        public static class Defaults
        {
            public const string READY_FALSE = "false";
            public const string READY_TRUE = "true";
            
            public const string DEFAULT_LOBBY_NAME = "New Lobby";
            public const string DEFAULT_GAME_MODE = "PersonVsPerson";
            
            public const string UNKNOWN_PLAYER = "Unknown Player";
            public const string DEFAULT_PLAYER_ROLE = PlayerRoles.NONE;
        }
        
        #endregion

        #region Validation Limits
        
        public static class Validation
        {
            public const int MIN_PASSWORD_LENGTH = 4;
            public const int MAX_PASSWORD_LENGTH = 20;
            public const int MAX_LOBBY_NAME_LENGTH = 50;
            public const int MIN_LOBBY_NAME_LENGTH = 1;
            public const int MAX_DISPLAY_NAME_LENGTH = 20;
            public const int MIN_DISPLAY_NAME_LENGTH = 1;
            public const int MAX_PLAYERS = 16;
            public const int MIN_PLAYERS = 4;
        }
        
        #endregion

        #region Utility Methods

        private static bool IsValidLobbyName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && 
                   name.Length >= Validation.MIN_LOBBY_NAME_LENGTH && 
                   name.Length <= Validation.MAX_LOBBY_NAME_LENGTH;
        }

        private static bool IsValidDisplayName(string displayName)
        {
            return !string.IsNullOrWhiteSpace(displayName) && 
                   displayName.Length >= Validation.MIN_DISPLAY_NAME_LENGTH && 
                   displayName.Length <= Validation.MAX_DISPLAY_NAME_LENGTH;
        }
        
        public static bool IsValidPassword(string password)
        {
            return string.IsNullOrEmpty(password) || 
                   (password.Length >= Validation.MIN_PASSWORD_LENGTH && 
                    password.Length <= Validation.MAX_PASSWORD_LENGTH);
        }
        
        public static bool IsValidMaxPlayers(int maxPlayers)
        {
            return maxPlayers >= Validation.MIN_PLAYERS && maxPlayers <= Validation.MAX_PLAYERS;
        }
        
        public static bool IsValidPhase(string phase)
        {
            return phase is SessionPhase.WAITING or SessionPhase.STARTING or SessionPhase.PLAYING or SessionPhase.FINISHED or SessionPhase.CANCELLED;
        }
        
        public static string GetSafeLobbyName(string name)
        {
            return IsValidLobbyName(name) ? name : Defaults.DEFAULT_LOBBY_NAME;
        }
        
        public static string GetSafeDisplayName(string displayName, string fallbackId = null)
        {
            if (IsValidDisplayName(displayName))
                return displayName;
                
            if (!string.IsNullOrEmpty(fallbackId))
                return $"Player_{fallbackId[..Math.Min(6, fallbackId.Length)]}";
                
            return Defaults.UNKNOWN_PLAYER;
        }
        
        #endregion
    }
}

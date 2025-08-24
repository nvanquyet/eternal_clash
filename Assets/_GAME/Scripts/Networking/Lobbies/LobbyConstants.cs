using System;

namespace _GAME.Scripts.Networking.Lobbies
{
    /// <summary>
    /// Centralized constants for lobby system to avoid magic strings and ensure consistency
    /// </summary>
    public static class LobbyConstants
    {
        #region Lobby Data Keys
        
        /// <summary>
        /// Keys used in Lobby.Data dictionary
        /// </summary>
        public static class LobbyData
        {
            public const string PASSWORD = "Password";
            public const string PHASE = "Phase";
            public const string RELAY_JOIN_CODE = "RelayJoinCode";
            public const string RELAY_ALLOCATION_ID = "RelayAllocationId";
            public const string NETWORK_STATUS = "NetworkStatus";
            public const string GAME_MODE = "GameMode";
            public const string MAP_NAME = "MapName";
            public const string CREATED_AT = "CreatedAt";
        }
        
        #endregion

        #region Player Data Keys
        
        /// <summary>
        /// Keys used in Player.Data dictionary
        /// </summary>
        public static class PlayerData
        {
            // Current format
            public const string DISPLAY_NAME = "DisplayName";
            public const string IS_READY = "IsReady";
            public const string PLAYER_ROLE = "PlayerRole";
            public const string TEAM_ID = "TeamId";
            public const string PLAYER_LEVEL = "PlayerLevel";
            
            // Legacy keys for backwards compatibility
            public const string LEGACY_NAME = "name";
            public const string LEGACY_READY = "ready";
        }
        
        #endregion

        #region Network Status Values
        
        /// <summary>
        /// Network status values used in lobby data
        /// </summary>
        public static class NetworkStatus
        {
            public const string NONE = "None";
            public const string CONNECTING = "Connecting";
            public const string CONNECTED = "Connected";
            public const string READY = "Ready";
            public const string FAILED = "Failed";
            public const string DISCONNECTED = "Disconnected";
        }
        
        #endregion

        #region Lobby Phases
        
        /// <summary>
        /// Game phase values used to track lobby state
        /// </summary>
        public static class Phases
        {
            public const string WAITING = "Waiting";
            public const string STARTING = "Starting";
            public const string PLAYING = "Playing";
            public const string FINISHED = "Finished";
            public const string CANCELLED = "Cancelled";
        }
        
        #endregion

        #region Player Roles
        
        /// <summary>
        /// Player role values for team-based games
        /// </summary>
        public static class PlayerRoles
        {
            public const string PLAYER = "Player";
            public const string SPECTATOR = "Spectator";
            public const string MODERATOR = "Moderator";
        }
        
        #endregion

        #region Default Values
        
        /// <summary>
        /// Default values used throughout the lobby system
        /// </summary>
        public static class Defaults
        {
            // Ready status
            public const string READY_FALSE = "false";
            public const string READY_TRUE = "true";
            
            // Legacy ready status
            public const string LEGACY_READY_FALSE = "0";
            public const string LEGACY_READY_TRUE = "1";
            
            // Lobby settings
            public const int MAX_PLAYERS = 4;
            public const int MIN_PLAYERS = 1;
            public const string DEFAULT_LOBBY_NAME = "New Lobby";
            public const string DEFAULT_GAME_MODE = "Classic";
            
            // Player settings
            public const string UNKNOWN_PLAYER = "Unknown Player";
            public const string DEFAULT_PLAYER_ROLE = PlayerRoles.PLAYER;
            public const int DEFAULT_TEAM_ID = 0;
            public const int DEFAULT_PLAYER_LEVEL = 1;
        }
        
        #endregion

        #region Validation Limits
        
        /// <summary>
        /// Validation limits for various inputs
        /// </summary>
        public static class Validation
        {
            // Text lengths
            public const int MIN_PASSWORD_LENGTH = 4;
            public const int MAX_PASSWORD_LENGTH = 20;
            public const int MAX_LOBBY_NAME_LENGTH = 50;
            public const int MIN_LOBBY_NAME_LENGTH = 1;
            public const int MAX_DISPLAY_NAME_LENGTH = 20;
            public const int MIN_DISPLAY_NAME_LENGTH = 1;
            
            // Player limits
            public const int MAX_PLAYERS = 8;
            public const int MIN_PLAYERS_FOR_START = 1;
            
            // Timeouts (in seconds)
            public const float LOBBY_HEARTBEAT_INTERVAL = 15f;
            public const float LOBBY_UPDATE_INTERVAL = 2f;
            public const float NETWORK_TIMEOUT = 30f;
            public const float RELAY_CODE_WAIT_TIMEOUT = 20f;
            
            // Retry settings
            public const int MAX_RETRY_ATTEMPTS = 3;
            public const float RETRY_DELAY = 2f;
        }
        
        #endregion

        #region Error Messages
        
        /// <summary>
        /// Standard error messages for consistency
        /// </summary>
        public static class ErrorMessages
        {
            // Service errors
            public const string SERVICE_NOT_INITIALIZED = "Service not initialized";
            public const string USER_NOT_AUTHENTICATED = "User not authenticated";
            public const string NETWORK_MANAGER_NOT_FOUND = "NetworkManager not found";
            
            // Lobby errors
            public const string LOBBY_NOT_FOUND = "Lobby not found";
            public const string LOBBY_FULL = "Lobby is full";
            public const string WRONG_PASSWORD = "Incorrect password";
            public const string NOT_HOST = "Only host can perform this action";
            public const string ALREADY_IN_LOBBY = "Already in a lobby";
            
            // Network errors
            public const string CONNECTION_FAILED = "Failed to connect to network";
            public const string CONNECTION_TIMEOUT = "Connection timeout";
            public const string RELAY_SETUP_FAILED = "Relay setup failed";
            public const string HOST_SETUP_FAILED = "Host setup failed";
            
            // Validation errors
            public const string INVALID_LOBBY_NAME = "Invalid lobby name";
            public const string INVALID_PASSWORD = "Invalid password";
            public const string INVALID_PLAYER_NAME = "Invalid player name";
            public const string INVALID_PLAYER_COUNT = "Invalid player count";
        }
        
        #endregion

        #region Unity Services Limits
        
        /// <summary>
        /// Unity Gaming Services specific limits
        /// </summary>
        public static class UGSLimits
        {
            // Lobby limits
            public const int MAX_LOBBY_NAME_LENGTH = 30;
            public const int MAX_LOBBY_PASSWORD_LENGTH = 30;
            public const int MAX_PLAYERS_PER_LOBBY = 100;
            
            // Data limits
            public const int MAX_LOBBY_DATA_ENTRIES = 5;
            public const int MAX_PLAYER_DATA_ENTRIES = 5;
            public const int MAX_DATA_VALUE_LENGTH = 300;
            
            // Rate limits
            public const int LOBBIES_PER_MINUTE = 10;
            public const int JOINS_PER_MINUTE = 20;
            public const int UPDATES_PER_MINUTE = 30;
        }
        
        #endregion

        #region Utility Methods
        
        /// <summary>
        /// Validate lobby name according to our rules
        /// </summary>
        public static bool IsValidLobbyName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && 
                   name.Length >= Validation.MIN_LOBBY_NAME_LENGTH && 
                   name.Length <= Validation.MAX_LOBBY_NAME_LENGTH;
        }
        
        /// <summary>
        /// Validate player display name
        /// </summary>
        public static bool IsValidDisplayName(string displayName)
        {
            return !string.IsNullOrWhiteSpace(displayName) && 
                   displayName.Length >= Validation.MIN_DISPLAY_NAME_LENGTH && 
                   displayName.Length <= Validation.MAX_DISPLAY_NAME_LENGTH;
        }
        
        /// <summary>
        /// Validate password
        /// </summary>
        public static bool IsValidPassword(string password)
        {
            // Empty password is valid (no password)
            return string.IsNullOrEmpty(password) || 
                   (password.Length >= Validation.MIN_PASSWORD_LENGTH && 
                    password.Length <= Validation.MAX_PASSWORD_LENGTH);
        }
        
        /// <summary>
        /// Validate max player count
        /// </summary>
        public static bool IsValidMaxPlayers(int maxPlayers)
        {
            return maxPlayers >= Defaults.MIN_PLAYERS && maxPlayers <= Validation.MAX_PLAYERS;
        }
        
        /// <summary>
        /// Check if phase is valid
        /// </summary>
        public static bool IsValidPhase(string phase)
        {
            return phase == Phases.WAITING || 
                   phase == Phases.STARTING || 
                   phase == Phases.PLAYING || 
                   phase == Phases.FINISHED || 
                   phase == Phases.CANCELLED;
        }
        
        /// <summary>
        /// Check if network status is valid
        /// </summary>
        public static bool IsValidNetworkStatus(string status)
        {
            return status == NetworkStatus.NONE || 
                   status == NetworkStatus.CONNECTING || 
                   status == NetworkStatus.CONNECTED || 
                   status == NetworkStatus.READY || 
                   status == NetworkStatus.FAILED || 
                   status == NetworkStatus.DISCONNECTED;
        }
        
        /// <summary>
        /// Get safe lobby name (fallback to default if invalid)
        /// </summary>
        public static string GetSafeLobbyName(string name)
        {
            return IsValidLobbyName(name) ? name : Defaults.DEFAULT_LOBBY_NAME;
        }
        
        /// <summary>
        /// Get safe display name (fallback to default if invalid)
        /// </summary>
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
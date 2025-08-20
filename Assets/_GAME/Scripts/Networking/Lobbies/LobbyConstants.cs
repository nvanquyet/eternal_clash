namespace _GAME.Scripts.Networking.Lobbies
{
    /// <summary>
    /// Contains all constant keys used in lobby system to avoid magic strings
    /// </summary>
    public static class LobbyConstants
    {
        // ========== LOBBY DATA KEYS ==========
        public static class LobbyData
        {
            public const string PASSWORD = "Password";
            public const string PHASE = "Phase";
            public const string RELAY_JOIN_CODE = "RelayJoinCode";
            public const string RELAY_ALLOCATION_ID = "RelayAllocationId";
            public const string NETWORK_STATUS = "NetworkStatus";
        }
        
        // Thêm Network status constants
        public static class NetworkStatus
        {
            public const string NONE = "None";
            public const string READY = "Ready";
            public const string CONNECTING = "Connecting"; 
            public const string CONNECTED = "Connected";
            public const string FAILED = "Failed";
        }

        // ========== PLAYER DATA KEYS ==========
        public static class PlayerData
        {
            public const string DISPLAY_NAME = "DisplayName";
            public const string IS_READY = "IsReady";
            
            // Legacy keys for backwards compatibility
            public const string LEGACY_NAME = "name";
            public const string LEGACY_READY = "ready";
        }

        // ========== LOBBY PHASES ==========
        public static class Phases
        {
            public const string WAITING = "Waiting";
            public const string PLAYING = "Playing";
            public const string FINISHED = "Finished";
        }

        // ========== DEFAULT VALUES ==========
        public static class Defaults
        {
            public const string READY_FALSE = "false";
            public const string READY_TRUE = "true";
            public const string LEGACY_READY_FALSE = "0";
            public const string LEGACY_READY_TRUE = "1";
            public const int MAX_PLAYERS = 4;
            public const string UNKNOWN_PLAYER = "Unknown Player";
            public const string DEFAULT_LOBBY_NAME = "Lobby";
            public const int MIN_PASSWORD_LENGTH = 8;
        }

        // ========== VALIDATION ==========
        public static class Validation
        {
            public const int MIN_PASSWORD_LENGTH = 8;
            public const int MAX_LOBBY_NAME_LENGTH = 50;
            public const int MAX_DISPLAY_NAME_LENGTH = 20;
        }
    }
}
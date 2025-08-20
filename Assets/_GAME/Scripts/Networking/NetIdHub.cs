// _GAME/Scripts/Networking/NetIdHub.cs

using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;
using _GAME.Scripts.Networking.Relay; // để dùng extension GetRelayJoinCode()
using _GAME.Scripts.Networking.Lobbies;

namespace _GAME.Scripts.Networking
{
    /// <summary>
    /// Trung tâm đồng bộ và cung cấp ID (PlayerId/LobbyId/LobbyCode/HostId/RelayJoinCode/LocalClientId).
    /// Không gọi SDK trực tiếp bên ngoài; mọi nơi khác chỉ đọc từ đây.
    /// </summary>
    public static class NetIdHub
    {
        // --- Sources ---
        public static string PlayerId => AuthenticationService.Instance?.PlayerId;

        public static ulong LocalClientId =>
            NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;

        // --- State synced from LobbyEvents ---
        public static string LobbyId { get; private set; }
        public static string LobbyCode { get; private set; }
        public static string HostId { get; private set; }
        public static string RelayJoinCode { get; private set; }

        private static bool _wired;

        /// <summary>Gắn sự kiện 1 lần ở game start (vd: trong NetSessionManager.OnEnable)</summary>
        public static void Wire()
        {
            if (_wired) return;
            _wired = true;

            LobbyEvents.OnLobbyCreated += OnLobbyCreatedOrJoined;
            LobbyEvents.OnLobbyJoined += OnLobbyCreatedOrJoined;
            LobbyEvents.OnLobbyUpdated += (lobby, _) => SyncFromLobby(lobby);
            LobbyEvents.OnLobbyLeft += (_, ok, __) =>
            {
                if (ok) Clear();
            };
            LobbyEvents.OnLobbyRemoved += (_, ok, __) =>
            {
                if (ok) Clear();
            };
        }

        private static void OnLobbyCreatedOrJoined(Lobby lobby, bool ok, string _)
        {
            if (ok) SyncFromLobby(lobby);
        }

        private static void SyncFromLobby(Lobby lobby)
        {
            if (lobby == null) return;
            LobbyId = lobby.Id;
            LobbyCode = lobby.LobbyCode;
            HostId = lobby.HostId;
            RelayJoinCode = lobby.GetRelayJoinCode(); // đọc từ Lobby.Data qua RelayExtension
        }

        public static void BindLobby(Lobby lobby) => SyncFromLobby(lobby);
        public static void SetRelayJoinCode(string code) => RelayJoinCode = code;
        public static bool IsLocalHost() => !string.IsNullOrEmpty(HostId) && HostId == PlayerId;

        public static void Clear()
        {
            LobbyId = LobbyCode = HostId = RelayJoinCode = null;
        }
    }
}
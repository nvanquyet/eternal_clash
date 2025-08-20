using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;

namespace _GAME.Scripts.Networking.Relay
{
    /// <summary>
    /// Lớp tiện ích thuần NGO: StartHost/StartClient (sau khi RelayConnector đã set transport).
    /// Không thao tác Lobby trực tiếp (để LobbyExtensions đảm nhận).
    /// </summary>
    public static class NetworkStarter
    {
        public static bool StartHost()  => NetworkManager.Singleton.StartHost();
        public static bool StartClient()=> NetworkManager.Singleton.StartClient();

        /// <summary>
        /// Host: tạo Relay, báo joinCode qua callback (để LobbyExtensions set vào Lobby.Data), rồi StartHost.
        /// </summary>
        public static async Task<bool> HostWithRelayAsync(int maxPlayers, Func<string, Task> onJoinCodeReady)
        {
            var maxClients = Math.Max(0, maxPlayers - 1);
            var (_, joinCode) = await RelayConnector.AllocateHostAsync(maxClients);

            NetIdHub.SetRelayJoinCode(joinCode);
            
            if (onJoinCodeReady != null)
                await onJoinCodeReady(joinCode);

            return StartHost();
        }

        /// <summary>
        /// Client: Join Relay từ joinCode (lấy joinCode ở nơi khác), rồi StartClient.
        /// </summary>
        public static async Task<bool> ClientWithRelayAsync(string joinCode)
        {
            await RelayConnector.JoinAsClientAsync(joinCode);
            return StartClient();
        }
    }
}
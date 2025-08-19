// Networking/Relay/RelayConnector.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

namespace _GAME.Scripts.Networking.Relay
{
    public static class RelayConnector
    {
         private static UnityTransport GetTransport()
        {
            var utp = NetworkManager.Singleton?.NetworkConfig?.NetworkTransport as UnityTransport;
            if (utp == null) throw new Exception("UnityTransport not found on NetworkManager.");
            return utp;
        }

        private static RelayServerEndpoint PickEndpoint(System.Collections.Generic.IList<RelayServerEndpoint> endpoints, string preferred = "dtls")
        {
            if (endpoints == null || endpoints.Count == 0)
                throw new Exception("Relay endpoints not available.");

            // Ưu tiên endpoint theo ConnectionType (vd: dtls), fallback cái đầu tiên
            var ep = endpoints.FirstOrDefault(e => string.Equals(e.ConnectionType, preferred, StringComparison.OrdinalIgnoreCase))
                     ?? endpoints[0];
            return ep;
        }

        /// <summary>
        /// Host tạo Allocation và set transport. Trả về (Allocation, JoinCode).
        /// </summary>
        public static async Task<(Allocation allocation, string joinCode)> AllocateHostAsync(int maxClientConnections)
        {
            var alloc = await RelayService.Instance.CreateAllocationAsync(Math.Max(0, maxClientConnections));
            var code  = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

            var ep = PickEndpoint(alloc.ServerEndpoints, "dtls"); // hoặc "udp" nếu muốn
            var serverData = new RelayServerData(
                ep.Host,
                (ushort)ep.Port,
                alloc.AllocationIdBytes,
                alloc.ConnectionData,   // connectionData
                alloc.ConnectionData,   // hostConnectionData (host dùng chính connectionData của mình)
                alloc.Key,
                ep.Secure               // true nếu dtls
            );

            var utp = GetTransport();
            utp.SetRelayServerData(serverData);

            return (alloc, code);
        }

        /// <summary>
        /// Client join theo JoinCode và set transport.
        /// </summary>
        public static async Task<JoinAllocation> JoinAsClientAsync(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
                throw new ArgumentException("JoinCode is null or empty.", nameof(joinCode));

            var join = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var ep = PickEndpoint(join.ServerEndpoints, "dtls"); // hoặc "udp"
            var serverData = new RelayServerData(
                ep.Host,
                (ushort)ep.Port,
                join.AllocationIdBytes,
                join.ConnectionData,     // client connectionData
                join.HostConnectionData, // hostConnectionData (quan trọng ở client)
                join.Key,
                ep.Secure
            );

            var utp = GetTransport();
            utp.SetRelayServerData(serverData);

            return join;
        }
    }
}
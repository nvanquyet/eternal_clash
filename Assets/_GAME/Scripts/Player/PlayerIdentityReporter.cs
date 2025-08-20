using _GAME.Scripts.Networking;
using Unity.Netcode;
using Unity.Services.Authentication;

namespace _GAME.Scripts.Player
{

    public class PlayerIdentityReporter : NetworkBehaviour
    {
        public override void OnNetworkSpawn()
        {
            // Chủ sở hữu player object gửi UGS PlayerId lên server
            if (IsOwner && IsClient)
            {
                var pid = AuthenticationService.Instance?.PlayerId;
                if (!string.IsNullOrEmpty(pid))
                    RegisterIdentityServerRpc(pid);
            }

            // Host (server) cũng tự đăng ký cho chính mình
            if (IsServer && OwnerClientId == NetworkManager.Singleton.LocalClientId)
            {
                var pid = AuthenticationService.Instance?.PlayerId;
                if (!string.IsNullOrEmpty(pid))
                    ClientIdentityRegistry.Instance?.Register(OwnerClientId, pid);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void RegisterIdentityServerRpc(string ugsPlayerId, ServerRpcParams rpc = default)
        {
            var senderClientId = rpc.Receive.SenderClientId;
            ClientIdentityRegistry.Instance?.Register(senderClientId, ugsPlayerId);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
                ClientIdentityRegistry.Instance?.UnregisterByClient(OwnerClientId);
        }
    }
}
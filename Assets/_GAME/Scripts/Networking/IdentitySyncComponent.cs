using Unity.Netcode;
using Unity.Services.Authentication;
using UnityEngine;

namespace _GAME.Scripts.Networking
{
    /// <summary>
    /// NetworkBehaviour component that handles identity synchronization between clients and server
    /// Automatically registers UGS Player ID <-> Netcode Client ID mappings
    /// </summary>
    public class IdentitySyncComponent : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool autoSyncOnSpawn = true;
        [SerializeField] private float syncRetryDelay = 1f;
        [SerializeField] private int maxSyncRetries = 5;

        private int _syncAttempts = 0;
        private bool _isSynced = false;

        public override void OnNetworkSpawn()
        {
            if (autoSyncOnSpawn)
            {
                TrySyncIdentity();
            }
        }

        private void TrySyncIdentity()
        {
            if (_isSynced || _syncAttempts >= maxSyncRetries) return;

            var myUgsId = AuthenticationService.Instance?.PlayerId;
            var myClientId = NetworkManager.LocalClientId;

            if (string.IsNullOrEmpty(myUgsId))
            {
                Debug.LogWarning("[IdentitySyncComponent] UGS Player ID not available, will retry");
                _syncAttempts++;
                Invoke(nameof(TrySyncIdentity), syncRetryDelay);
                return;
            }

            if (IsServer)
            {
                // Server registers itself directly
                RegisterIdentityLocal(myUgsId, myClientId);
            }
            else if (IsClient)
            {
                // Client sends identity to server
                SendIdentityToServerRpc(myUgsId, myClientId);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SendIdentityToServerRpc(string ugsPlayerId, ulong clientId, ServerRpcParams rpcParams = default)
        {
            // Validate that the RPC sender matches the claimed client ID
            var senderClientId = rpcParams.Receive.SenderClientId;
            if (senderClientId != clientId)
            {
                Debug.LogWarning($"[IdentitySyncComponent] Identity sync mismatch: claimed {clientId}, actual {senderClientId}");
                return;
            }

            RegisterIdentityLocal(ugsPlayerId, clientId);
            
            // Confirm successful registration back to client
            ConfirmIdentitySyncClientRpc(ugsPlayerId, clientId, new ClientRpcParams 
            { 
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } 
            });
        }

        [ClientRpc]
        private void ConfirmIdentitySyncClientRpc(string ugsPlayerId, ulong clientId, ClientRpcParams rpcParams = default)
        {
            var myClientId = NetworkManager.LocalClientId;
            if (clientId == myClientId)
            {
                _isSynced = true;
                Debug.Log($"[IdentitySyncComponent] Identity sync confirmed: UGS({ugsPlayerId}) <-> Client({clientId})");
            }
        }

        private void RegisterIdentityLocal(string ugsPlayerId, ulong clientId)
        {
            var registry = ClientIdentityRegistry.Instance;
            if (registry != null)
            {
                registry.RegisterMapping(ugsPlayerId, clientId);
                _isSynced = true;
            }
            else
            {
                Debug.LogError("[IdentitySyncComponent] ClientIdentityRegistry not available");
            }
        }

        /// <summary>
        /// Manually trigger identity sync (useful for debugging or special cases)
        /// </summary>
        [ContextMenu("Force Sync Identity")]
        public void ForceSyncIdentity()
        {
            _syncAttempts = 0;
            _isSynced = false;
            TrySyncIdentity();
        }

        /// <summary>
        /// Check if identity is properly synced
        /// </summary>
        public bool IsIdentitySynced()
        {
            return _isSynced;
        }
    }
}
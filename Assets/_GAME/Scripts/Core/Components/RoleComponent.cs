using System;
using _GAME.Scripts.HideAndSeek;
using _GAME.Scripts.HideAndSeek.Config;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Core.Components
{
    [Serializable]
    public struct RoleState : INetworkSerializable
    {
        public Role role;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref role);
        }
    }

    public class RoleComponent : NetworkBehaviour, IPlayerComponent
    {
        [SerializeField] private PlayerRoleSO roleSo;
        private IPlayer _owner;
        private NetworkVariable<RoleState> _roleState = new NetworkVariable<RoleState>(
            new RoleState { role = Role.None },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public Role CurrentRole => _roleState.Value.role;
        public bool IsActive => enabled;

        public event Action<Role, Role> OnRoleChanged;

        public void Initialize(IPlayer owner)
        {
            _owner = owner;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _roleState.OnValueChanged += HandleRoleChanged;
            GameEvent.OnRoleAssigned += RoleAssigned;
        }

        public override void OnNetworkDespawn()
        {
            if (_roleState != null)
                _roleState.OnValueChanged -= HandleRoleChanged;
            GameEvent.OnRoleAssigned -= RoleAssigned;
            base.OnNetworkDespawn();
        }

        private void RoleAssigned()
        {
            var playerRole = GameManager.Instance.GetPlayerRoleWithId(this.OwnerClientId);
            SetRole(playerRole);
        }

        private void SetRole(Role newRole)
        {
            if (IsServer)
            {
                AssignRole(newRole);
            }
            else if (_owner.NetObject.IsOwner)
            {
                RequestRoleServerRpc(newRole);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestRoleServerRpc(Role newRole, ServerRpcParams rpc = default)
        {
            AssignRole(newRole);
        }

        private void AssignRole(Role newRole)
        {
            if (!IsServer) return;

            var state = _roleState.Value;
            if (state.role == newRole) return;

            state.role = newRole;
            _roleState.Value = state;
            
            //Spawn role object
            SpawnRoleObject(newRole);
        }

        private void HandleRoleChanged(RoleState oldState, RoleState newState)
        {
            OnRoleChanged?.Invoke(oldState.role, newState.role);
        }

        private void SpawnRoleObject(Role newRole)
        {
            if (!IsServer)
            {
                Debug.LogError($"❌ [RoleComponent] This should only run on server!");
                return;
            }

            if (roleSo == null)
            {
                Debug.LogError($"❌ [RoleComponent] PlayerRoleSO is null!");
                return;
            }

            var roleData = roleSo.GetData(newRole);

            var prefab = roleData.Prefab;
            if (prefab == null)
            {
                Debug.LogError($"❌ [RoleComponent] No prefab found for role {newRole}!");
                return;
            }

            // Check if prefab has NetworkObject
            if (prefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError($"❌ [RoleComponent] Prefab {prefab.name} doesn't have NetworkObject component!");
                return;
            }

            var gO = Instantiate(prefab);

            if (gO == null)
            {
                Debug.LogError($"❌ [RoleComponent] Failed to instantiate prefab!");
                return;
            }

            gO.OnNetworkSpawned += () =>
            {
                gO.NetworkObject.TrySetParent(transform, false);
                gO.transform.localPosition = Vector3.zero;
                gO.SetRole(newRole);
            };
            
            try
            {
                // Spawn with ownership
                gO.NetworkObject.SpawnWithOwnership(OwnerClientId);
                gO.SetRole(newRole);
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ [RoleComponent] Exception during spawn: {e.Message}");
                Debug.LogError($"❌ [RoleComponent] Stack trace: {e.StackTrace}");
                if (gO != null)
                {
                    Destroy(gO);
                }
            }
        }
    }
}
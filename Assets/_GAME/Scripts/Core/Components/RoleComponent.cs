using System;
using _GAME.Scripts.HideAndSeek;
using Unity.Netcode;

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
        private IPlayer _owner;
        private NetworkVariable<RoleState> _roleState;

        public Role CurrentRole => _roleState.Value.role;
        public bool IsActive => enabled;

        public event Action<Role, Role> OnRoleChanged;

        public void Initialize(IPlayer owner)
        {
            _owner = owner;
            _roleState = new NetworkVariable<RoleState>(
                new RoleState { role = Role.None },
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _roleState.OnValueChanged += HandleRoleChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (_roleState != null)
                _roleState.OnValueChanged -= HandleRoleChanged;
            base.OnNetworkDespawn();
        }

        public void SetRole(Role newRole)
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
        }

        private void HandleRoleChanged(RoleState oldState, RoleState newState)
        {
            OnRoleChanged?.Invoke(oldState.role, newState.role);
        }
    }

}
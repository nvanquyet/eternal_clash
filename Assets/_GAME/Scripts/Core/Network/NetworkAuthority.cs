// Base class xử lý tất cả network boilerplate

using Unity.Netcode;

namespace _GAME.Scripts.Core.Network
{
    /// <summary>
    /// Base class handling all network authority boilerplate
    /// Eliminates repetitive IsServer/IsOwner checks
    /// </summary>
    public abstract class NetworkAuthority<TState> : NetworkBehaviour
        where TState : unmanaged, INetworkSerializable
    {
        protected NetworkVariable<TState> State { get; private set; }

        protected virtual void Awake()
        {
            State = new NetworkVariable<TState>(
                GetDefaultState(),
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            State.OnValueChanged += OnStateChanged;

            if (IsServer)
            {
                InitializeServerState();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (State != null)
                State.OnValueChanged -= OnStateChanged;
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Thread-safe state update - automatically routes to server
        /// </summary>
        protected void UpdateState(TState newState)
        {
            if (IsServer)
            {
                if (ValidateStateChange(State.Value, newState))
                    State.Value = newState;
            }
            else if (IsOwner)
            {
                RequestStateChangeServerRpc(newState);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestStateChangeServerRpc(TState newState, ServerRpcParams rpc = default)
        {
            if (!ValidateRequest(rpc.Receive.SenderClientId, State.Value, newState))
                return;

            State.Value = newState;
        }

        // Abstract methods for customization
        protected abstract TState GetDefaultState();
        protected abstract void InitializeServerState();
        protected abstract bool ValidateStateChange(TState oldState, TState newState);
        protected abstract bool ValidateRequest(ulong senderId, TState oldState, TState newState);
        protected abstract void OnStateChanged(TState oldState, TState newState);
    }
    
    
    /// <summary>
    /// Simplified version for single-value network variables
    /// </summary>
    public abstract class NetworkValue<T> : NetworkBehaviour where T : unmanaged
    {
        private NetworkVariable<T> _value;

        protected T Value
        {
            get => _value.Value;
            set
            {
                if (IsServer) _value.Value = value;
                else if (IsOwner) SetValueServerRpc(value);
            }
        }

        protected virtual void Awake()
        {
            _value = new NetworkVariable<T>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _value.OnValueChanged += OnValueChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (_value != null)
                _value.OnValueChanged -= OnValueChanged;
            base.OnNetworkDespawn();
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetValueServerRpc(T newValue, ServerRpcParams rpc = default)
        {
            if (ValidateChange(rpc.Receive.SenderClientId, newValue))
                _value.Value = newValue;
        }

        protected virtual bool ValidateChange(ulong senderId, T newValue) => true;
        protected virtual void OnValueChanged(T oldValue, T newValue) { }
    }
}
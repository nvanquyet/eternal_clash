using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _GAME.Scripts.Controller;
using _GAME.Scripts.UI;
using _GAME.Scripts.UI.Base;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Networking.StateMachine
{
    public enum NetworkState
    {
        Default,             // Default state, no active connection
    
        ClientConnecting, // Connecting to Host/Server
        Connected,        // Successfully connected
    
        Disconnecting,    // Actively disconnecting
        Disconnected,     // Fully disconnected
    
        Failed            // Connection error (timeout, lost connection, host closed, etc.)
    }

    public interface INetworkState
    {
        NetworkState State { get; }
        Task OnEnterAsync(NetworkStateManager manager, object context = null);
        Task OnExitAsync(NetworkStateManager manager);
        bool CanTransitionTo(NetworkState targetState);
    }

    public abstract class NetworkStateBase : INetworkState
    {
        public abstract NetworkState State { get; }

        public virtual async Task OnEnterAsync(NetworkStateManager manager, object context = null)
        {
            await Task.Yield();
            Debug.Log($"[NetworkState] Enter: {State}");
        }

        public virtual async Task OnExitAsync(NetworkStateManager manager)
        {
            await Task.Yield();
            Debug.Log($"[NetworkState] Exit: {State}");
        }

        public virtual bool CanTransitionTo(NetworkState targetState)
        {
            return targetState == NetworkState.Failed || targetState == NetworkState.Default || targetState == NetworkState.Disconnected;
        }
    }

    public class NetworkStateMachine : IDisposable
    {
        private readonly NetworkStateManager _manager;
        private readonly Dictionary<NetworkState, INetworkState> _states;
        private INetworkState _currentState;

        public INetworkState CurrentStateInstance => _currentState;
        public NetworkState CurrentState => _currentState?.State ?? NetworkState.Default;

        public event Action<NetworkState, NetworkState> OnStateChanged;

        public NetworkStateMachine(NetworkStateManager manager)
        {
            _manager = manager;
            _states = CreateStates();
        }

        private Dictionary<NetworkState, INetworkState> CreateStates()
        {
            return new Dictionary<NetworkState, INetworkState>
            {
                { NetworkState.Default, new DefaultNetworkState() },
                { NetworkState.ClientConnecting, new ClientConnectingState() },
                { NetworkState.Connected, new ConnectedState() },
                { NetworkState.Disconnecting, new DisconnectingState() },
                { NetworkState.Disconnected, new DisconnectedState() },
                { NetworkState.Failed, new FailedNetworkState() }
            };
        }

        public bool TransitionTo(NetworkState targetState)
        {
            if (_currentState == null || _currentState.CanTransitionTo(targetState))
                return TransitionToInternal(targetState);
            
            Debug.LogWarning($"[NetworkStateMachine] Invalid transition: {_currentState.State} → {targetState}");
            return false;

        }

        public async Task<bool> TransitionToAsync(NetworkState targetState, object context = null)
        {
            if (_currentState != null && !_currentState.CanTransitionTo(targetState))
            {
                Debug.LogWarning($"[NetworkStateMachine] Invalid transition: {_currentState.State} → {targetState}");
                return false;
            }

            return await TransitionToInternalAsync(targetState, context);
        }

        private bool TransitionToInternal(NetworkState targetState)
        {
            if (!_states.TryGetValue(targetState, out var newState))
            {
                Debug.LogError($"[NetworkStateMachine] State not found: {targetState}");
                return false;
            }

            _currentState?.OnExitAsync(_manager);
            var oldState = _currentState?.State ?? NetworkState.Default;
            _currentState = newState;
            _currentState.OnEnterAsync(_manager);
            OnStateChanged?.Invoke(oldState, targetState);
            return true;
        }

        private async Task<bool> TransitionToInternalAsync(NetworkState targetState, object context = null)
        {
            if (!_states.TryGetValue(targetState, out var newState))
            {
                Debug.LogError($"[NetworkStateMachine] State not found: {targetState}");
                return false;
            }

            if (_currentState != null)
                await _currentState.OnExitAsync(_manager);

            var oldState = _currentState?.State ?? NetworkState.Default;
            _currentState = newState;
            await _currentState.OnEnterAsync(_manager, context);
            OnStateChanged?.Invoke(oldState, targetState);
            return true;
        }

        public void Dispose()
        {
            OnStateChanged = null;
            _states.Clear();
            _currentState = null;
        }
    }
    
    #region Context Objects

    public static class NetworkStateContext
    {
        public class ErrorContext
        {
            public string ErrorMessage { get; set; }
            public NetworkState PreviousState { get; set; }

            public ErrorContext(string message, NetworkState previousState = NetworkState.Default)
            {
                ErrorMessage = message;
                PreviousState = previousState;
            }
        }

        public class HostContext
        {
            public int MaxConnections { get; set; }
            public string RelayJoinCode { get; set; }
            
            public HostContext(int maxConnections, string relayJoinCode = null)
            {
                MaxConnections = maxConnections;
                RelayJoinCode = relayJoinCode;
            }
        }

        public class ClientContext
        {
            public string RelayJoinCode { get; set; }
            
            public ClientContext(string relayJoinCode)
            {
                RelayJoinCode = relayJoinCode;
            }
        }

        public class GameLoadContext
        {
            public SceneDefinitions SceneToLoad { get; set; }
            public bool IsHost { get; set; }
            
            public GameLoadContext(SceneDefinitions scene, bool isHost)
            {
                SceneToLoad = scene;
                IsHost = isHost;
            }
        }
    }

    #endregion
}
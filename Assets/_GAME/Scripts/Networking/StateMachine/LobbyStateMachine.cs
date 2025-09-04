using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace _GAME.Scripts.Networking.StateMachine
{
    public enum LobbyState
    {
        Default,
        CreatingLobby,
        JoiningLobby,
        LobbyActive,
        LeavingLobby,
        RemovingLobby,
        Failed
    }

    public interface ILobbyState
    {
        LobbyState State { get; }
        Task OnEnterAsync(LobbyStateManager manager, object context = null);
        Task OnExitAsync(LobbyStateManager manager);
        bool CanTransitionTo(LobbyState targetState);
    }

    public abstract class LobbyStateBase : ILobbyState
    {
        public abstract LobbyState State { get; }

        public virtual async Task OnEnterAsync(LobbyStateManager manager, object context = null)
        {
            await Task.Yield();
            Debug.Log($"[LobbyState] Enter: {State}");
        }

        public virtual async Task OnExitAsync(LobbyStateManager manager)
        {
            await Task.Yield();
            Debug.Log($"[LobbyState] Exit: {State}");
        }

        public virtual bool CanTransitionTo(LobbyState targetState)
        {
            return targetState == LobbyState.Failed || targetState == LobbyState.Default;
        }
    }

    public class LobbyStateMachine : IDisposable
    {
        private readonly LobbyStateManager _manager;
        private readonly Dictionary<LobbyState, ILobbyState> _states;
        private ILobbyState _currentState;

        public ILobbyState CurrentStateInstance => _currentState;
        public LobbyState CurrentState => _currentState?.State ?? LobbyState.Default;

        public event Action<LobbyState, LobbyState> OnStateChanged;

        public LobbyStateMachine(LobbyStateManager manager)
        {
            _manager = manager;
            _states = CreateStates();
        }

        private Dictionary<LobbyState, ILobbyState> CreateStates()
        {
            return new Dictionary<LobbyState, ILobbyState>
            {
                { LobbyState.Default, new DefaultState() },
                { LobbyState.CreatingLobby, new CreatingLobbyState() },
                { LobbyState.JoiningLobby, new JoiningLobbyState() },
                { LobbyState.LobbyActive, new LobbyActiveState() },
                { LobbyState.LeavingLobby, new LeavingLobbyState () },
                { LobbyState.RemovingLobby, new RemovingLobbyState() },
                { LobbyState.Failed, new FailedLobbyState() }
            };
        }

        public bool TransitionTo(LobbyState targetState)
        {
            if (_currentState != null && !_currentState.CanTransitionTo(targetState))
            {
                Debug.LogWarning($"[LobbyStateMachine] Invalid transition: {_currentState.State} → {targetState}");
                return false;
            }

            return TransitionToInternal(targetState);
        }

        public async Task<bool> TransitionToAsync(LobbyState targetState, object context = null)
        {
            if (_currentState != null && !_currentState.CanTransitionTo(targetState))
            {
                Debug.LogWarning($"[LobbyStateMachine] Invalid transition: {_currentState.State} → {targetState}");
                return false;
            }

            return await TransitionToInternalAsync(targetState, context);
        }

        private bool TransitionToInternal(LobbyState targetState)
        {
            if (!_states.TryGetValue(targetState, out var newState))
            {
                Debug.LogError($"[LobbyStateMachine] State not found: {targetState}");
                return false;
            }

            _currentState?.OnExitAsync(_manager);
            var oldState = _currentState?.State ?? LobbyState.Default;
            _currentState = newState;
            _currentState.OnEnterAsync(_manager);
            OnStateChanged?.Invoke(oldState, targetState);
            return true;
        }

        private async Task<bool> TransitionToInternalAsync(LobbyState targetState, object context = null)
        {
            if (!_states.TryGetValue(targetState, out var newState))
            {
                Debug.LogError($"[LobbyStateMachine] State not found: {targetState}");
                return false;
            }

            if (_currentState != null)
                await _currentState.OnExitAsync(_manager);

            var oldState = _currentState?.State ?? LobbyState.Default;
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
} 

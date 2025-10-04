using System;
using _GAME.Scripts.Core.Player;
using _GAME.Scripts.Core.Services;
using _GAME.Scripts.HideAndSeek;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Core.StateMachine
{
    /// <summary>
    /// Base state class for game state machine
    /// </summary>
    public abstract class GameStateBase
    {
        protected GameStateMachine StateMachine { get; private set; }
        protected GameStateContext Context { get; private set; }

        public virtual void Initialize(GameStateMachine machine, GameStateContext context)
        {
            StateMachine = machine;
            Context = context;
        }

        public abstract void Enter();
        public abstract void Exit();
        public virtual void Update() { }
        public virtual void FixedUpdate() { }
    }

    /// <summary>
    /// Context data shared between states
    /// </summary>
    public class GameStateContext
    {
        public float PreparationTime = 5f;
        public float GameDuration = 300f;
        public IPlayerRegistry PlayerRegistry;
        public IRoleService RoleService;
    }

    /// <summary>
    /// Game state machine - manages game flow
    /// Replaces monolithic GameManager state handling
    /// </summary>
    public class GameStateMachine : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float preparationTime = 5f;
        [SerializeField] private float gameDuration = 300f;

        private NetworkVariable<GameState> _networkState = new(
            GameState.PreparingGame,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private GameStateBase _currentState;
        private GameStateContext _context;

        public GameState CurrentState => _networkState.Value;

        private void Awake()
        {
            InitializeContext();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _networkState.OnValueChanged += OnStateChanged;

            if (IsServer)
            {
                TransitionTo(GameState.PreparingGame);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_networkState != null)
                _networkState.OnValueChanged -= OnStateChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            _currentState?.Update();
        }

        private void FixedUpdate()
        {
            _currentState?.FixedUpdate();
        }

        private void InitializeContext()
        {
            _context = new GameStateContext
            {
                PreparationTime = preparationTime,
                GameDuration = gameDuration,
                PlayerRegistry = GameServices.Get<IPlayerRegistry>(),
                RoleService = GameServices.Get<IRoleService>()
            };
        }

        public void TransitionTo(GameState newState)
        {
            if (!IsServer) return;
            if (_networkState.Value == newState) return;

            _networkState.Value = newState;
        }

        private void OnStateChanged(GameState oldState, GameState newState)
        {
            _currentState?.Exit();

            _currentState = CreateState(newState);
            _currentState?.Initialize(this, _context);
            _currentState?.Enter();

            GameEventBus.Publish(new GameStateChangedEvent
            {
                OldState = oldState,
                NewState = newState
            });

            Debug.Log($"[StateMachine] Transitioned from {oldState} to {newState}");
        }

        private GameStateBase CreateState(GameState state)
        {
            return state switch
            {
                GameState.PreparingGame => new PreparationState(),
                GameState.Playing => new PlayingState(),
                GameState.GameEnded => new EndedState(),
                _ => null
            };
        }
    }

    // ==================== STATE IMPLEMENTATIONS ====================

    /// <summary>
    /// Preparation state - countdown and role assignment
    /// </summary>
    public class PreparationState : GameStateBase
    {
        private float _countdownTimer;

        public override void Enter()
        {
            Debug.Log("[PreparationState] Entered");
            _countdownTimer = Context.PreparationTime;

            // Assign roles
            AssignRoles();
        }

        public override void Update()
        {
            _countdownTimer -= Time.deltaTime;

            if (_countdownTimer <= 0f)
            {
                StateMachine.TransitionTo(GameState.Playing);
            }
        }

        public override void Exit()
        {
            Debug.Log("[PreparationState] Exited");
        }

        private void AssignRoles()
        {
            var allPlayers = Context.PlayerRegistry.GetAllPlayers();
            var playerList = new System.Collections.Generic.List<ModularPlayer>(allPlayers);

            if (playerList.Count == 0) return;

            // Calculate seeker count
            int totalPlayers = playerList.Count;
            int seekerCount = Mathf.Clamp(totalPlayers / 4, 1, Mathf.Max(1, totalPlayers - 1));

            // Shuffle and assign
            playerList.Shuffle();

            for (int i = 0; i < playerList.Count; i++)
            {
                var role = i < seekerCount ? Role.Seeker : Role.Hider;
                Context.RoleService.AssignRole(playerList[i].ClientId, role);
            }

            Debug.Log($"[PreparationState] Assigned {seekerCount} seekers, {playerList.Count - seekerCount} hiders");
        }
    }

    /// <summary>
    /// Playing state - main gameplay
    /// </summary>
    public class PlayingState : GameStateBase
    {
        private float _gameTimer;

        public override void Enter()
        {
            Debug.Log("[PlayingState] Entered");
            _gameTimer = Context.GameDuration;

            // Notify all players
            GameEventBus.Publish(new GameStateChangedEvent
            {
                OldState = GameState.PreparingGame,
                NewState = GameState.Playing
            });
        }

        public override void Update()
        {
            _gameTimer -= Time.deltaTime;

            // Time's up - Hiders win
            if (_gameTimer <= 0f)
            {
                EndGame(Role.Hider);
                return;
            }

            // Check win conditions
            CheckWinConditions();
        }

        public override void Exit()
        {
            Debug.Log("[PlayingState] Exited");
        }

        private void CheckWinConditions()
        {
            var hiders = Context.PlayerRegistry.GetPlayersByRole(Role.Hider);
            var seekers = Context.PlayerRegistry.GetPlayersByRole(Role.Seeker);

            int aliveHiders = 0;
            foreach (var hider in hiders)
            {
                if (hider.IsAlive()) aliveHiders++;
            }

            int aliveSeekers = 0;
            foreach (var seeker in seekers)
            {
                if (seeker.IsAlive()) aliveSeekers++;
            }

            // Seekers win if all hiders are dead
            if (aliveHiders == 0)
            {
                EndGame(Role.Seeker);
            }
            // Hiders win if all seekers are dead
            else if (aliveSeekers == 0)
            {
                EndGame(Role.Hider);
            }
        }

        private void EndGame(Role winner)
        {
            Debug.Log($"[PlayingState] Game ended. Winner: {winner}");
            StateMachine.TransitionTo(GameState.GameEnded);
        }
    }
    
    /// <summary>
    /// Ended state - show results and cleanup
    /// </summary>
    public class EndedState : GameStateBase
    {
        public override void Enter()
        {
            Debug.Log("[EndedState] Entered");

            // Determine winner
            // This can be enhanced with more sophisticated logic
        }

        public override void Exit()
        {
            Debug.Log("[EndedState] Exited");
        }
    }

    

    // ==================== UTILITY EXTENSIONS ====================
    public static class ListExtensions
    {
        private static System.Random _rng = new System.Random();

        public static void Shuffle<T>(this System.Collections.Generic.List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = _rng.Next(n + 1);
                (list[k], list[n]) = (list[n], list[k]);
            }
        }
    }
}
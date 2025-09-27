using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Core;
using _GAME.Scripts.HideAndSeek.Config;
using _GAME.Scripts.HideAndSeek.Util;
using _GAME.Scripts.Networking;
using _GAME.Scripts.Player;
using GAME.Scripts.DesignPattern;
using UnityEngine;
using Unity.Netcode;

namespace _GAME.Scripts.HideAndSeek
{
    public class GameManager : NetworkSingleton<GameManager>
    {
        [Header("Game Settings")] 
        [SerializeField] private GameSettingsConfig gameSettings;
        [SerializeField] private GameMode gameMode;
        [SerializeField] private SkillDataConfig skillDatabase;

        [Header("References")] 
        [SerializeField] private TimeCountDown timeCountDown;
        [SerializeField] private SpawnerController spawnerController;

        // Network Variables
        private NetworkVariable<GameState> _gameState = new(GameState.PreparingGame);

        // SERVER SOURCE OF TRUTH - Only server modifies this
        private readonly Dictionary<ulong, NetworkPlayerData> _serverPlayers = new();

        // CLIENT CACHES - Read-only, synced from server
        private readonly List<NetworkPlayerData> _localAllPlayers = new();
        private readonly List<NetworkPlayerData> _localHiderPlayers = new();
        private readonly List<NetworkPlayerData> _localSeekerPlayers = new();
        private readonly Dictionary<ulong, IGamePlayer> _allPlayersBehaviour = new();
        private readonly Dictionary<ulong, int> _indexById = new();

        // Public Properties
        public List<NetworkPlayerData> Seekers => _localSeekerPlayers;
        public List<NetworkPlayerData> Hiders => _localHiderPlayers;
        public List<NetworkPlayerData> AllPlayers => _localAllPlayers;
        public GameMode CurrentMode => gameMode;
        public GameSettingsConfig Settings => gameSettings;
        public GameState CurrentGameState => _gameState.Value;

        // Events
        public static event Action<List<NetworkPlayerData>> OnPlayersListUpdated;

        private Coroutine returnToLobbyCoroutine;
        private Coroutine spawnObjectRoutine;

        #region Unity Lifecycle

        protected override void OnNetworkAwake()
        {
            base.OnNetworkAwake();
            ValidateConfiguration();

            // SERVER ONLY SUBSCRIPTIONS
            if (IsServer)
            {
                TimeCountDown.OnCountdownFinished += OnServerTimeCountdownFinished;
                SpawnerController.OnFinishSpawning += OnServerPlayersSpawned;
            }
        }

        public override void OnDestroy()
        {
            if (IsServer)
            {
                TimeCountDown.OnCountdownFinished -= OnServerTimeCountdownFinished;
                SpawnerController.OnFinishSpawning -= OnServerPlayersSpawned;
            }
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            try
            {
                _gameState.OnValueChanged += OnGameStateChanged;

                if (IsServer)
                {
                    NetworkManager.Singleton.OnClientDisconnectCallback += OnServerClientDisconnected;
                    NetworkManager.Singleton.OnClientConnectedCallback += OnServerClientConnected;
                    Debug.Log("[GameManager] Server initialized successfully.");
                }

                GameEvent.OnPlayerKilled += OnPlayerDeathRequested;
                Debug.Log($"[GameManager] Client {NetworkManager.Singleton.LocalClientId} initialized.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameManager] Error during network spawn: {e.Message}");
            }
        }

        public override void OnNetworkDespawn()
        {
            try
            {
                StopAllCoroutines();
                _gameState.OnValueChanged -= OnGameStateChanged;

                if (IsServer && NetworkManager.Singleton != null)
                {
                    NetworkManager.Singleton.OnClientDisconnectCallback -= OnServerClientDisconnected;
                    NetworkManager.Singleton.OnClientConnectedCallback -= OnServerClientConnected;
                }

                GameEvent.OnPlayerKilled -= OnPlayerDeathRequested;
                ClearAllLocalCaches();
                _serverPlayers.Clear();
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameManager] Error during network despawn: {e.Message}");
            }
            base.OnNetworkDespawn();
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// All clients receive game state changes
        /// </summary>
        private void OnGameStateChanged(GameState oldState, GameState newState)
        {
            Debug.Log($"[GameManager] Game state changed from {oldState} to {newState}");

            switch (newState)
            {
                case GameState.PreparingGame:
                    Debug.Log("[GameManager] Preparing game...");
                    break;

                case GameState.Playing:
                    Debug.Log("[GameManager] Game started!");
                    GameEvent.OnGameStarted?.Invoke();
                    break;

                case GameState.GameEnded:
                    Debug.Log("[GameManager] Game ended.");
                    break;
            }
        }

        /// <summary>
        /// Server: Send full snapshot to newly connected client
        /// </summary>
        private void OnServerClientConnected(ulong clientId)
        {
            if (!IsServer) return;

            Debug.Log($"[GameManager] Server: Client {clientId} connected, sending snapshot.");
            var snapshot = _serverPlayers.Values.ToArray();
            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };
            SyncFullSnapshotClientRpc(snapshot, rpcParams);
        }

        /// <summary>
        /// Server: Handle client disconnect - mark as dead, don't remove
        /// </summary>
        private void OnServerClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            Debug.Log($"[GameManager] Server: Client {clientId} disconnected.");

            if (!_allPlayersBehaviour.ContainsKey(clientId)) return;

            // Mark player as dead (disconnected), but keep in game
            if (_serverPlayers.TryGetValue(clientId, out var playerData))
            {
                playerData.isAlive = false;
                _serverPlayers[clientId] = playerData;
                
                // Sync updated player data to all clients
                SyncPlayerUpdateClientRpc(playerData);

                // Notify all clients about disconnection (killerId = 0 means disconnect)
                NotifyPlayerKilledClientRpc(0, clientId);
                CheckWinConditions();
            }
        }

        /// <summary>
        /// Server: Handle countdown finished
        /// </summary>
        private void OnServerTimeCountdownFinished()
        {
            if (!IsServer) return;

            if (_gameState.Value == GameState.Playing)
            {
                Debug.Log("[GameManager] Server: Time's up! Hiders win!");
                EndGame(Role.Hider);
            }
            else if (_gameState.Value == GameState.PreparingGame)
            {
                //Delay 3s to notice and spawn object
                if(spawnObjectRoutine != null)
                    StopCoroutine(spawnObjectRoutine);
                spawnObjectRoutine = StartCoroutine(IESpawnObjectAndStartPlaying());
                
            } 
        }

        private IEnumerator IESpawnObjectAndStartPlaying()
        {
            //Spawn Item Object or Spawn Task...
            yield return new WaitForSeconds(3f);
            StartPlayingPhase();
            spawnObjectRoutine = null;
        }

        /// <summary>
        /// Server: All players spawned, start game preparation
        /// </summary>
        private void OnServerPlayersSpawned()
        {
            if (!IsServer) return;
            Debug.Log("[GameManager] Server: All players spawned, starting game preparation.");
            
            _gameState.Value = GameState.PreparingGame;
            StartPreparationCountdown();
        }

        /// <summary>
        /// Both: Player death requested (client calls this, server processes)
        /// </summary>
        private void OnPlayerDeathRequested(ulong killerId, ulong victimId)
        {
            if (!IsServer) return;

            if (_serverPlayers.TryGetValue(killerId, out var killer) && 
                _serverPlayers.TryGetValue(victimId, out var victim))
            {
                ProcessPlayerDeath(killer, victim);
            }
        }

        #endregion

        #region Server Authority Methods

        public Role GetPlayerRoleWithId(ulong playerId)
        {
            // Server/Host: đọc từ nguồn sự thật
            if (IsServer)
            {
                if (_serverPlayers.TryGetValue(playerId, out var d))
                    return d.role;
                return Role.None;
            }

            // Client: đọc từ snapshot đã sync
            if (_indexById.TryGetValue(playerId, out var idx) && idx >= 0 && idx < _localAllPlayers.Count)
                return _localAllPlayers[idx].role;

            // Fallback tìm tuyến tính (phòng khi index chưa có)
            var found = _localAllPlayers.Find(p => p.clientId == playerId);
            return found.clientId != 0 ? found.role : Role.None;
        }
        
        /// <summary>
        /// Server: Start preparation phase countdown
        /// </summary>
        private void StartPreparationCountdown()
        {
            if (!IsServer) return;
            const float preparationTime = 5f;
            timeCountDown?.StartCountdownServerRpc(preparationTime, "Game Starting In");
            Invoke(nameof(AssignRoles), preparationTime);
        }

        /// <summary>
        /// Server: Transition from preparation to playing
        /// </summary>
        private void StartPlayingPhase()
        {
            if (!IsServer || _gameState.Value != GameState.PreparingGame) return;

            _gameState.Value = GameState.Playing;

            if (gameSettings && timeCountDown)
            {
                timeCountDown.StartCountdownServerRpc(gameSettings.gameDuration);
            }

            Debug.Log("[GameManager] Server: Playing phase started.");
        }

        /// <summary>
        /// Server: Assign roles to all players randomly
        /// </summary>
        private void AssignRoles()
        {
            if (!IsServer) return;

            var allPlayerControllers = spawnerController?.GetSpawnedPlayersDictionary<PlayerController>();
            if (allPlayerControllers == null || allPlayerControllers.Count == 0)
            {
                Debug.LogError("[GameManager] Server: No players found for role assignment.");
                return;
            }

            // Calculate seeker count (1/4 of total, minimum 1, maximum total-1)
            int totalPlayers = allPlayerControllers.Count;
            int seekerCount = Mathf.Clamp(totalPlayers / 4, 1, Mathf.Max(1, totalPlayers - 1));

            // Clear server data and notify clients
            _serverPlayers.Clear();
            ClearAllDataClientRpc();

            // Randomize player order
            List<ulong> clientIds = allPlayerControllers.Keys.ToList();
            clientIds.ShuffleList();

            Debug.Log($"[GameManager] Server: Assigning {seekerCount} seekers, {clientIds.Count - seekerCount} hiders");

            // Assign roles
            for (int i = 0; i < clientIds.Count; i++)
            {
                var clientId = clientIds[i];
                var role = i < seekerCount ? Role.Seeker : Role.Hider;


                // Create and store player data
                var playerData = new NetworkPlayerData
                {
                    clientId = clientId,
                    role = role,
                    isAlive = true,
                }; 

                _serverPlayers[clientId] = playerData;
                // Sync to all clients
                SyncPlayerUpdateClientRpc(playerData);
            }

            NotifyRoleAssignmentCompleteClientRpc();
            Debug.Log("[GameManager] Server: Role assignment completed.");
        }

        /// <summary>
        /// Server: Process player death
        /// </summary>
        private void ProcessPlayerDeath(NetworkPlayerData killer, NetworkPlayerData victim)
        {
            if (!IsServer) return;

            try
            {
                Debug.Log($"[GameManager] Server: Player {victim.clientId} killed by {killer.clientId}");

                // Update victim status
                if (_serverPlayers.TryGetValue(victim.clientId, out var victimData))
                {
                    victimData.isAlive = false;
                    _serverPlayers[victim.clientId] = victimData;
                    
                    // Sync updated victim data
                    SyncPlayerUpdateClientRpc(victimData);
                }

                // Notify all clients
                NotifyPlayerKilledClientRpc(killer.clientId, victim.clientId);
                CheckWinConditions();
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameManager] Server: Error processing player death: {e.Message}");
            }
        }

        /// <summary>
        /// Server: Check if any team has won
        /// </summary>
        private void CheckWinConditions()
        {
            if (!IsServer || _gameState.Value != GameState.Playing) return;

            try
            {
                var allPlayers = _serverPlayers.Values;
                
                // Count hiders
                int totalHiders = allPlayers.Count(p => p.role == Role.Hider);
                int aliveHiders = allPlayers.Count(p => p.role == Role.Hider && p.isAlive);

                // Seekers win if all hiders are eliminated
                if (totalHiders > 0 && aliveHiders == 0)
                {
                    Debug.Log("[GameManager] Server: All hiders eliminated. Seekers win!");
                    EndGame(Role.Seeker);
                    return;
                }

                // Count seekers
                int totalSeekers = allPlayers.Count(p => p.role == Role.Seeker);
                int aliveSeekers = allPlayers.Count(p => p.role == Role.Seeker && p.isAlive);

                // Hiders win if all seekers are eliminated
                if (totalSeekers > 0 && aliveSeekers == 0)
                {
                    Debug.Log("[GameManager] Server: All seekers eliminated. Hiders win!");
                    EndGame(Role.Hider);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameManager] Server: Error checking win conditions: {e.Message}");
            }
        }

        /// <summary>
        /// Server: End the game
        /// </summary>
        private void EndGame(Role winnerRole)
        {
            if (!IsServer || _gameState.Value == GameState.GameEnded) return;

            try
            {
                Debug.Log($"[GameManager] Server: Game ended. Winner: {winnerRole}");

                _gameState.Value = GameState.GameEnded;
                timeCountDown?.StopCountdownServerRpc();
                
                NotifyGameEndedClientRpc(winnerRole);

                // Stop previous coroutine if running
                if(spawnObjectRoutine != null)
                {
                    StopCoroutine(spawnObjectRoutine);
                }
                
                if (returnToLobbyCoroutine != null)
                {
                    StopCoroutine(returnToLobbyCoroutine);
                }
                

                returnToLobbyCoroutine = StartCoroutine(ReturnToLobbyRoutine());
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameManager] Server: Error ending game: {e.Message}");
            }
        }

        #endregion

        #region Client RPCs

        /// <summary>
        /// Send full player data snapshot to specific client(s)
        /// </summary>
        [ClientRpc]
        private void SyncFullSnapshotClientRpc(NetworkPlayerData[] snapshot, ClientRpcParams rpcParams = default)
        {
            ClearAllLocalCaches();

            if (snapshot != null && snapshot.Length > 0)
            {
                for (int i = 0; i < snapshot.Length; i++)
                {
                    var player = snapshot[i];
                    _localAllPlayers.Add(player);
                    _indexById[player.clientId] = i;
                    
                    // Add to role-specific lists
                    if (player.role == Role.Hider)
                        _localHiderPlayers.Add(player);
                    else if (player.role == Role.Seeker)
                        _localSeekerPlayers.Add(player);
                }
            }

            OnPlayersListUpdated?.Invoke(_localAllPlayers);
            Debug.Log($"[GameManager] Client: Received full snapshot with {snapshot?.Length ?? 0} players.");
        }

        /// <summary>
        /// Update single player data across all clients
        /// </summary>
        [ClientRpc]
        private void SyncPlayerUpdateClientRpc(NetworkPlayerData playerData)
        {
            // Update or add player in main list
            if (_indexById.TryGetValue(playerData.clientId, out int index))
            {
                // Update existing player
                var oldPlayer = _localAllPlayers[index];
                _localAllPlayers[index] = playerData;
                
                // Update role-specific lists if role changed
                if (oldPlayer.role != playerData.role)
                {
                    // Remove from old role list
                    if (oldPlayer.role == Role.Hider)
                        _localHiderPlayers.RemoveAll(p => p.clientId == playerData.clientId);
                    else if (oldPlayer.role == Role.Seeker)
                        _localSeekerPlayers.RemoveAll(p => p.clientId == playerData.clientId);
                    
                    // Add to new role list
                    if (playerData.role == Role.Hider)
                        _localHiderPlayers.Add(playerData);
                    else if (playerData.role == Role.Seeker)
                        _localSeekerPlayers.Add(playerData);
                }
                else
                {
                    // Same role, just update in role-specific lists
                    if (playerData.role == Role.Hider)
                    {
                        for (int i = 0; i < _localHiderPlayers.Count; i++)
                        {
                            if (_localHiderPlayers[i].clientId == playerData.clientId)
                            {
                                _localHiderPlayers[i] = playerData;
                                break;
                            }
                        }
                    }
                    else if (playerData.role == Role.Seeker)
                    {
                        for (int i = 0; i < _localSeekerPlayers.Count; i++)
                        {
                            if (_localSeekerPlayers[i].clientId == playerData.clientId)
                            {
                                _localSeekerPlayers[i] = playerData;
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                // Add new player
                _indexById[playerData.clientId] = _localAllPlayers.Count;
                _localAllPlayers.Add(playerData);
                
                if (playerData.role == Role.Hider)
                    _localHiderPlayers.Add(playerData);
                else if (playerData.role == Role.Seeker)
                    _localSeekerPlayers.Add(playerData);
            }

            OnPlayersListUpdated?.Invoke(_localAllPlayers);
        }

        /// <summary>
        /// Clear all player data on all clients
        /// </summary>
        [ClientRpc]
        private void ClearAllDataClientRpc()
        {
            ClearAllLocalCaches();
            OnPlayersListUpdated?.Invoke(_localAllPlayers);
        }

        /// <summary>
        /// Notify all clients that role assignment is complete
        /// </summary>
        [ClientRpc]
        private void NotifyRoleAssignmentCompleteClientRpc()
        {
            GameEvent.OnRoleAssigned?.Invoke();
            Debug.Log("[GameManager] Role assignment completed.");
        }

        /// <summary>
        /// Notify all clients that the game has ended
        /// </summary>
        [ClientRpc]
        private void NotifyGameEndedClientRpc(Role winnerRole)
        {
            GameEvent.OnGameEnded?.Invoke(winnerRole);
            Debug.Log($"[GameManager] Game ended. Winner: {winnerRole}");
        }

        /// <summary>
        /// Notify all clients about player elimination
        /// </summary>
        [ClientRpc]
        private void NotifyPlayerKilledClientRpc(ulong killerId, ulong victimId)
        {
            GameEvent.OnPlayerKilled?.Invoke(killerId, victimId);
            
            string killerText = killerId == 0 ? "disconnection" : $"player {killerId}";
            Debug.Log($"[GameManager] Player {victimId} eliminated by {killerText}");
        }

        #endregion

        #region Public API

        /// <summary>
        /// Server: Register player behavior component
        /// </summary>
        public bool RegisterPlayer(IGamePlayer player)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[GameManager] RegisterPlayer can only be called on server.");
                return false;
            }

            if (player == null || _allPlayersBehaviour.ContainsKey(player.ClientId))
            {
                return false;
            }

            try
            {
                _allPlayersBehaviour[player.ClientId] = player;
                Debug.Log($"[GameManager] Server: Player {player.ClientId} registered.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameManager] Server: Error registering player {player.ClientId}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if role can be assigned (based on local cache)
        /// </summary>
        public bool CanAssignRole(ulong clientId, Role newRole)
        {
            if (_gameState.Value != GameState.PreparingGame) return false;

            int currentSeekers = _localAllPlayers.Count(p => p.clientId != clientId && p.role == Role.Seeker);

            if (newRole == Role.Seeker)
            {
                int maxSeekers = Mathf.Max(1, _localAllPlayers.Count / 4);
                return currentSeekers < maxSeekers;
            }

            return true;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Clear all local player caches
        /// </summary>
        private void ClearAllLocalCaches()
        {
            _localAllPlayers.Clear();
            _localHiderPlayers.Clear();
            _localSeekerPlayers.Clear();
            _indexById.Clear();
        }

        /// <summary>
        /// Coroutine: Return to lobby after game ends
        /// </summary>
        private IEnumerator ReturnToLobbyRoutine()
        {
            const float displayTime = 5f;

            Debug.Log($"[GameManager] Server: Showing results for {displayTime} seconds...");
            yield return new WaitForSeconds(displayTime);

            Debug.Log("[GameManager] Server: Returning to lobby...");
            yield return NetworkController.Instance.DisconnectAsync();

            ResetGameState();
            SceneController.Instance.LoadSceneAsync((int)SceneDefinitions.Home);
            returnToLobbyCoroutine = null;
        }

        /// <summary>
        /// Server: Reset game state for new game
        /// </summary>
        private void ResetGameState()
        {
            if (!IsServer) return;

            try
            {
                _gameState.Value = GameState.PreparingGame;
                _serverPlayers.Clear();
                ClearAllDataClientRpc();
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameManager] Server: Error resetting game state: {e.Message}");
            }
        }

        /// <summary>
        /// Validate required component references
        /// </summary>
        private void ValidateConfiguration()
        {
            var errors = new List<string>();

            if (gameSettings == null) errors.Add("GameSettings is null");
            if (timeCountDown == null) errors.Add("TimeCountDown component is missing");
            if (spawnerController == null) errors.Add("SpawnerController component is missing");
            if (skillDatabase == null) errors.Add("SkillDatabase is missing");

            if (errors.Count > 0)
            {
                Debug.LogError($"[GameManager] Configuration errors:\n- {string.Join("\n- ", errors)}");
            }
            else
            {
                Debug.Log("[GameManager] Configuration validation passed.");
            }
        }

        #endregion

        #if UNITY_EDITOR
        [Header("Debug")] 
        [SerializeField] private bool enableDebugLogs = true;
        #endif
    }
}
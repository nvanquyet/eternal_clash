using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using HideAndSeekGame.Core;
using _GAME.Scripts.DesignPattern.Interaction;

namespace HideAndSeekGame.Managers
{
    public class GameManager : NetworkBehaviour
    {
        [Header("Game Settings")]
        [SerializeField] private GameSettings gameSettings;
        [SerializeField] private SkillData[] skillDatabase;
        [SerializeField] private ObjectData[] objectDatabase;
        [SerializeField] private TaskData[] taskDatabase;
        
        [Header("Spawn Points")]
        [SerializeField] private Transform[] hiderSpawnPoints;
        [SerializeField] private Transform[] seekerSpawnPoints;
        [SerializeField] private Transform[] taskSpawnPoints;
        [SerializeField] private Transform[] objectSpawnPoints;
        
        // Network Variables
        private NetworkVariable<NetworkGameState> networkGameState = new NetworkVariable<NetworkGameState>();
        private NetworkList<NetworkPlayerData> networkPlayers;
        
        // Game State
        private Dictionary<ulong, IGamePlayer> players = new Dictionary<ulong, IGamePlayer>();
        private List<IGameTask> gameTasks = new List<IGameTask>();
        private List<IObjectDisguise> disguiseObjects = new List<IObjectDisguise>();
        private float gameStartTime;
        private float nextObjectSwapTime;
        
        // Events
        public static event Action<GameState> OnGameStateChanged;
        public static event Action<PlayerRole> OnGameEnded;
        public static event Action<int, int> OnTaskProgressUpdated;
        public static event Action<float> OnTimeUpdated;
        
        // Properties
        public GameState CurrentState => networkGameState.Value.state;
        public GameMode CurrentMode => networkGameState.Value.mode;
        public float TimeRemaining => networkGameState.Value.timeRemaining;
        public GameSettings Settings => gameSettings;
        
        private void Awake()
        {
            networkPlayers = new NetworkList<NetworkPlayerData>();
        }
        
        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                networkGameState.Value = new NetworkGameState
                {
                    state = GameState.Lobby,
                    mode = gameSettings.gameMode,
                    timeRemaining = gameSettings.gameTime,
                    completedTasks = 0,
                    totalTasks = gameSettings.tasksToComplete,
                    alivePlayers = 0
                };
                
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }
            
            networkGameState.OnValueChanged += OnGameStateNetworkChanged;
            networkPlayers.OnListChanged += OnPlayersListChanged;
        }
        
        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            
            networkGameState.OnValueChanged -= OnGameStateNetworkChanged;
            networkPlayers.OnListChanged -= OnPlayersListChanged;
        }
        
        private void Update()
        {
            if (!IsServer || CurrentState != GameState.Playing) return;
            
            UpdateGameTime();
            
            if (CurrentMode == GameMode.PersonVsObject)
            {
                UpdateObjectSwapping();
            }
            
            CheckWinConditions();
        }
        
        #region Server Methods
        
        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"Client {clientId} connected");
        }
        
        private void OnClientDisconnected(ulong clientId)
        {
            if (players.ContainsKey(clientId))
            {
                RemovePlayer(clientId);
            }
        }
        
        [ServerRpc(RequireOwnership = false)]
        public void StartGameServerRpc()
        {
            if (CurrentState != GameState.Lobby) return;
            
            AssignRoles();
            SpawnGameElements();
            
            var newState = networkGameState.Value;
            newState.state = GameState.Preparation;
            networkGameState.Value = newState;
            
            gameStartTime = Time.time;
            nextObjectSwapTime = Time.time + gameSettings.objectSwapTime;
            
            // Start game after preparation time
            Invoke(nameof(StartPlayingPhase), 5f);
        }
        
        private void StartPlayingPhase()
        {
            var newState = networkGameState.Value;
            newState.state = GameState.Playing;
            networkGameState.Value = newState;
            
            // Notify all players
            foreach (var player in players.Values)
            {
                player.OnGameStart();
            }
        }
        
        private void AssignRoles()
        {
            var clientIds = NetworkManager.Singleton.ConnectedClientsList.Select(c => c.ClientId).ToList();
            var seekerCount = Mathf.Max(1, clientIds.Count / 4); // 1/4 players are seekers
            
            // Randomly assign seekers
            var seekers = clientIds.OrderBy(x => UnityEngine.Random.value).Take(seekerCount).ToList();
            
            foreach (var clientId in clientIds)
            {
                var role = seekers.Contains(clientId) ? PlayerRole.Seeker : PlayerRole.Hider;
                AssignPlayerRole(clientId, role);
            }
        }
        
        private void AssignPlayerRole(ulong clientId, PlayerRole role)
        {
            var playerData = new NetworkPlayerData
            {
                clientId = clientId,
                role = role,
                position = Vector3.zero,
                isAlive = true,
                health = role == PlayerRole.Seeker ? gameSettings.seekerHealth : 100f
            };
            
            networkPlayers.Add(playerData);
        }
        
        private void SpawnGameElements()
        {
            if (CurrentMode == GameMode.PersonVsPerson)
            {
                SpawnTasks();
            }
            else if (CurrentMode == GameMode.PersonVsObject)
            {
                SpawnDisguiseObjects();
            }
        }
        
        private void SpawnTasks()
        {
            for (int i = 0; i < gameSettings.tasksToComplete && i < taskSpawnPoints.Length; i++)
            {
                var spawnPoint = taskSpawnPoints[i];
                var taskType = (TaskType)(i % System.Enum.GetValues(typeof(TaskType)).Length);
                var taskData = taskDatabase.FirstOrDefault(t => t.type == taskType);
                
                if (taskData.prefab != null)
                {
                    var taskObj = Instantiate(taskData.prefab, spawnPoint.position, spawnPoint.rotation);
                    var networkObj = taskObj.GetComponent<NetworkObject>();
                    if (networkObj != null)
                    {
                        networkObj.Spawn();
                    }
                    
                    var gameTask = taskObj.GetComponent<IGameTask>();
                    if (gameTask != null)
                    {
                        gameTasks.Add(gameTask);
                    }
                }
            }
        }
        
        private void SpawnDisguiseObjects()
        {
            foreach (var spawnPoint in objectSpawnPoints)
            {
                var randomObjectType = (ObjectType)UnityEngine.Random.Range(0, System.Enum.GetValues(typeof(ObjectType)).Length);
                var objectData = objectDatabase.FirstOrDefault(o => o.type == randomObjectType);
                
                if (objectData.prefab != null)
                {
                    var objInstance = Instantiate(objectData.prefab, spawnPoint.position, spawnPoint.rotation);
                    var networkObj = objInstance.GetComponent<NetworkObject>();
                    if (networkObj != null)
                    {
                        networkObj.Spawn();
                    }
                    
                    var disguiseObj = objInstance.GetComponent<IObjectDisguise>();
                    if (disguiseObj != null)
                    {
                        disguiseObjects.Add(disguiseObj);
                    }
                }
            }
        }
        
        private void UpdateGameTime()
        {
            var newState = networkGameState.Value;
            newState.timeRemaining -= Time.deltaTime;
            networkGameState.Value = newState;
            
            if (newState.timeRemaining <= 0)
            {
                EndGame(PlayerRole.Seeker); // Time up, seekers win by default
            }
        }
        
        private void UpdateObjectSwapping()
        {
            if (Time.time >= nextObjectSwapTime)
            {
                ForceObjectSwapClientRpc();
                nextObjectSwapTime = Time.time + gameSettings.objectSwapTime;
            }
        }
        
        private void CheckWinConditions()
        {
            if (CurrentMode == GameMode.PersonVsPerson)
            {
                // Check if all tasks completed
                if (networkGameState.Value.completedTasks >= networkGameState.Value.totalTasks)
                {
                    EndGame(PlayerRole.Hider);
                    return;
                }
                
                // Check if all hiders are dead
                var aliveHiders = networkPlayers.Count(p => p.role == PlayerRole.Hider && p.isAlive);
                if (aliveHiders == 0)
                {
                    EndGame(PlayerRole.Seeker);
                    return;
                }
                
                // Check if all seekers are dead
                var aliveSeekers = networkPlayers.Count(p => p.role == PlayerRole.Seeker && p.isAlive);
                if (aliveSeekers == 0)
                {
                    EndGame(PlayerRole.Hider);
                }
            }
            else if (CurrentMode == GameMode.PersonVsObject)
            {
                // Check if all hiders found
                var aliveHiders = networkPlayers.Count(p => p.role == PlayerRole.Hider && p.isAlive);
                if (aliveHiders == 0)
                {
                    EndGame(PlayerRole.Seeker);
                    return;
                }
                
                // Check if all seekers are dead
                var aliveSeekers = networkPlayers.Count(p => p.role == PlayerRole.Seeker && p.isAlive);
                if (aliveSeekers == 0)
                {
                    EndGame(PlayerRole.Hider);
                }
            }
        }
        
        private void EndGame(PlayerRole winnerRole)
        {
            var newState = networkGameState.Value;
            newState.state = GameState.GameOver;
            networkGameState.Value = newState;
            
            OnGameEnded?.Invoke(winnerRole);
            
            // Notify all players
            foreach (var player in players.Values)
            {
                player.OnGameEnd(winnerRole);
            }
        }
        
        [ServerRpc(RequireOwnership = false)]
        public void PlayerTaskCompletedServerRpc(ulong playerId, int taskId)
        {
            var newState = networkGameState.Value;
            newState.completedTasks++;
            networkGameState.Value = newState;
            
            OnTaskProgressUpdated?.Invoke(newState.completedTasks, newState.totalTasks);
        }
        
        [ServerRpc(RequireOwnership = false)]
        public void PlayerKilledServerRpc(ulong killerId, ulong victimId)
        {
            // Update victim status
            for (int i = 0; i < networkPlayers.Count; i++)
            {
                var playerData = networkPlayers[i];
                if (playerData.clientId == victimId)
                {
                    playerData.isAlive = false;
                    networkPlayers[i] = playerData;
                    break;
                }
            }
            
            // If seeker killed hider, restore health
            var killer = networkPlayers.FirstOrDefault(p => p.clientId == killerId);
            var victim = networkPlayers.FirstOrDefault(p => p.clientId == victimId);
            
            if (killer.role == PlayerRole.Seeker && victim.role == PlayerRole.Hider)
            {
                for (int i = 0; i < networkPlayers.Count; i++)
                {
                    var playerData = networkPlayers[i];
                    if (playerData.clientId == killerId)
                    {
                        playerData.health = Mathf.Min(gameSettings.seekerHealth, playerData.health + gameSettings.hiderKillReward);
                        networkPlayers[i] = playerData;
                        break;
                    }
                }
            }
        }
        
        [ServerRpc(RequireOwnership = false)]
        public void PlayerTookDamageServerRpc(ulong playerId, float damage)
        {
            for (int i = 0; i < networkPlayers.Count; i++)
            {
                var playerData = networkPlayers[i];
                if (playerData.clientId == playerId)
                {
                    playerData.health = Mathf.Max(0, playerData.health - damage);
                    if (playerData.health <= 0)
                    {
                        playerData.isAlive = false;
                    }
                    networkPlayers[i] = playerData;
                    break;
                }
            }
        }
        
        #endregion
        
        #region Client RPCs
        
        [ClientRpc]
        private void ForceObjectSwapClientRpc()
        {
            // Force all hiders to swap objects
            var hiders = players.Values.Where(p => p.Role == PlayerRole.Hider);
            foreach (var hider in hiders)
            {
                if (hider is IHider h)
                {
                    // Implementation will be in hider class
                }
            }
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnGameStateNetworkChanged(NetworkGameState previousValue, NetworkGameState newValue)
        {
            OnGameStateChanged?.Invoke(newValue.state);
            OnTimeUpdated?.Invoke(newValue.timeRemaining);
            OnTaskProgressUpdated?.Invoke(newValue.completedTasks, newValue.totalTasks);
        }
        
        private void OnPlayersListChanged(NetworkListEvent<NetworkPlayerData> changeEvent)
        {
            // Handle player list changes
            var newState = networkGameState.Value;
            newState.alivePlayers = networkPlayers.Count(p => p.isAlive);
            networkGameState.Value = newState;
        }
        
        #endregion
        
        #region Public Methods
        
        public void RegisterPlayer(IGamePlayer player)
        {
            if (!players.ContainsKey(player.ClientId))
            {
                players[player.ClientId] = player;
            }
        }
        
        public void RemovePlayer(ulong clientId)
        {
            if (players.ContainsKey(clientId))
            {
                players.Remove(clientId);
            }
            
            for (int i = networkPlayers.Count - 1; i >= 0; i--)
            {
                if (networkPlayers[i].clientId == clientId)
                {
                    networkPlayers.RemoveAt(i);
                    break;
                }
            }
        }
        
        public SkillData GetSkillData(SkillType skillType)
        {
            return skillDatabase.FirstOrDefault(s => s.type == skillType);
        }
        
        public ObjectData GetObjectData(ObjectType objectType)
        {
            return objectDatabase.FirstOrDefault(o => o.type == objectType);
        }
        
        public TaskData GetTaskData(TaskType taskType)
        {
            return taskDatabase.FirstOrDefault(t => t.type == taskType);
        }
        
        #endregion
    }
}
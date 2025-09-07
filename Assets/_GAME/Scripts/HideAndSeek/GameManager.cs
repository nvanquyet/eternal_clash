using System;
using System.Collections;
using System.Collections.Generic;
using _GAME.Scripts.Core;
using _GAME.Scripts.HideAndSeek.Config;
using _GAME.Scripts.HideAndSeek.Util;
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
        [SerializeField] private SkillDataConfig skillDatabase;
        [SerializeField] private ObjectDataConfig objectDatabase;
        [SerializeField] private TaskDataConfig taskDatabase;
        
        [Header("Spawn Points")]
        [SerializeField] private Transform[] hiderSpawnPoints;
        [SerializeField] private Transform[] seekerSpawnPoints;
        [SerializeField] private Transform[] taskSpawnPoints;
        [SerializeField] private Transform[] objectSpawnPoints;

        [Header("References")] [SerializeField]
        private TimeCountDown timeCountDown;

        [SerializeField] private SpawnerController spawnerController; 
        
        // Network Variables
        private NetworkVariable<NetworkGameState> networkGameState = new NetworkVariable<NetworkGameState>();
        private NetworkList<NetworkPlayerData> networkPlayers;
        
        // Game State
        private Dictionary<ulong, IGamePlayer> players = new Dictionary<ulong, IGamePlayer>();
        private List<IGameTask> gameTasks = new List<IGameTask>();
        private List<IObjectDisguise> disguiseObjects = new List<IObjectDisguise>();
        // Events
        public static event Action<GameState> OnGameStateChanged;
        public static event Action<PlayerRole> OnGameEnded;
        public static event Action<int, int> OnTaskProgressUpdated;
        
        // Properties
        public GameState CurrentState => networkGameState.Value.state;
        public GameMode CurrentMode => networkGameState.Value.mode;
        public GameSettingsConfig Settings => gameSettings;
        
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
                    state = GameState.Preparation,
                    mode = gameSettings.gameMode,
                    completedTasks = 0,
                    totalTasks = gameSettings.tasksToComplete,
                    alivePlayers = 0
                };
            }
            
            networkGameState.OnValueChanged += OnGameStateNetworkChanged;
            networkPlayers.OnListChanged += OnPlayersListChanged;


            if(spawnerController) spawnerController.OnFinishSpawning += StartGameServerRpc;
        }
        
        public override void OnNetworkDespawn()
        {
            networkGameState.OnValueChanged -= OnGameStateNetworkChanged;
            if (networkPlayers != null)
            {
                networkPlayers.OnListChanged -= OnPlayersListChanged;
            }
            
            if(spawnerController) spawnerController.OnFinishSpawning -= StartGameServerRpc;
        }
        
        // private void Update()
        // {
        //     if (!IsServer || CurrentState != GameState.Playing) return;
        //     
        //     if (CurrentMode == GameMode.PersonVsObject)
        //     {
        //         UpdateObjectSwapping();
        //     }
        //     
        //     CheckWinConditions();
        // }
        //
        #region Server Methods
        
        [ServerRpc(RequireOwnership = false)]
        private void StartGameServerRpc()
        {
            AssignRoles();
            SpawnGameElements();
            
            var newState = networkGameState.Value;
            newState.state = GameState.Preparation;
            networkGameState.Value = newState;
            
            //gameStartTime = Time.time;
            //
            if (gameSettings)
            {
                timeCountDown.OnCountdownFinished += () =>
                {
                    EndGame(PlayerRole.Seeker); // Time up, seekers win by default
                };
                timeCountDown.StartCountdownServerRpc(gameSettings.gameDuration);
            }
            
            // Start game after preparation time
            StartCoroutine(StartPlayingPhaseCoroutine());
        }
        
        private IEnumerator StartPlayingPhaseCoroutine()
        {
            yield return new WaitForSeconds(5f);
            StartPlayingPhase();
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
            if (NetworkManager.Singleton == null) return;
            
            var clientsList = NetworkManager.Singleton.ConnectedClientsList;
            var seekerCount = Mathf.Max(1, clientsList.Count / 4); // 1/4 players are seekers
            
            // Create list of client IDs
            List<ulong> clientIds = new List<ulong>();
            foreach (var client in clientsList)
            {
                clientIds.Add(client.ClientId);
            }
            
            // Randomly assign seekers
            List<ulong> seekers = new List<ulong>();
            for (int i = 0; i < seekerCount; i++)
            {
                if (clientIds.Count > 0)
                {
                    int randomIndex = UnityEngine.Random.Range(0, clientIds.Count);
                    seekers.Add(clientIds[randomIndex]);
                    clientIds.RemoveAt(randomIndex);
                }
            }
            
            // Assign roles
            foreach (var client in clientsList)
            {
                var role = seekers.Contains(client.ClientId) ? PlayerRole.Seeker : PlayerRole.Hider;
                AssignPlayerRole(client.ClientId, role);
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
                var taskData = GetTaskData(taskType);
                
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
                var objectData = GetObjectData(randomObjectType);
                
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
        
        
        private void CheckWinConditions()
        {
            if (networkPlayers == null || networkPlayers.Count == 0) return;
            
            if (CurrentMode == GameMode.PersonVsPerson)
            {
                // Check if all tasks completed
                if (networkGameState.Value.completedTasks >= networkGameState.Value.totalTasks)
                {
                    EndGame(PlayerRole.Hider);
                    return;
                }
                
                // Check if all hiders are dead
                int aliveHiders = 0;
                int aliveSeekers = 0;
                
                foreach (var player in networkPlayers)
                {
                    if (player.isAlive)
                    {
                        if (player.role == PlayerRole.Hider)
                            aliveHiders++;
                        else if (player.role == PlayerRole.Seeker)
                            aliveSeekers++;
                    }
                }
                
                if (aliveHiders == 0)
                {
                    EndGame(PlayerRole.Seeker);
                    return;
                }
                
                if (aliveSeekers == 0)
                {
                    EndGame(PlayerRole.Hider);
                }
            }
            else if (CurrentMode == GameMode.PersonVsObject)
            {
                // Check if all hiders found or all seekers dead
                int aliveHiders = 0;
                int aliveSeekers = 0;
                
                foreach (var player in networkPlayers)
                {
                    if (player.isAlive)
                    {
                        if (player.role == PlayerRole.Hider)
                            aliveHiders++;
                        else if (player.role == PlayerRole.Seeker)
                            aliveSeekers++;
                    }
                }
                
                if (aliveHiders == 0)
                {
                    EndGame(PlayerRole.Seeker);
                    return;
                }
                
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
            
            timeCountDown.StopCountdownServerRpc();
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
            
            // Find killer and victim data
            NetworkPlayerData killer = default;
            NetworkPlayerData victim = default;
            bool killerFound = false;
            bool victimFound = false;
            
            for (int i = 0; i < networkPlayers.Count; i++)
            {
                if (networkPlayers[i].clientId == killerId)
                {
                    killer = networkPlayers[i];
                    killerFound = true;
                }
                if (networkPlayers[i].clientId == victimId)
                {
                    victim = networkPlayers[i];
                    victimFound = true;
                }
                
                if (killerFound && victimFound) break;
            }
            
            // If seeker killed hider, restore health
            if (killerFound && victimFound && killer.role == PlayerRole.Seeker && victim.role == PlayerRole.Hider)
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
            foreach (var kvp in players)
            {
                if (kvp.Value.Role == PlayerRole.Hider)
                {
                    if (kvp.Value is IHider hider)
                    {
                        // Implementation will be in hider class
                        Debug.Log($"Forcing object swap for hider {hider.ClientId}");
                    }
                }
            }
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnGameStateNetworkChanged(NetworkGameState previousValue, NetworkGameState newValue)
        {
            OnGameStateChanged?.Invoke(newValue.state);
            OnTaskProgressUpdated?.Invoke(newValue.completedTasks, newValue.totalTasks);
        }
        
        private void OnPlayersListChanged(NetworkListEvent<NetworkPlayerData> changeEvent)
        {
            // Handle player list changes
            var newState = networkGameState.Value;
            
            // Count alive players manually
            int aliveCount = 0;
            foreach (var player in networkPlayers)
            {
                if (player.isAlive)
                    aliveCount++;
            }
            
            newState.alivePlayers = aliveCount;
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
            return skillDatabase.GetData(skillType);
        }

        private ObjectData GetObjectData(ObjectType objectType)
        {
            return objectDatabase.GetData(objectType);
        }

        private TaskData GetTaskData(TaskType taskType)
        {
            return taskDatabase.GetData(taskType);
        }
        
        #endregion


        #region Testing
        
        [Header("Testing")]
        [SerializeField] private PlayerController playerPrefab;
        
        
        [ContextMenu("Spawn Test Player")]
        public void SpawnTestPlayer()
        {
            if (NetworkManager.Singleton == null || playerPrefab == null) return;
            
            
            var playerInstance = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
            var networkObj = playerInstance.GetComponent<NetworkObject>();
            if (networkObj != null)
            {
                networkObj.SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId);
            }
            
            //Assign random role for testing
            var role = UnityEngine.Random.value > 0.5f ? PlayerRole.Hider : PlayerRole.Seeker;
            playerInstance.SetRole(role);
        }
        #endregion
        
    }
}
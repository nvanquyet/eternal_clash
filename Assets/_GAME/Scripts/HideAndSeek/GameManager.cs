using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using _GAME.Scripts.Core;
using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.Config;
using _GAME.Scripts.HideAndSeek.Player;
using _GAME.Scripts.HideAndSeek.Util;
using _GAME.Scripts.Player;
using GAME.Scripts.DesignPattern;
using UnityEngine;
using Unity.Netcode;

namespace _GAME.Scripts.HideAndSeek
{
    public class GameManager : NetworkSingleton<GameManager>
    {
        [Header("Game Settings")] [SerializeField]
        private GameSettingsConfig gameSettings;

        [SerializeField] private SkillDataConfig skillDatabase;
        [SerializeField] private ObjectDataConfig objectDatabase;
        [SerializeField] private TaskDataConfig taskDatabase;

        [Header("Spawn Points")] [SerializeField]
        private Transform[] hiderSpawnPoints;

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
        private Dictionary<int, IGameTask> taskIdMapping = new Dictionary<int, IGameTask>();


        private readonly Dictionary<ulong, Action<Role>> _roleChangedSubs = new();
        private readonly Dictionary<ulong, Action<float, float>> _hpChangedSubs = new();
        private readonly Dictionary<ulong, Action<IDefendable, IAttackable>> _diedSubs = new();


        // Events
        public static event Action<GameState> OnGameStateChanged;
        public static event Action<Role> OnGameEnded;
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
                networkGameState.OnValueChanged += OnGameStateNetworkChanged;
                networkPlayers.OnListChanged += OnPlayersListChanged;
                if (timeCountDown) timeCountDown.OnCountdownFinished += TimeDurationEnded;
                if (spawnerController) spawnerController.OnFinishSpawning += StartGameServerRpc;

                Debug.Log("GameManager: Initialized on Server.");
            }

            HiderPlayer.OnTaskProgressChanged += HandleHiderTaskProgressChanged; // optional
            SeekerPlayer.OnHidersCaughtChanged += HandleSeekerCaughtChanged; // optional
        }

        public override void OnNetworkDespawn()
        {
            networkGameState.OnValueChanged -= OnGameStateNetworkChanged;
            if (networkPlayers != null)
            {
                networkPlayers.OnListChanged -= OnPlayersListChanged;
            }

            if (spawnerController) spawnerController.OnFinishSpawning -= StartGameServerRpc;
            if (timeCountDown) timeCountDown.OnCountdownFinished -= TimeDurationEnded;

            HiderPlayer.OnTaskProgressChanged -= HandleHiderTaskProgressChanged;
            SeekerPlayer.OnHidersCaughtChanged -= HandleSeekerCaughtChanged;
        }

        #region Player State Synchronization

        [ServerRpc(RequireOwnership = false)]
        public void SyncPlayerPositionServerRpc(ulong clientId, Vector3 position)
        {
            if (!IsServer) return;

            for (int i = 0; i < networkPlayers.Count; i++)
            {
                var playerData = networkPlayers[i];
                if (playerData.clientId == clientId)
                {
                    playerData.position = position;
                    networkPlayers[i] = playerData;
                    break;
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void SyncPlayerHealthServerRpc(ulong clientId, float health)
        {
            if (!IsServer) return;

            for (int i = 0; i < networkPlayers.Count; i++)
            {
                var playerData = networkPlayers[i];
                if (playerData.clientId == clientId)
                {
                    playerData.health = Mathf.Clamp(health, 0f,
                        playerData.role == Role.Seeker ? gameSettings.seekerHealth : 100f);

                    if (playerData.health <= 0f)
                    {
                        playerData.isAlive = false;
                    }

                    networkPlayers[i] = playerData;
                    break;
                }
            }
        }

        [ClientRpc]
        public void SyncSkillEffectClientRpc(ulong playerId, SkillType skillType, Vector3 position,
            Vector3 direction, float duration = 0f)
        {
            // Đồng bộ skill effects cho all clients
            var player = GetPlayerByClientId(playerId);
            if (player != null)
            {
                // Play visual/audio effects for all clients
                PlaySkillEffectForAllClients(skillType, position, direction, duration);
            }
        }

        private void PlaySkillEffectForAllClients(SkillType skillType, Vector3 position,
            Vector3 direction, float duration)
        {
            // Implement visual/audio effects based on skill type
            switch (skillType)
            {
                case SkillType.Detect:
                    // Show detection radius effect
                    Debug.Log($"[GameManager] Playing detection effect at {position}");
                    break;

                case SkillType.Teleport:
                    // Show teleport effect
                    Debug.Log($"[GameManager] Playing teleport effect at {position}");
                    break;

                case SkillType.FreezeHider:
                case SkillType.FreezeSeeker:
                    // Show freeze effect
                    Debug.Log($"[GameManager] Playing freeze effect at {position}");
                    break;

                case SkillType.Rush:
                    // Show rush effect
                    Debug.Log($"[GameManager] Playing rush effect from {position} to {direction}");
                    break;
            }
        }

        #endregion

        #region Server Methods

        [ServerRpc(RequireOwnership = false)]
        private void StartGameServerRpc()
        {
            Debug.Log("[GameManager] Starting Game...");
            AssignRoles();
            SpawnGameElements();

            var newState = networkGameState.Value;
            newState.state = GameState.Preparation;
            networkGameState.Value = newState;

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

            if (gameSettings && timeCountDown)
            {
                timeCountDown.StartCountdownServerRpc(gameSettings.gameDuration);
            }
        }

        private void AssignRoles()
        {
            if (NetworkManager.Singleton == null) return;

            var clientsList = NetworkManager.Singleton.ConnectedClients;
            var allPlayers = spawnerController.GetSpawnedPlayersDictionary<PlayerController>();

            var seekerCount = Mathf.Max(1, allPlayers.Count / 4); // 1/4 players are seekers

            var seekerPlayers = new List<IGamePlayer>();
            var hiderPlayers = new List<IGamePlayer>();

            // Create list of client IDs
            List<ulong> clientIds = new List<ulong>();
            foreach (var client in clientsList)
            {
                clientIds.Add(client.Value.ClientId);
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
            foreach (var client in clientsList.Values)
            {
                var role = seekers.Contains(client.ClientId) ? Role.Seeker : Role.Hider;
                if (allPlayers.ContainsKey(client.ClientId))
                {
                    allPlayers[client.ClientId].SetRole(role);
                    AssignPlayerRole(client.ClientId, role);
                }
            }
        }

        private void AssignPlayerRole(ulong clientId, Role role)
        {
            var playerData = new NetworkPlayerData
            {
                clientId = clientId,
                role = role,
                position = Vector3.zero,
                isAlive = true,
                health = role == Role.Seeker ? gameSettings.seekerHealth : 100f
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
                        taskIdMapping[i] = gameTask; // Map task ID to task object
                    }
                }
            }
        }

        private void SpawnDisguiseObjects()
        {
            foreach (var spawnPoint in objectSpawnPoints)
            {
                var randomObjectType =
                    (ObjectType)UnityEngine.Random.Range(0, System.Enum.GetValues(typeof(ObjectType)).Length);
                var objectData = GetObjectData(randomObjectType);

                if (objectData.prefab != null)
                {
                    var objInstance = Instantiate(objectData.prefab, spawnPoint.position, spawnPoint.rotation);
                    var networkObj = objInstance.GetComponent<NetworkObject>();
                    if (networkObj != null)
                    {
                        networkObj.Spawn();
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
                    EndGame(Role.Hider);
                    return;
                }

                // Check if all hiders are dead
                int aliveHiders = 0;
                int aliveSeekers = 0;

                foreach (var player in networkPlayers)
                {
                    if (player.isAlive)
                    {
                        if (player.role == Role.Hider)
                            aliveHiders++;
                        else if (player.role == Role.Seeker)
                            aliveSeekers++;
                    }
                }

                if (aliveHiders == 0)
                {
                    EndGame(Role.Seeker);
                    return;
                }

                if (aliveSeekers == 0)
                {
                    EndGame(Role.Hider);
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
                        if (player.role == Role.Hider)
                            aliveHiders++;
                        else if (player.role == Role.Seeker)
                            aliveSeekers++;
                    }
                }

                if (aliveHiders == 0)
                {
                    EndGame(Role.Seeker);
                    return;
                }

                if (aliveSeekers == 0)
                {
                    EndGame(Role.Hider);
                }
            }
        }

        private void TimeDurationEnded()
        {
            Debug.Log($"[GameManager] Time's up! Ending game...");
            EndGame(Role.Hider); // Time up, hiders win by default
        }

        private void EndGame(Role winnerRole)
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

            if (timeCountDown)
            {
                timeCountDown.StopCountdownServerRpc();
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void PlayerTaskCompletedServerRpc(ulong playerId, int taskId)
        {
            var newState = networkGameState.Value;
            newState.completedTasks++;
            networkGameState.Value = newState;

            OnTaskProgressUpdated?.Invoke(newState.completedTasks, newState.totalTasks);

            Debug.Log(
                $"[GameManager] Task {taskId} completed by player {playerId}. Progress: {newState.completedTasks}/{newState.totalTasks}");
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
            if (killerFound && victimFound && killer.role == Role.Seeker && victim.role == Role.Hider)
            {
                for (int i = 0; i < networkPlayers.Count; i++)
                {
                    var playerData = networkPlayers[i];
                    if (playerData.clientId == killerId)
                    {
                        playerData.health = Mathf.Min(gameSettings.seekerHealth,
                            playerData.health + gameSettings.hiderKillReward);
                        networkPlayers[i] = playerData;
                        break;
                    }
                }
            }

            Debug.Log($"[GameManager] Player {victimId} killed by {killerId}");
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

            Debug.Log($"[GameManager] Player {playerId} took {damage} damage");
        }

        #endregion

        #region Client RPCs

        [ClientRpc]
        private void ForceObjectSwapClientRpc()
        {
            // Force all hiders to swap objects
            foreach (var kvp in players)
            {
                if (kvp.Value.Role == Role.Hider)
                {
                    if (kvp.Value is IHider hider)
                    {
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
            if (player == null) return;

            players.TryAdd(player.ClientId, player);

            // Nếu là server và chưa có trong network list thì thêm vào
            if (IsServer)
            {
                bool playerExists = false;
                foreach (var networkPlayer in networkPlayers)
                {
                    if (networkPlayer.clientId == player.ClientId)
                    {
                        playerExists = true;
                        break;
                    }
                }

                if (!playerExists)
                {
                    var playerData = new NetworkPlayerData
                    {
                        clientId = player.ClientId,
                        role = player.Role,
                        position = player.Position,
                        isAlive = player.IsAlive,
                        health = 1,
                    };
                    networkPlayers.Add(playerData);
                }
            }

            if (player is RolePlayer rp)
            {
                // Role changed
                Action<Role> onRoleChanged = (newRole) => OnPlayerRoleChanged(rp, newRole);
                rp.OnRoleChanged += onRoleChanged;
                _roleChangedSubs[rp.ClientId] = onRoleChanged;
            }

            if (player is ADefendable def)
            {
                // Health changed
                Action<float, float> onHp = (cur, max) => OnPlayerHealthChanged(player, cur, max);
                def.OnHealthChanged += onHp;
                _hpChangedSubs[player.ClientId] = onHp;

                // Death
                Action<IDefendable, IAttackable> onDied = (self, killer) =>
                {
                    if (!IsServer) return; // server-authority
                    var victim = player as RolePlayer;
                    NotifyPlayerDiedServer(victim, killer);
                };
                def.OnDied += onDied;
                _diedSubs[player.ClientId] = onDied;
            }

            Debug.Log($"[GameManager] Subscribed callbacks for player {player.ClientId}");


            Debug.Log($"[GameManager] Player {player.ClientId} registered. Total: {players.Count}");
        }

        public void RemovePlayer(IGamePlayer player)
        {
            if (player == null) return;

            var clientId = player.ClientId;
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

            if (player is RolePlayer rp && _roleChangedSubs.TryGetValue(rp.ClientId, out var roleDel))
            {
                rp.OnRoleChanged -= roleDel;
                _roleChangedSubs.Remove(rp.ClientId);
            }

            if (_hpChangedSubs.TryGetValue(player.ClientId, out var hpDel))
            {
                if (player is ADefendable def) def.OnHealthChanged -= hpDel;
                _hpChangedSubs.Remove(player.ClientId);
            }

            if (_diedSubs.TryGetValue(player.ClientId, out var diedDel))
            {
                if (player is ADefendable def) def.OnDied -= diedDel;
                _diedSubs.Remove(player.ClientId);
            }

            Debug.Log($"[GameManager] Player {clientId} removed");
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

        public bool CanAssignRole(ulong clientId, Role newRole)
        {
            // Validate role assignment based on game rules
            if (CurrentState != GameState.Preparation)
            {
                return false; // Can only assign roles during preparation or lobby
            }

            // Check if player exists
            if (!players.ContainsKey(clientId))
            {
                return false;
            }

            // Check current role counts
            int seekerCount = 0;
            int hiderCount = 0;

            foreach (var playerData in networkPlayers)
            {
                if (playerData.clientId == clientId) continue; // Skip current player

                if (playerData.role == Role.Seeker)
                    seekerCount++;
                else if (playerData.role == Role.Hider)
                    hiderCount++;
            }

            // Enforce role balance (at least 1 seeker, max 1/3 seekers)
            int totalPlayers = networkPlayers.Count;
            int maxSeekers = Mathf.Max(1, totalPlayers / 3);

            if (newRole == Role.Seeker && seekerCount >= maxSeekers)
            {
                return false;
            }

            return true;
        }

        public void OnPlayerTaskCompleted(ulong clientId, int taskId)
        {
            if (!IsServer) return;

            // Validate the task completion
            if (taskIdMapping.ContainsKey(taskId))
            {
                var task = taskIdMapping[taskId];

                // Mark task as completed and update progress
                PlayerTaskCompletedServerRpc(clientId, taskId);
            }
            else
            {
                Debug.LogWarning($"[GameManager] Invalid task ID {taskId} for player {clientId}");
            }
        }

        public bool IsTaskValidAtPosition(int taskId, Vector3 playerPosition, ulong clientId)
        {
            // Check if task exists and is accessible at the given position
            if (!taskIdMapping.ContainsKey(taskId))
            {
                return false;
            }

            var task = taskIdMapping[taskId];

            // Check distance to task (implement based on your task system)
            float maxTaskDistance = 5f; // Configurable
            float distance = Vector3.Distance(playerPosition, task.Position);

            return distance <= maxTaskDistance;
        }

        public void CheckHiderWinCondition(ulong clientId)
        {
            if (!IsServer) return;

            // Check if all tasks are completed
            if (networkGameState.Value.completedTasks >= networkGameState.Value.totalTasks)
            {
                EndGame(Role.Hider);
            }
        }

        public List<IGamePlayer> GetAlivePlayers()
        {
            return players.Values.Where(p => p.IsAlive).ToList();
        }

        public List<IGamePlayer> GetAllPlayers()
        {
            return players.Values.ToList();
        }

        public void OnHiderCaught(ulong hiderClientId, ulong seekerClientId)
        {
            if (!IsServer) return;

            // Mark hider as caught/dead
            PlayerKilledServerRpc(seekerClientId, hiderClientId);

            Debug.Log($"[GameManager] Hider {hiderClientId} caught by seeker {seekerClientId}");
        }

        public IGamePlayer GetPlayerByClientId(ulong clientId)
        {
            players.TryGetValue(clientId, out var player);
            return player;
        }

        public List<IGamePlayer> GetPlayersByRole(Role role)
        {
            return players.Values.Where(p => p.Role == role).ToList();
        }

        public void TriggerSeekerWin()
        {
            if (!IsServer) return;

            EndGame(Role.Seeker);
        }

        #endregion


        #region Per-player Handlers

        private void OnPlayerRoleChanged(RolePlayer player, Role newRole)
        {
            if (!IsServer) return;

            // Cập nhật networkPlayers entry tương ứng
            for (int i = 0; i < networkPlayers.Count; i++)
            {
                if (networkPlayers[i].clientId == player.ClientId)
                {
                    var d = networkPlayers[i];
                    d.role = newRole;
                    networkPlayers[i] = d;
                    break;
                }
            }

            // (tuỳ chọn) gửi ClientRpc để cập nhật HUD
            RoleChangedClientRpc(player.ClientId, newRole);
        }

        [ClientRpc]
        private void RoleChangedClientRpc(ulong clientId, Role newRole)
        {
            // TODO: HUD / icon / marker
        }

        private void OnPlayerHealthChanged(IGamePlayer player, float cur, float max)
        {
            if (!IsServer) return;

            for (int i = 0; i < networkPlayers.Count; i++)
            {
                if (networkPlayers[i].clientId == player.ClientId)
                {
                    var d = networkPlayers[i];
                    d.health = Mathf.Clamp(cur, 0, max);
                    d.isAlive = d.health > 0f;
                    networkPlayers[i] = d;
                    break;
                }
            }
        }

        #endregion

        #region Death pipe (server-authoritative)

        /// <summary>
        /// Được gọi khi ADefendable.OnDied bắn, hoặc RolePlayer override OnDeath gọi vào.
        /// Hợp nhất thành 1 chỗ để cập nhật killer/victim & win conditions.
        /// </summary>
        public void NotifyPlayerDiedServer(RolePlayer victim, IAttackable killer)
        {
            if (!IsServer || victim == null) return;

            ulong victimId = victim.ClientId;
            ulong killerId = 0;

            // Thử resolve killerId nếu killer là một NetworkObject của player
            if (killer is MonoBehaviour mb && mb.TryGetComponent(out NetworkObject kNob))
            {
                // Nếu killer gắn trên RolePlayer/weapon của RolePlayer
                var killerPlayer = kNob.GetComponentInParent<RolePlayer>();
                if (killerPlayer != null) killerId = killerPlayer.ClientId;
            }

            // Reuse hàm bạn đã có để cập nhật list + heal seeker nếu giết hider
            PlayerKilledServerRpc(killerId, victimId); // đã tồn tại ở code của bạn

            // Kiểm tra điều kiện thắng
            CheckWinConditions();
        }

        #endregion

        #region Optional: gom các event tĩnh của role để bắc cầu UI

        private void HandleHiderTaskProgressChanged(int completed, int total)
        {
            // bắc cầu ra OnTaskProgressUpdated đã có sẵn trong GameManager
            OnTaskProgressUpdated?.Invoke(completed, total);
        }

        private void HandleSeekerCaughtChanged(int caught)
        {
            // tuỳ bạn: cập nhật một HUD tổng/announce
            Debug.Log($"[GM] Seekers caught hiders: {caught}");
        }

        #endregion


        #region Testing

        [Header("Testing")] [SerializeField] private PlayerController playerPrefab;

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

            // Assign random role for testing
            var role = UnityEngine.Random.value > 0.5f ? Role.Hider : Role.Seeker;
            playerInstance.SetRole(role);
        }

        #endregion
    }
}
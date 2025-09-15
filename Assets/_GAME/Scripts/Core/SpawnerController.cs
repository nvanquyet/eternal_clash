using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using _GAME.Scripts.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace _GAME.Scripts.Core
{
    /// <summary>
    /// Base SpawnerController - Handles scene-based spawning for gameplay scenes
    /// Players are expected to already be connected when scene loads
    /// </summary>
    public class SpawnerController : NetworkBehaviour
    {
        [Header("Prefabs")] [SerializeField] protected NetworkObject playerPrefab;

        [Header("Spawn Points")] [SerializeField]
        protected Transform[] spawnPoints;

        [Header("Settings")] [SerializeField] protected float spawnDelay = 0.5f;
        [SerializeField] protected float clientReadyTimeout = 2.5f;
        [SerializeField] protected bool debugMode = false;

        // Spawn point management
        protected readonly List<Transform> available = new();
        protected readonly List<Transform> usedSpawnPoints = new();
        protected readonly Dictionary<ulong, Transform> slotByClient = new();

        // Player tracking
        protected readonly Dictionary<ulong, NetworkObject> spawnedPlayers = new();
        protected readonly Dictionary<ulong, ClientSpawnState> clientStates = new();

        // State tracking
        protected bool _isInitialized = false;
        protected bool _eventsRegistered = false;
        protected bool _sceneFullyLoaded = false;
        
        // Callback tracking to prevent multiple calls
        protected bool _hasCalledFinishSpawning = false;
        protected Coroutine _spawnWatchdog = null;

        // Callbacks
        public Action<NetworkObject> OnPlayerSpawn;
        public Action<NetworkObject> OnPlayerDespawn;
        public Action OnFinishSpawning;

        // Public Properties
        public int SpawnedPlayerCount => spawnedPlayers.Count;
        public List<NetworkObject> SpawnedPlayers => new List<NetworkObject>(spawnedPlayers.Values);
        
        
        public Dictionary<ulong, T> GetSpawnedPlayersDictionary<T>() where T : NetworkBehaviour
        {
            //Return a dictionary of clientId to T component from spawned players
            return spawnedPlayers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetComponent<T>());
        }
        
        // CLIENT SPAWN STATE ENUM
        protected enum ClientSpawnState
        {
            Connected, // Vừa kết nối
            WaitingForScene, // Chờ scene load
            ReadyToSpawn, // Sẵn sàng spawn  
            Spawning, // Đang spawn
            Spawned, // Đã spawn xong
            Disconnected // Đã disconnect
        }

        #region Unity Lifecycle

        protected virtual void Awake()
        {
            if (debugMode) Debug.Log($"[{GetType().Name}] Awake - Initializing spawner");
            ValidateSetup();
            InitializeSpawnPoints();
        }

        protected virtual void Start()
        {
            RegisterEarlyCallbacks();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            UnregisterEarlyCallbacks();
        }

        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            if (!_isInitialized)
            {
                Debug.LogError($"[{GetType().Name}] Not properly initialized!");
                return;
            }

            if (!IsServer) return;

            Debug.Log($"[{GetType().Name}] Server spawning - Scene: {gameObject.scene.name}");
            RegisterServerCallbacks();
            StartCoroutine(InitializeSceneSpawning());
        }

        public override void OnNetworkDespawn()
        {
            UnregisterAllCallbacks();
            if (IsServer) CleanupAllPlayers();
        }

        #endregion

        #region SCENE-BASED SPAWNING SYSTEM

        /// <summary>
        /// Initialize spawning for gameplay scenes - spawn all existing connected clients
        /// </summary>
        protected virtual IEnumerator InitializeSceneSpawning()
        {
            // Wait for network to be fully ready
            yield return new WaitForSeconds(0.1f);
            
            _sceneFullyLoaded = true;
            Debug.Log($"[{GetType().Name}] Scene spawning system initialized");
            
            ProcessExistingClients();
        }

        /// <summary>
        /// Process all existing connected clients for scene-based spawning
        /// </summary>
        protected virtual void ProcessExistingClients()
        {
            var nm = NetworkManager.Singleton;
            if (nm?.ConnectedClients == null)
            {
                Debug.LogWarning($"[{GetType().Name}] No NetworkManager available");
                TryCallFinishSpawning();
                return;
            }

            var clientsToSpawn = new List<ulong>();

            foreach (var clientId in nm.ConnectedClients.Keys)
            {
                // Initialize state for existing clients
                if (!clientStates.ContainsKey(clientId))
                {
                    clientStates[clientId] = ClientSpawnState.ReadyToSpawn;
                }

                if (ShouldSpawnClient(clientId))
                {
                    clientsToSpawn.Add(clientId);
                }
            }

            Debug.Log($"[{GetType().Name}] Processing {nm.ConnectedClients.Count} clients - {clientsToSpawn.Count} ready to spawn");

            if (clientsToSpawn.Count > 0)
            {
                StartCoroutine(SpawnMultipleClients(clientsToSpawn));
            }
            else
            {
                StartSpawnWatchdog();
            }
        }

        /// <summary>
        /// Watchdog to handle cases where no immediate spawning occurs but callback still needs to be called
        /// </summary>
        protected virtual void StartSpawnWatchdog()
        {
            if (_spawnWatchdog != null) StopCoroutine(_spawnWatchdog);
            _spawnWatchdog = StartCoroutine(SpawnWatchdog());
        }

        protected virtual IEnumerator SpawnWatchdog()
        {
            // Wait a bit to see if any spawning occurs
            yield return new WaitForSeconds(spawnDelay + 0.5f);

            var nm = NetworkManager.Singleton;
            if (nm?.ConnectedClients != null)
            {
                int connectedCount = nm.ConnectedClients.Count;
                int spawnedCount = SpawnedPlayerCount;
                
                if (debugMode) Debug.Log($"[{GetType().Name}] SpawnWatchdog - Connected: {connectedCount}, Spawned: {spawnedCount}");
                
                // If all connected clients have been spawned or no clients to spawn, call finish
                if (spawnedCount >= connectedCount || connectedCount == 0)
                {
                    TryCallFinishSpawning();
                }
            }
            else
            {
                TryCallFinishSpawning();
            }
        }

        /// <summary>
        /// Safely call OnFinishSpawning only once
        /// </summary>
        protected virtual void TryCallFinishSpawning()
        {
            if (!_hasCalledFinishSpawning)
            {
                _hasCalledFinishSpawning = true;
                Debug.Log($"[{GetType().Name}] All spawning completed - calling OnFinishSpawning");
                OnFinishSpawning?.Invoke();
            }
        }

        protected virtual bool ShouldSpawnClient(ulong clientId)
        {
            if (!_sceneFullyLoaded) return false;
            if (spawnedPlayers.ContainsKey(clientId)) return false;

            var state = GetClientState(clientId);
            return state == ClientSpawnState.Connected || state == ClientSpawnState.ReadyToSpawn;
        }

        protected virtual IEnumerator SpawnMultipleClients(List<ulong> clientIds)
        {
            var spawnTasks = new List<Coroutine>();

            foreach (var clientId in clientIds)
            {
                if (ShouldSpawnClient(clientId))
                {
                    SetClientState(clientId, ClientSpawnState.Spawning);
                    spawnTasks.Add(StartCoroutine(SpawnSingleClient(clientId)));
                }
            }

            if (debugMode) Debug.Log($"[{GetType().Name}] Waiting for {spawnTasks.Count} spawn tasks to complete");

            // Wait for all spawns to complete
            while (spawnTasks.Any(task => task != null))
            {
                yield return new WaitForSeconds(0.1f);
            }

            Debug.Log($"[{GetType().Name}] All spawn tasks completed");
            TryCallFinishSpawning();
        }

        protected virtual IEnumerator SpawnSingleClient(ulong clientId)
        {
            yield return new WaitForSeconds(spawnDelay);

            // Double check conditions after delay
            if (!ShouldSpawnClient(clientId) && GetClientState(clientId) != ClientSpawnState.Spawning)
            {
                if (debugMode) Debug.Log($"[{GetType().Name}] Skipping spawn for client {clientId} - conditions changed");
                yield break;
            }

            TrySpawnPlayer(clientId);
        }

        #endregion

        #region Callback Registration

        protected virtual void RegisterEarlyCallbacks()
        {
            // Base implementation - derived classes can add their specific callbacks
        }
        
        protected virtual void UnregisterEarlyCallbacks()
        {
            // Base implementation - derived classes can add their specific callbacks
        }
        
        

        protected virtual void RegisterServerCallbacks()
        {
            if (!IsServer || _eventsRegistered) return;

            var nm = NetworkManager.Singleton;
            if (nm?.SceneManager != null)
            {
                nm.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;
                nm.SceneManager.OnSynchronizeComplete += OnSceneSynchronizeComplete;
                if (debugMode) Debug.Log($"[{GetType().Name}] Registered scene callbacks");
            }

            _eventsRegistered = true;
        }

        protected virtual void UnregisterAllCallbacks()
        {
            if (!_eventsRegistered) return;

            var nm = NetworkManager.Singleton;
            if (nm?.SceneManager != null)
            {
                nm.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
                nm.SceneManager.OnSynchronizeComplete -= OnSceneSynchronizeComplete;
            }

            _eventsRegistered = false;
        }

        #endregion

        #region Scene Event Handlers

        protected virtual void OnSceneLoadCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!IsServer) return;

            var currentSceneName = SceneManager.GetActiveScene().name;
            if (sceneName != currentSceneName && sceneName != gameObject.scene.name) return;

            Debug.Log($"[{GetType().Name}] Scene '{sceneName}' loaded - {clientsCompleted.Count} completed, {clientsTimedOut.Count} timed out");

            // Update states for clients that completed scene load
            foreach (var clientId in clientsCompleted)
            {
                SetClientState(clientId, ClientSpawnState.ReadyToSpawn);
            }

            foreach (var clientId in clientsTimedOut)
            {
                Debug.LogWarning($"[{GetType().Name}] Client {clientId} timed out during scene load");
                SetClientState(clientId, ClientSpawnState.ReadyToSpawn); // Still try to spawn
            }

            ProcessExistingClients();
        }

        protected virtual void OnSceneSynchronizeComplete(ulong clientId)
        {
            if (!IsServer) return;

            var currentState = GetClientState(clientId);
            if (currentState == ClientSpawnState.Connected)
            {
                if (debugMode) Debug.Log($"[{GetType().Name}] Scene sync complete for client {clientId}");
                SetClientState(clientId, ClientSpawnState.ReadyToSpawn);
                ProcessExistingClients();
            }
        }

        #endregion

        #region State Management

        protected ClientSpawnState GetClientState(ulong clientId)
        {
            return clientStates.TryGetValue(clientId, out var value) ? value : ClientSpawnState.Disconnected;
        }

        protected void SetClientState(ulong clientId, ClientSpawnState newState)
        {
            var oldState = GetClientState(clientId);
            if (oldState != newState)
            {
                clientStates[clientId] = newState;
                if (debugMode) Debug.Log($"[{GetType().Name}] Client {clientId}: {oldState} → {newState}");
            }
        }

        #endregion

        #region Spawning Logic

        protected virtual void ValidateSetup()
        {
            if (playerPrefab == null)
            {
                Debug.LogError($"[{GetType().Name}] Player prefab is not assigned!");
                return;
            }

            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogError($"[{GetType().Name}] No spawn points assigned!");
                return;
            }

            _isInitialized = true;
        }

        protected virtual void InitializeSpawnPoints()
        {
            available.Clear();
            usedSpawnPoints.Clear();
            slotByClient.Clear();

            if (spawnPoints != null)
            {
                available.AddRange(spawnPoints);
                if (debugMode) Debug.Log($"[{GetType().Name}] Initialized {available.Count} spawn points");
            }
        }
        

        protected virtual void TrySpawnPlayer(ulong clientId)
        {
            if (!IsServer) return;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.ConnectedClients.ContainsKey(clientId))
            {
                Debug.LogWarning($"[{GetType().Name}] Client {clientId} no longer connected");
                SetClientState(clientId, ClientSpawnState.Disconnected);
                return;
            }

            if (spawnedPlayers.ContainsKey(clientId))
            {
                SetClientState(clientId, ClientSpawnState.Spawned);
                return;
            }

            var client = nm.ConnectedClients[clientId];
            if (client.PlayerObject != null && client.PlayerObject.IsSpawned)
            {
                spawnedPlayers[clientId] = client.PlayerObject;
                SetClientState(clientId, ClientSpawnState.Spawned);
                OnPlayerSpawn?.Invoke(client.PlayerObject);
                return;
            }

            try
            {
                var (position, rotation, spawnPoint) = GetSpawnTransform();
                Debug.Log($"[{GetType().Name}] Spawning player {clientId} at {position}");

                var playerInstance = Instantiate(playerPrefab, position, rotation);

                // Add identity sync if needed
                if (ShouldAddIdentitySync() && playerInstance.GetComponent<IdentitySyncComponent>() == null)
                {
                    playerInstance.gameObject.AddComponent<IdentitySyncComponent>();
                }

                playerInstance.SpawnAsPlayerObject(clientId);
                spawnedPlayers[clientId] = playerInstance;
                SetClientState(clientId, ClientSpawnState.Spawned);

                if (spawnPoint != null)
                {
                    usedSpawnPoints.Add(spawnPoint);
                    slotByClient[clientId] = spawnPoint;
                }

                OnPlayerSpawn?.Invoke(playerInstance);

                var receiver = playerInstance.GetComponent<IReceiveSpawnChoice>();
                receiver?.OnSpawnWithChoice(new SpawnChoice { Position = position, Rotation = rotation });

                Debug.Log($"[{GetType().Name}] Successfully spawned player for client {clientId}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[{GetType().Name}] Failed to spawn player for client {clientId}: {e.Message}");
                SetClientState(clientId, ClientSpawnState.Connected); // Reset để retry
            }
        }

        protected virtual bool ShouldAddIdentitySync()
        {
            return false; // Base spawner doesn't add identity sync by default
        }

        protected virtual (Vector3 position, Quaternion rotation, Transform spawnPoint) GetSpawnTransform()
        {
            if (available.Count == 0)
            {
                Debug.LogWarning($"[{GetType().Name}] No available spawn points, using fallback");
                var fallbackPos = new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
                return (fallbackPos, Quaternion.identity, null);
            }

            var index = Random.Range(0, available.Count);
            var spawnPoint = available[index];
            available.RemoveAt(index);

            return (spawnPoint.position, spawnPoint.rotation, spawnPoint);
        }

        protected virtual void CleanupClient(ulong clientId)
        {
            clientStates.Remove(clientId);

            if (spawnedPlayers.TryGetValue(clientId, out var playerObject))
            {
                OnPlayerDespawn?.Invoke(playerObject);
                RestoreSpawnPoint(clientId, playerObject.transform.position);

                if (playerObject != null && playerObject.IsSpawned)
                {
                    playerObject.Despawn(destroy: true);
                }

                spawnedPlayers.Remove(clientId);
            }

            slotByClient.Remove(clientId);
        }

        protected virtual void CleanupAllPlayers()
        {
            var clientsToCleanup = new List<ulong>(spawnedPlayers.Keys);
            foreach (var clientId in clientsToCleanup)
            {
                CleanupClient(clientId);
            }

            InitializeSpawnPoints();
        }

        protected virtual void RestoreSpawnPoint(ulong clientId, Vector3 playerLastPos)
        {
            if (slotByClient.TryGetValue(clientId, out var used))
            {
                usedSpawnPoints.Remove(used);
                if (used != null && !available.Contains(used))
                {
                    available.Add(used);
                }
            }
        }

        #endregion

        #region Debug Methods

        [ContextMenu("Force Respawn All")]
        protected virtual void ForceRespawnAll()
        {
            if (!IsServer) return;

            Debug.Log($"[{GetType().Name}] Force respawning all players");
            var clientIds = new List<ulong>(spawnedPlayers.Keys);
            foreach (var clientId in clientIds)
            {
                CleanupClient(clientId);
                SetClientState(clientId, ClientSpawnState.ReadyToSpawn);
            }

            ProcessExistingClients();
        }

        [ContextMenu("Debug Dump State")]
        protected virtual void DebugDumpState()
        {
            Debug.Log($"[{GetType().Name}] State Dump:\n" +
                      $"  Available: {available.Count}, Used: {usedSpawnPoints.Count}, Spawned: {spawnedPlayers.Count}\n" +
                      $"  Scene Loaded: {_sceneFullyLoaded}, Events Registered: {_eventsRegistered}, Is Server: {IsServer}");

            foreach (var kvp in clientStates)
            {
                Debug.Log($"    Client {kvp.Key}: {kvp.Value}");
            }
        }

        #endregion

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                var container = transform.Find("SpawnPoints");
                if (container != null)
                {
                    var list = new List<Transform>();
                    foreach (Transform t in container)
                        list.Add(t);
                    spawnPoints = list.ToArray();
                }
                else
                {
                    var list = new List<Transform>();
                    foreach (Transform t in transform)
                        list.Add(t);
                    spawnPoints = list.ToArray();
                }
            }
        }
#endif
    }

    public interface IReceiveSpawnChoice
    {
        void OnSpawnWithChoice(SpawnChoice choice);
    }

    public struct SpawnChoice
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }
}
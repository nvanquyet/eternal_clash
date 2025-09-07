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
        [Header("Prefabs")]
        [SerializeField] protected NetworkObject playerPrefab;

        [Header("Spawn Points")]
        [SerializeField] protected Transform[] spawnPoints;
        
        [Header("Settings")]
        [SerializeField] protected float spawnDelay = 0.5f;
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
        
        // Callbacks
        public Action<NetworkObject> OnPlayerSpawn;
        public Action<NetworkObject> OnPlayerDespawn;
        public Action OnFinishSpawning;
        
        // Public Properties
        public int SpawnedPlayerCount => spawnedPlayers.Count;
        public List<NetworkObject> SpawnedPlayers => new List<NetworkObject>(spawnedPlayers.Values);

        // ✅ CLIENT SPAWN STATE ENUM
        protected enum ClientSpawnState
        {
            Connected,        // Vừa kết nối
            WaitingForScene, // Chờ scene load
            ReadyToSpawn,    // Sẵn sàng spawn  
            Spawning,        // Đang spawn
            Spawned,         // Đã spawn xong
            Disconnected     // Đã disconnect
        }

        #region Unity Lifecycle
        
        protected virtual void Awake()
        { 
            ValidateSetup();
            InitializeSpawnPoints();
            
            if (debugMode) Debug.Log($"[{GetType().Name}] Awake completed");
        }

        protected virtual void Start()
        {
            RegisterEarlyCallbacks();
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

            if (debugMode) 
                Debug.Log($"[{GetType().Name}] NetworkSpawn - IsServer: {IsServer}, Scene: {gameObject.scene.name}");

            if (!IsServer) return;

            RegisterServerCallbacks();
            
            // ✅ SCENE-BASED INITIALIZATION - spawn existing players immediately
            StartCoroutine(InitializeSceneSpawning());
        }

        public override void OnNetworkDespawn()
        {
            if (debugMode) Debug.Log($"[{GetType().Name}] NetworkDespawn");
            
            UnregisterAllCallbacks();
            
            if (IsServer)
            {
                CleanupAllPlayers();
            }
        }

        #endregion

        #region ✅ SCENE-BASED SPAWNING SYSTEM

        /// <summary>
        /// Initialize spawning for gameplay scenes - spawn all existing connected clients
        /// </summary>
        protected virtual IEnumerator InitializeSceneSpawning()
        {
            // Wait for network to be fully ready
            yield return new WaitForSeconds(0.1f);
            
            // Mark scene as loaded
            _sceneFullyLoaded = true;
            
            if (debugMode) Debug.Log($"[{GetType().Name}] Scene spawning system initialized");
            
            // Process all existing clients (they should already be connected from previous scene)
            ProcessExistingClients();
        }

        /// <summary>
        /// Process all existing connected clients for scene-based spawning
        /// </summary>
        protected virtual void ProcessExistingClients()
        {
            var nm = NetworkManager.Singleton;
            if (nm?.ConnectedClients == null) return;

            var clientsToSpawn = new List<ulong>();
            
            foreach (var clientId in nm.ConnectedClients.Keys)
            {
                // Initialize state for existing clients
                if (!clientStates.ContainsKey(clientId))
                {
                    clientStates[clientId] = ClientSpawnState.ReadyToSpawn;
                    if (debugMode) Debug.Log($"[{GetType().Name}] Preparing to spawn existing client {clientId}");
                }
                
                // Check if ready to spawn
                if (ShouldSpawnClient(clientId))
                {
                    clientsToSpawn.Add(clientId);
                }
            }

            // Spawn all ready clients
            if (clientsToSpawn.Count > 0)
            {
                StartCoroutine(SpawnMultipleClients(clientsToSpawn));
            }
            else if (debugMode)
            {
                Debug.Log($"[{GetType().Name}] No clients to spawn in this scene");
            }
        }

        protected virtual bool ShouldSpawnClient(ulong clientId)
        {
            // ✅ SINGLE SOURCE OF TRUTH
            if (!_sceneFullyLoaded) return false;
            if (spawnedPlayers.ContainsKey(clientId)) return false;
            
            var state = GetClientState(clientId);
            return state == ClientSpawnState.Connected || 
                   state == ClientSpawnState.ReadyToSpawn;
        }

        protected virtual IEnumerator SpawnMultipleClients(List<ulong> clientIds)
        {
            if (debugMode) Debug.Log($"[{GetType().Name}] Spawning {clientIds.Count} clients");
            
            var spawnTasks = new List<Coroutine>();
            
            foreach (var clientId in clientIds)
            {
                if (ShouldSpawnClient(clientId))
                {
                    SetClientState(clientId, ClientSpawnState.Spawning);
                    spawnTasks.Add(StartCoroutine(SpawnSingleClient(clientId)));
                }
            }

            // Wait for all spawns to complete
            while (spawnTasks.Any(task => task != null))
            {
                yield return new WaitForSeconds(0.1f);
            }

            // ✅ SINGLE CALLBACK INVOCATION
            OnFinishSpawning?.Invoke();
        }

        protected virtual IEnumerator SpawnSingleClient(ulong clientId)
        {
            yield return new WaitForSeconds(spawnDelay);
            
            if (!ShouldSpawnClient(clientId) && GetClientState(clientId) != ClientSpawnState.Spawning)
            {
                if (debugMode) Debug.Log($"[{GetType().Name}] Skipping spawn for client {clientId} - conditions changed");
                yield break;
            }

            TrySpawnPlayer(clientId);
        }

        #endregion

        #region Callback Registration

        /// <summary>
        /// Register callbacks that don't depend on network - override in derived classes for specific events
        /// </summary>
        protected virtual void RegisterEarlyCallbacks()
        {
            // Base implementation - derived classes can add their specific callbacks
            if (debugMode) Debug.Log($"[{GetType().Name}] Early callbacks registered");
        }

        /// <summary>
        /// Register server-side callbacks - base scene management only
        /// </summary>
        protected virtual void RegisterServerCallbacks()
        {
            if (!IsServer || _eventsRegistered) return;

            var nm = NetworkManager.Singleton;
            if (nm?.SceneManager != null)
            {
                // Only scene-related callbacks for base controller
                nm.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;
                nm.SceneManager.OnSynchronizeComplete += OnSceneSynchronizeComplete;
            }

            _eventsRegistered = true;
            if (debugMode) Debug.Log($"[{GetType().Name}] Server callbacks registered");
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
            if (debugMode) Debug.Log($"[{GetType().Name}] All callbacks unregistered");
        }

        #endregion

        #region Scene Event Handlers

        /// <summary>
        /// Handle scene load completion - respawn players if needed
        /// </summary>
        protected virtual void OnSceneLoadCompleted(string sceneName, LoadSceneMode mode, 
                                        List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!IsServer) return;
            
            var currentSceneName = SceneManager.GetActiveScene().name;
            if (sceneName != currentSceneName && sceneName != gameObject.scene.name) return;

            if (debugMode) 
                Debug.Log($"[{GetType().Name}] Scene '{sceneName}' load completed. " +
                         $"Clients completed: {clientsCompleted.Count}, timed out: {clientsTimedOut.Count}");

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

            // Process ready clients
            ProcessExistingClients();
        }

        protected virtual void OnSceneSynchronizeComplete(ulong clientId)
        {
            if (!IsServer) return;

            if (debugMode) Debug.Log($"[{GetType().Name}] Scene synchronization complete for client {clientId}");
            
            // Update state and try to spawn
            if (GetClientState(clientId) == ClientSpawnState.Connected)
            {
                SetClientState(clientId, ClientSpawnState.ReadyToSpawn);
                ProcessExistingClients();
            }
        }

        #endregion

        #region State Management

        protected ClientSpawnState GetClientState(ulong clientId)
        {
            return clientStates.TryGetValue(clientId, out var state) ? state : ClientSpawnState.Disconnected;
        }

        protected void SetClientState(ulong clientId, ClientSpawnState newState)
        {
            var oldState = GetClientState(clientId);
            if (oldState != newState)
            {
                clientStates[clientId] = newState;
                if (debugMode) 
                    Debug.Log($"[{GetType().Name}] Client {clientId}: {oldState} → {newState}");
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

        public static List<T> GetAllSpawnedPlayers<T>(List<NetworkObject> originList) where T : NetworkBehaviour
        {
            return (from obj in originList where obj != null select obj.GetComponent<T>() into component where component != null select component).ToList();
        }

        protected virtual void TrySpawnPlayer(ulong clientId)
        {
            if (!IsServer) return;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.ConnectedClients.ContainsKey(clientId))
            {
                if (debugMode) Debug.LogWarning($"[{GetType().Name}] Client {clientId} no longer connected");
                SetClientState(clientId, ClientSpawnState.Disconnected);
                return;
            }

            if (spawnedPlayers.ContainsKey(clientId))
            {
                if (debugMode) Debug.LogWarning($"[{GetType().Name}] Player {clientId} already spawned");
                SetClientState(clientId, ClientSpawnState.Spawned);
                return;
            }

            var client = nm.ConnectedClients[clientId];
            if (client.PlayerObject != null && client.PlayerObject.IsSpawned)
            {
                if (debugMode) Debug.Log($"[{GetType().Name}] Client {clientId} already has player object");
                spawnedPlayers[clientId] = client.PlayerObject;
                SetClientState(clientId, ClientSpawnState.Spawned);
                OnPlayerSpawn?.Invoke(client.PlayerObject);
                return;
            }

            try
            {
                var (position, rotation, spawnPoint) = GetSpawnTransform();
                
                if (debugMode) Debug.Log($"[{GetType().Name}] Spawning player for client {clientId} at {position}");

                var playerInstance = Instantiate(playerPrefab, position, rotation);
                
                // Add identity sync if needed (can be overridden in derived classes)
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

                if (debugMode) Debug.Log($"[{GetType().Name}] Successfully spawned player for client {clientId}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[{GetType().Name}] Failed to spawn player for client {clientId}: {e.Message}");
                SetClientState(clientId, ClientSpawnState.Connected); // Reset để retry
            }
        }

        /// <summary>
        /// Override in derived classes to control identity sync component addition
        /// </summary>
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

            if (debugMode) Debug.Log($"[{GetType().Name}] Cleaned up client {clientId}");
        }

        protected virtual void CleanupAllPlayers()
        {
            if (debugMode) Debug.Log($"[{GetType().Name}] Cleaning up all {spawnedPlayers.Count} players");

            var clientsToCleanup = new List<ulong>(spawnedPlayers.Keys);
            foreach (var clientId in clientsToCleanup)
            {
                CleanupClient(clientId);
            }

            InitializeSpawnPoints();
            // OnFinishSpawning chỉ gọi khi thực sự cleanup tất cả
            if (clientsToCleanup.Count > 0)
            {
                OnFinishSpawning?.Invoke();
            }
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

                if (debugMode)
                {
                    Debug.Log($"[{GetType().Name}] Restored spawn point for client {clientId}");
                }
            }
        }

        #endregion

        #region Debug Methods
        
        [ContextMenu("Force Respawn All")]
        protected virtual void ForceRespawnAll()
        {
            if (!IsServer) return;

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
            Debug.Log($"[{GetType().Name}] State:\n" +
                     $"  Available: {available.Count}\n" +
                     $"  Used: {usedSpawnPoints.Count}\n" +
                     $"  Spawned: {spawnedPlayers.Count}\n" +
                     $"  Client States: {clientStates.Count}\n" +
                     $"  Scene Loaded: {_sceneFullyLoaded}\n" +
                     $"  Events Registered: {_eventsRegistered}");
                     
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
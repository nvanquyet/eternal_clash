using System.Collections;
using System.Collections.Generic;
using _GAME.Scripts.Networking;
using _GAME.Scripts.Networking.Lobbies;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace _GAME.Scripts.Core.WaitingRoom
{
    public class WaitingRoomSpawner : NetworkBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private NetworkObject playerPrefab;

        [Header("Spawn Points")]
        [SerializeField] private Transform[] spawnPoints;
        
        [Header("Settings")]
        [SerializeField] private float spawnDelay = 0.5f;
        [SerializeField] private float clientReadyTimeout = 2.5f;
        [SerializeField] private bool debugMode = false;

        // Spawn point management
        private readonly List<Transform> available = new();
        private readonly List<Transform> usedSpawnPoints = new();
        private readonly Dictionary<ulong, Transform> slotByClient = new();

        // Player tracking
        private readonly Dictionary<ulong, NetworkObject> spawnedPlayers = new();
        private readonly HashSet<ulong> pendingSpawns = new();
        private readonly Dictionary<ulong, float> clientConnectTimes = new();

        // State
        private bool _isInitialized = false;

        private void Awake()
        {
            ValidateSetup();
            InitializeSpawnPoints();
        }

        private void ValidateSetup()
        {
            if (playerPrefab == null)
            {
                Debug.LogError("[WaitingRoomSpawner] Player prefab is not assigned!");
                return;
            }

            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogError("[WaitingRoomSpawner] No spawn points assigned!");
                return;
            }

            _isInitialized = true;
        }

        private void InitializeSpawnPoints()
        {
            available.Clear();
            usedSpawnPoints.Clear();
            slotByClient.Clear();

            if (spawnPoints != null)
            {
                available.AddRange(spawnPoints);
                if (debugMode) Debug.Log($"[WaitingRoomSpawner] Initialized {available.Count} spawn points");
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!_isInitialized)
            {
                Debug.LogError("[WaitingRoomSpawner] Not properly initialized!");
                return;
            }

            if (!IsServer)
            {
                if (debugMode) Debug.Log("[WaitingRoomSpawner] Client instance spawned");
                return;
            }

            if (debugMode) Debug.Log("[WaitingRoomSpawner] Server instance spawned, registering events");

            RegisterEvents();
            
            // Handle any clients that connected before we spawned
            HandleExistingClients();

            // Start timeout monitor
            StartCoroutine(MonitorClientReadyTimeout());
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                UnregisterEvents();
                CleanupAllPlayers();
            }
        }

        private void RegisterEvents()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientConnectedCallback += OnClientConnected;
                nm.OnClientDisconnectCallback += OnClientDisconnected;
                nm.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;
            }

            // Lobby events for kick handling
            LobbyEvents.OnPlayerKicked += OnPlayerKicked;
            LobbyEvents.OnLobbyRemoved += OnLobbyRemoved;
        }

        private void UnregisterEvents()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientConnectedCallback -= OnClientConnected;
                nm.OnClientDisconnectCallback -= OnClientDisconnected;
                nm.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
            }

            LobbyEvents.OnPlayerKicked -= OnPlayerKicked;
            LobbyEvents.OnLobbyRemoved -= OnLobbyRemoved;
        }

        private void HandleExistingClients()
        {
            var nm = NetworkManager.Singleton;
            if (nm?.ConnectedClients == null) return;

            foreach (var clientId in nm.ConnectedClients.Keys)
            {
                if (!spawnedPlayers.ContainsKey(clientId))
                {
                    if (debugMode) Debug.Log($"[WaitingRoomSpawner] Handling existing client: {clientId}");
                    OnClientConnected(clientId);
                }
            }
        }

        #region Event Handlers

        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer) return;

            if (debugMode) Debug.Log($"[WaitingRoomSpawner] Client {clientId} connected");
            
            clientConnectTimes[clientId] = Time.time;

            // Don't spawn immediately - wait for scene load completion
            if (debugMode) Debug.Log($"[WaitingRoomSpawner] Waiting for scene load completion for client {clientId}");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            if (debugMode) Debug.Log($"[WaitingRoomSpawner] Client {clientId} disconnected, cleaning up");

            CleanupClient(clientId);
        }

        private void OnSceneLoadCompleted(string sceneName, LoadSceneMode mode, 
                                        List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!IsServer) return;
            
            // Only handle our scene
            if (sceneName != gameObject.scene.name) return;

            if (debugMode) Debug.Log($"[WaitingRoomSpawner] Scene '{sceneName}' loaded. Clients completed: {clientsCompleted.Count}, timed out: {clientsTimedOut.Count}");

            // Spawn for clients that completed scene load
            foreach (var clientId in clientsCompleted)
            {
                StartCoroutine(SpawnPlayerWithDelay(clientId, spawnDelay));
            }

            // Handle timed out clients
            foreach (var clientId in clientsTimedOut)
            {
                Debug.LogWarning($"[WaitingRoomSpawner] Client {clientId} timed out during scene load");
            }
        }

        private void OnPlayerKicked(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message)
        {
            if (!IsServer || player == null) return;

            if (debugMode) Debug.Log($"[WaitingRoomSpawner] Player kicked from lobby: {player.Id}");

            // Find and disconnect the corresponding network client
            var registry = ClientIdentityRegistry.Instance;
            if (registry != null && registry.TryGetClientId(player.Id, out var clientId))
            {
                if (debugMode) Debug.Log($"[WaitingRoomSpawner] Disconnecting kicked player: UGS({player.Id}) -> Client({clientId})");
                NetworkManager.Singleton.DisconnectClient(clientId);
            }
            else
            {
                Debug.LogWarning($"[WaitingRoomSpawner] Could not find network client for kicked player: {player.Id}");
            }
        }

        private void OnLobbyRemoved(Lobby lobby, bool success, string message)
        {
            if (!IsServer || !success) return;

            Debug.Log("[WaitingRoomSpawner] Lobby removed, cleaning up all players");
            CleanupAllPlayers();
        }

        #endregion

        #region Spawning Logic

        private IEnumerator SpawnPlayerWithDelay(ulong clientId, float delay)
        {
            if (delay > 0)
            {
                yield return new WaitForSeconds(delay);
            }

            TrySpawnPlayer(clientId);
        }

        private void TrySpawnPlayer(ulong clientId)
        {
            if (!IsServer) return;

            // Validate client is still connected
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.ConnectedClients.ContainsKey(clientId))
            {
                if (debugMode) Debug.LogWarning($"[WaitingRoomSpawner] Client {clientId} no longer connected, skipping spawn");
                return;
            }

            // Check if already spawned
            if (spawnedPlayers.ContainsKey(clientId))
            {
                if (debugMode) Debug.LogWarning($"[WaitingRoomSpawner] Player {clientId} already spawned");
                return;
            }

            // Check if spawn is pending
            if (pendingSpawns.Contains(clientId))
            {
                if (debugMode) Debug.LogWarning($"[WaitingRoomSpawner] Spawn already pending for client {clientId}");
                return;
            }

            // Check if client already has a player object
            var client = nm.ConnectedClients[clientId];
            if (client.PlayerObject != null && client.PlayerObject.IsSpawned)
            {
                if (debugMode) Debug.Log($"[WaitingRoomSpawner] Client {clientId} already has player object, registering locally");
                spawnedPlayers[clientId] = client.PlayerObject;
                return;
            }

            pendingSpawns.Add(clientId);

            try
            {
                var (position, rotation, spawnPoint) = GetSpawnTransform();
                
                if (debugMode) Debug.Log($"[WaitingRoomSpawner] Spawning player for client {clientId} at {position}");

                var playerInstance = Instantiate(playerPrefab, position, rotation);
                
                // Add identity sync component if not present
                if (playerInstance.GetComponent<IdentitySyncComponent>() == null)
                {
                    playerInstance.gameObject.AddComponent<IdentitySyncComponent>();
                }

                playerInstance.SpawnAsPlayerObject(clientId);

                spawnedPlayers[clientId] = playerInstance;

                if (spawnPoint != null)
                {
                    usedSpawnPoints.Add(spawnPoint);
                    slotByClient[clientId] = spawnPoint;
                }

                if (debugMode) Debug.Log($"[WaitingRoomSpawner] Successfully spawned player for client {clientId}");

                // Notify spawn choice if supported
                var receiver = playerInstance.GetComponent<IReceiveSpawnChoice>();
                receiver?.OnSpawnWithChoice(new SpawnChoice { Position = position, Rotation = rotation });
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WaitingRoomSpawner] Failed to spawn player for client {clientId}: {e.Message}");
            }
            finally
            {
                pendingSpawns.Remove(clientId);
            }
        }

        private (Vector3 position, Quaternion rotation, Transform spawnPoint) GetSpawnTransform()
        {
            if (available.Count == 0)
            {
                Debug.LogWarning("[WaitingRoomSpawner] No available spawn points, using fallback position");
                var fallbackPos = new Vector3(
                    Random.Range(-3f, 3f), 
                    0f, 
                    Random.Range(-3f, 3f)
                );
                return (fallbackPos, Quaternion.identity, null);
            }

            var index = Random.Range(0, available.Count);
            var spawnPoint = available[index];
            available.RemoveAt(index);

            return (spawnPoint.position, spawnPoint.rotation, spawnPoint);
        }

        #endregion

        #region Cleanup Logic

        private void CleanupClient(ulong clientId)
        {
            // Remove from pending spawns
            pendingSpawns.Remove(clientId);
            
            // Remove connect time tracking
            clientConnectTimes.Remove(clientId);

            // Cleanup spawned player
            if (spawnedPlayers.TryGetValue(clientId, out var playerObject))
            {
                RestoreSpawnPoint(clientId, playerObject.transform.position);

                if (playerObject != null && playerObject.IsSpawned)
                {
                    playerObject.Despawn(destroy: true);
                }

                spawnedPlayers.Remove(clientId);
            }

            // Cleanup slot tracking
            slotByClient.Remove(clientId);

            // Cleanup from identity registry
            var registry = ClientIdentityRegistry.Instance;
            registry?.UnregisterByClient(clientId);

            if (debugMode) Debug.Log($"[WaitingRoomSpawner] Cleaned up client {clientId}");
        }

        private void CleanupAllPlayers()
        {
            if (debugMode) Debug.Log($"[WaitingRoomSpawner] Cleaning up all {spawnedPlayers.Count} players");

            var clientsToCleanup = new List<ulong>(spawnedPlayers.Keys);
            foreach (var clientId in clientsToCleanup)
            {
                CleanupClient(clientId);
            }

            // Reset spawn point availability
            InitializeSpawnPoints();
        }

        private void RestoreSpawnPoint(ulong clientId, Vector3 playerLastPos)
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
                    Debug.Log($"[WaitingRoomSpawner] Restored spawn point for client {clientId} at {used.position} (player last at {playerLastPos})");
                }
            }
            else
            {
                if (debugMode) Debug.Log($"[WaitingRoomSpawner] No reserved spawn point to restore for client {clientId}");
            }
        }

        #endregion

        #region Timeouts & Utilities

        private IEnumerator MonitorClientReadyTimeout()
        {
            var wait = new WaitForSeconds(1f);
            while (IsServer)
            {
                var now = Time.time;
                // Copy keys to avoid enumeration issues
                var ids = new List<ulong>(clientConnectTimes.Keys);
                foreach (var clientId in ids)
                {
                    if (spawnedPlayers.ContainsKey(clientId) || pendingSpawns.Contains(clientId))
                        continue;

                    if (now - clientConnectTimes[clientId] > clientReadyTimeout)
                    {
                        Debug.LogWarning($"[WaitingRoomSpawner] Client {clientId} exceeded ready timeout ({clientReadyTimeout}s). Forcing spawn.");
                        TrySpawnPlayer(clientId);
                    }
                }

                yield return wait;
            }
        }
        
        #endregion

        [ContextMenu("Force Respawn All")]
        private void ForceRespawnAll()
        {
            if (!IsServer) return;

            foreach (var kvp in new List<KeyValuePair<ulong, NetworkObject>>(spawnedPlayers))
            {
                CleanupClient(kvp.Key);
                StartCoroutine(SpawnPlayerWithDelay(kvp.Key, 0.1f));
            }
        }

        [ContextMenu("Debug Dump State")]
        private void DebugDumpState()
        {
            Debug.Log($"[WaitingRoomSpawner] State:\n  available: {available.Count}\n  used: {usedSpawnPoints.Count}\n  spawned: {spawnedPlayers.Count}\n  pending: {pendingSpawns.Count}");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                // Try to auto-fill from children named "SpawnPoints" or any direct children
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
                    // Fallback: collect direct children
                    var list = new List<Transform>();
                    foreach (Transform t in transform)
                        list.Add(t);
                    spawnPoints = list.ToArray();
                }
            }
        }
#endif
    }

    /// <summary>
    /// Optional interface for spawn receivers to know about the chosen transform
    /// </summary>
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

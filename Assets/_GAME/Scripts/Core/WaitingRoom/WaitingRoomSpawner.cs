using System.Collections.Generic;
using _GAME.Scripts.Networking;
using _GAME.Scripts.Networking.Lobbies;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace _GAME.Scripts.Core.WaitingRoom
{
    public class WaitingRoomSpawner : NetworkBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private NetworkObject playerPrefab;

        [Header("Spawn Points")]
        [SerializeField] private Transform[] spawnPoints;

        // Quản lý slot
        private readonly List<Transform> available = new();
        private readonly List<Transform> usedSpawnPoints = new();
        private readonly Dictionary<ulong, Transform> slotByClient = new();     // NEW: clientId -> spawnPoint

        // Lưu player đã spawn theo clientId
        private readonly Dictionary<ulong, NetworkObject> spawned = new();

        private void Awake()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogError("[WaitingRoomSpawner] No spawn points assigned.");
                return;
            }
            RefreshAvailableSpawnPoints();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            nm.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;

            LobbyEvents.OnPlayerKicked += OnPlayerKicked;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;

            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
            nm.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;

            LobbyEvents.OnPlayerKicked -= OnPlayerKicked;
        }

        // ===== Relay/Lobby kick -> disconnect, cleanup sẽ chạy qua OnClientDisconnected
        private void OnPlayerKicked(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string _)
        {
            if (!IsServer || player == null) return;

            if (!ClientIdentityRegistry.Instance.TryGetClientId(player.Id, out var clientId))
            {
                Debug.LogWarning($"[WaitingRoomSpawner] Kick: no clientId mapped for UGS {player.Id}");
                return;
            }

            Debug.Log($"[WaitingRoomSpawner] Kicking client {clientId} (UGS {player.Id})");
            NetworkManager.Singleton.DisconnectClient(clientId);
        }

        // ===== Scene load xong mới spawn (chuẩn nhất)
        private void OnSceneLoadCompleted(string sceneName, LoadSceneMode mode,
                                          List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!IsServer) return;
            if (sceneName != gameObject.scene.name) return;

            Debug.Log($"[WaitingRoomSpawner] Scene {sceneName} loaded. Spawning clientsCompleted...");

            foreach (var clientId in clientsCompleted)
            {
                TrySpawnFor(clientId);
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer) return;
            // Không spawn ở đây để tránh spawn trước khi client vào scene.
            Debug.Log($"[WaitingRoomSpawner] Player {clientId} connected.");
            TrySpawnFor(clientId);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            Debug.Log($"[WaitingRoomSpawner] Player {clientId} disconnected. Cleaning up...");

            if (spawned.TryGetValue(clientId, out var player) && player != null)
            {
                RestoreSpawnPoint(clientId, player.transform.position);

                if (player.IsSpawned)
                {
                    player.Despawn(true);
                }
            }

            spawned.Remove(clientId);
            slotByClient.Remove(clientId);
            ClientIdentityRegistry.Instance?.UnregisterByClient(clientId);
        }

        // ===== Core Spawn Logic =====
        private void TrySpawnFor(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            // Client còn kết nối không?
            if (!nm.ConnectedClients.ContainsKey(clientId))
            {
                Debug.LogWarning($"[WaitingRoomSpawner] Client {clientId} not connected, skip spawning.");
                return;
            }

            // Đã spawn trong spawner này?
            if (spawned.ContainsKey(clientId))
            {
                Debug.LogWarning($"[WaitingRoomSpawner] Player {clientId} already spawned (local dict).");
                return;
            }

            // Client đã có PlayerObject ở nơi khác?
            var client = nm.ConnectedClients[clientId];
            if (client.PlayerObject != null && client.PlayerObject.IsSpawned)
            {
                Debug.LogWarning($"[WaitingRoomSpawner] Client {clientId} already has PlayerObject; syncing handle.");
                spawned[clientId] = client.PlayerObject;
                return;
            }

            if (playerPrefab == null)
            {
                Debug.LogError("[WaitingRoomSpawner] playerPrefab is null.");
                return;
            }

            var (pos, rot, spawnPoint) = GetSpawnTR();

            var instance = Instantiate(playerPrefab, pos, rot);
            instance.SpawnAsPlayerObject(clientId);

            spawned[clientId] = instance;

            if (spawnPoint != null)
            {
                usedSpawnPoints.Add(spawnPoint);
                slotByClient[clientId] = spawnPoint;   // NEW: ghi lại chỗ của client
            }

            Debug.Log($"[WaitingRoomSpawner] Spawned player {clientId} at {pos}");

            var receiver = instance.GetComponent<IReceiveSpawnChoice>();
            receiver?.OnSpawnWithChoice(default);
        }

        private (Vector3 position, Quaternion rotation, Transform spawnPoint) GetSpawnTR()
        {
            if (available.Count == 0)
            {
                Debug.LogWarning("[WaitingRoomSpawner] No available spawn points! Using random position.");
                var pos = new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
                return (pos, Quaternion.identity, null);
            }

            var idx = Random.Range(0, available.Count);
            var sp = available[idx];
            available.RemoveAt(idx);
            return (sp.position, sp.rotation, sp);
        }

        // Trả đúng slot theo map; fallback “gần nhất” nếu mất map
        private void RestoreSpawnPoint(ulong clientId, Vector3 playerPosition)
        {
            // Trả về đúng slot nếu có
            if (slotByClient.TryGetValue(clientId, out var exact))
            {
                if (exact != null && !available.Contains(exact))
                {
                    usedSpawnPoints.Remove(exact);
                    available.Add(exact);
                    Debug.Log($"[WaitingRoomSpawner] Restored slot for {clientId}: {exact.name}");
                }
                return;
            }

            // Fallback: tìm gần nhất
            Transform closest = null;
            float closestDist = float.MaxValue;
            foreach (var sp in usedSpawnPoints)
            {
                if (sp == null) continue;
                var d = Vector3.Distance(playerPosition, sp.position);
                if (d < closestDist) { closestDist = d; closest = sp; }
            }
            if (closest != null && closestDist < 1f)
            {
                usedSpawnPoints.Remove(closest);
                available.Add(closest);
                Debug.Log($"[WaitingRoomSpawner] Restored nearest slot: {closest.name}");
            }
        }

        private void RefreshAvailableSpawnPoints()
        {
            available.Clear();
            usedSpawnPoints.Clear();
            slotByClient.Clear();

            if (spawnPoints != null)
                available.AddRange(spawnPoints);
        }

        [ContextMenu("Debug Spawn Info")]
        private void DebugSpawnInfo()
        {
            Debug.Log($"Available spawn points: {available.Count}");
            Debug.Log($"Used spawn points: {usedSpawnPoints.Count}");
            Debug.Log($"Spawned players: {spawned.Count}");
        }
    }

    public interface IReceiveSpawnChoice
    {
        void OnSpawnWithChoice(object choice);
    }
}

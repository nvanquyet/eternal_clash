using System;
using System.Collections;
using System.Collections.Generic;
using _GAME.Scripts.Core;
using _GAME.Scripts.Networking;
using _GAME.Scripts.Networking.Lobbies;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.UI.WaitingRoom
{
    /// <summary>
    /// WaitingSpawnController - Handles client connections/disconnections in waiting room
    /// Spawns players as they connect and manages lobby events
    /// </summary>
    public class WaitingSpawnController : SpawnerController
    {
        // Waiting room specific tracking
        private readonly Dictionary<ulong, float> clientConnectTimes = new();

        #region Unity Lifecycle Override

        protected override void Awake()
        {
            base.Awake();
            if (debugMode) Debug.Log("[WaitingSpawnController] Waiting room initialized");
        }

        #endregion

        #region Network Lifecycle Override

        /// <summary>
        /// Override to use connection-based spawning instead of scene-based
        /// </summary>
        protected override IEnumerator InitializeSceneSpawning()
        {
            // Wait for network to be fully ready
            yield return new WaitForSeconds(0.1f);
            
            // Mark scene as loaded
            _sceneFullyLoaded = true;
            
            if (debugMode) Debug.Log("[WaitingSpawnController] Waiting room spawning system initialized");
            
            // Process existing clients (host should spawn immediately)
            ProcessExistingClients();
            
            // Start monitoring for waiting room specific logic
            StartCoroutine(MonitorWaitingRoom());
        }

        #endregion

        #region Callback Registration Override

        /// <summary>
        /// Register waiting room specific callbacks
        /// </summary>
        protected override void RegisterEarlyCallbacks()
        {
            base.RegisterEarlyCallbacks();
            
            // Lobby events for waiting room
            LobbyEvents.OnPlayerKicked += OnPlayerKicked;
            LobbyEvents.OnLobbyRemoved += OnLobbyRemoved;
            
            if (debugMode) Debug.Log("[WaitingSpawnController] Waiting room callbacks registered");
        }

        /// <summary>
        /// Register connection callbacks for waiting room
        /// </summary>
        protected override void RegisterServerCallbacks()
        {
            base.RegisterServerCallbacks(); // Scene callbacks
            
            if (!IsServer || _eventsRegistered) return;

            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                // ✅ CONNECTION CALLBACKS - chỉ có ở waiting room
                nm.OnClientConnectedCallback += OnClientConnected;
                nm.OnClientDisconnectCallback += OnClientDisconnected;
            }

            if (debugMode) Debug.Log("[WaitingSpawnController] Connection callbacks registered");
        }

        protected override void UnregisterAllCallbacks()
        {
            base.UnregisterAllCallbacks();

            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientConnectedCallback -= OnClientConnected;
                nm.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            LobbyEvents.OnPlayerKicked -= OnPlayerKicked;
            LobbyEvents.OnLobbyRemoved -= OnLobbyRemoved;

            if (debugMode) Debug.Log("[WaitingSpawnController] Waiting room callbacks unregistered");
        }

        #endregion

        #region Connection Event Handlers

        /// <summary>
        /// Handle new client connections in waiting room
        /// </summary>
        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer) return;

            if (debugMode) Debug.Log($"[WaitingSpawnController] Client {clientId} connected to waiting room");
            
            // Track connection time
            clientConnectTimes[clientId] = Time.time;

            // Set initial state and spawn immediately
            SetClientState(clientId, ClientSpawnState.Connected);
            
            if (_sceneFullyLoaded)
            {
                StartCoroutine(SpawnNewClient(clientId));
            }
        }

        /// <summary>
        /// Handle client disconnections in waiting room
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            if (debugMode) Debug.Log($"[WaitingSpawnController] Client {clientId} disconnected from waiting room");
            
            SetClientState(clientId, ClientSpawnState.Disconnected);
            CleanupClient(clientId);
            clientConnectTimes.Remove(clientId);
        }

        #endregion

        #region Waiting Room Spawning

        /// <summary>
        /// Spawn a newly connected client
        /// </summary>
        private IEnumerator SpawnNewClient(ulong clientId)
        {
            yield return new WaitForSeconds(spawnDelay);
            
            if (GetClientState(clientId) == ClientSpawnState.Disconnected)
            {
                if (debugMode) Debug.Log($"[WaitingSpawnController] Client {clientId} disconnected before spawn");
                yield break;
            }

            TrySpawnPlayer(clientId);
        }

        /// <summary>
        /// Override to add identity sync for waiting room players
        /// </summary>
        protected override bool ShouldAddIdentitySync()
        {
            return true; // Waiting room players need identity sync
        }

        #endregion

        #region Game Start Logic

        /// <summary>
        /// Monitor waiting room conditions
        /// </summary>
        private IEnumerator MonitorWaitingRoom()
        {
            var wait = new WaitForSeconds(1f);
            
            while (IsServer && NetworkManager.Singleton != null)
            {
                // Check for timeout clients in waiting room
                CheckClientTimeouts();
                yield return wait;
            }
        }
        

        /// <summary>
        /// Check for clients that have been waiting too long
        /// </summary>
        private void CheckClientTimeouts()
        {
            var now = Time.time;
            var clientsToTimeout = new List<ulong>();
            
            foreach (var kvp in clientConnectTimes)
            {
                var clientId = kvp.Key;
                var connectTime = kvp.Value;
                var state = GetClientState(clientId);
                
                // Check for clients stuck in connecting states
                if ((state == ClientSpawnState.Connected || state == ClientSpawnState.Spawning) &&
                    now - connectTime > clientReadyTimeout)
                {
                    Debug.LogWarning($"[WaitingSpawnController] Client {clientId} timeout in waiting room");
                    clientsToTimeout.Add(clientId);
                }
            }

            // Force spawn timeout clients
            foreach (var clientId in clientsToTimeout)
            {
                TrySpawnPlayer(clientId);
            }
        }

        #endregion

        #region Lobby Event Handlers

        /// <summary>
        /// Handle player being kicked from lobby
        /// </summary>
        private void OnPlayerKicked(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message)
        {
            if (!IsServer || player == null) return;

            if (debugMode) Debug.Log($"[WaitingSpawnController] Player kicked from lobby: {player.Id}");

            var registry = ClientIdentityRegistry.Instance;
            if (registry != null && registry.TryGetClientId(player.Id, out var clientId))
            {
                if (debugMode) Debug.Log($"[WaitingSpawnController] Disconnecting kicked player: {clientId}");
                NetworkManager.Singleton.DisconnectClient(clientId, "Kicked from lobby");
            }
        }

        /// <summary>
        /// Handle lobby being removed
        /// </summary>
        private void OnLobbyRemoved(Lobby lobby, bool success, string message)
        {
            if (!IsServer || !success) return;

            Debug.Log("[WaitingSpawnController] Lobby removed, cleaning up waiting room");
            CleanupAllPlayers();
        }

        #endregion

        #region Override Cleanup Methods

        /// <summary>
        /// Override cleanup to handle waiting room specific cleanup
        /// </summary>
        protected override void CleanupClient(ulong clientId)
        {
            base.CleanupClient(clientId);
            
            clientConnectTimes.Remove(clientId);
            
            // Clean up client identity registry
            var registry = ClientIdentityRegistry.Instance;
            registry?.UnregisterByClient(clientId);

            if (debugMode) Debug.Log($"[WaitingSpawnController] Cleaned up waiting room client {clientId}");
        }

        /// <summary>
        /// Override to handle waiting room cleanup
        /// </summary>
        protected override void CleanupAllPlayers()
        {
            if (debugMode) Debug.Log($"[WaitingSpawnController] Cleaning up all waiting room players");
            base.CleanupAllPlayers();
            
            clientConnectTimes.Clear();
        }

        #endregion
    }
}
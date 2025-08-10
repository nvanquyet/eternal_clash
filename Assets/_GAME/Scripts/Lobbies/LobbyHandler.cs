using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GAME.Scripts.DesignPattern;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.Lobbies
{
    // Event args cho các sự kiện lobby
    public class LobbyEventArgs : EventArgs
    {
        public Lobby Lobby { get; set; }
        public string Message { get; set; }
        public bool Success { get; set; }
    }

    public class PlayerEventArgs : EventArgs
    {
        public Unity.Services.Lobbies.Models.Player Player { get; set; }
        public string Message { get; set; }
    }

    // Interface cho lobby operations
    public interface ILobbyOperations
    {
        Task<List<Lobby>> GetLobbyListAsync();
        Task<Lobby> GetLobbyInfoAsync(string lobbyId);
        Task<Lobby> CreateLobbyAsync(string lobbyName, int maxPlayers, CreateLobbyOptions options);
        Task<bool> JoinLobbyAsync(string lobbyCode, string password);
        Task<bool> LeaveLobbyAsync(string lobbyId, string playerId, bool isHost = false); 
        Task<bool> RemoveLobbyAsync(string lobbyId);
        Task<bool> KickPlayerAsync(string lobbyId, string playerId);
        Task<bool> UpdateLobbyAsync(string lobbyId, UpdateLobbyOptions updateOptions = null);
    }

    // Main LobbyManager class với Singleton pattern
    public class LobbyHandler : Singleton<LobbyHandler>, ILobbyOperations
    {
        [Header("Lobby Settings")]
        [SerializeField] private float heartbeatInterval = 15f;
        [SerializeField] private float lobbyRefreshInterval = 2f;
        [SerializeField] private int maxLobbiesQuery = 25;

        // Events
        public event EventHandler<LobbyEventArgs> OnLobbyCreated;
        public event EventHandler<LobbyEventArgs> OnLobbyJoined;
        public event EventHandler<LobbyEventArgs> OnLobbyLeft;
        public event EventHandler<LobbyEventArgs> OnLobbyUpdated;
        public event EventHandler<LobbyEventArgs> OnLobbyRemoved;
        public event EventHandler<PlayerEventArgs> OnPlayerJoined;
        public event EventHandler<PlayerEventArgs> OnPlayerLeft;
        public event EventHandler<PlayerEventArgs> OnPlayerKicked;
        public event EventHandler OnGameStarted;

        private LobbyHeartbeat _heartbeat;
        private LobbyUpdater _updater;

        protected override void OnAwake()
        {
            base.OnAwake();
            InitializeComponents();
        }

        private async void Start()
        {
            try
            {
                await InitializeUnityServices();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        private void InitializeComponents()
        {
            _heartbeat = gameObject.AddComponent<LobbyHeartbeat>();
            _updater = gameObject.AddComponent<LobbyUpdater>();
            
            _heartbeat.Initialize(this, heartbeatInterval);
            _updater.Initialize(this, lobbyRefreshInterval);
        }

        private async Task InitializeUnityServices()
        {
            try
            {
                await UnityServices.InitializeAsync();
                Debug.Log("Unity Services initialized successfully.");

                AuthenticationService.Instance.SignedIn += OnSignedIn;
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize Unity Services: {e}");
            }
        }

        private void OnSignedIn()
        {
            Debug.Log($"Signed in successfully with player ID: {AuthenticationService.Instance.PlayerId}");
        }

        public async Task<List<Lobby>> GetLobbyListAsync()
        {
            try
            {
                var queryOptions = new QueryLobbiesOptions
                {
                    Count = maxLobbiesQuery,
                    Filters = new List<QueryFilter>
                    {
                        new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                    },
                    Order = new List<QueryOrder>
                    {
                        new QueryOrder(false, QueryOrder.FieldOptions.Created)
                    }
                };

                var queryResponse = await LobbyService.Instance.QueryLobbiesAsync(queryOptions);
                var lobbies = queryResponse.Results;

                if (lobbies == null || lobbies.Count == 0)
                {
                    Debug.Log("No lobbies found.");
                    return new List<Lobby>();
                }

                Debug.Log($"Found {lobbies.Count} lobbies");
                return lobbies;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to get lobby list: {e}");
                return new List<Lobby>();
            }
        }

        public async Task<Lobby> GetLobbyInfoAsync(string lobbyId)
        {
            try
            {
                var lobby = await LobbyService.Instance.GetLobbyAsync(lobbyId);
                Debug.Log($"Got lobby info: {lobby.Name} ({lobby.Id})");
                return lobby;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to get lobby info for ID {lobbyId}: {e}");
                return null;
            }
        }

        public async Task<Lobby> CreateLobbyAsync(string lobbyName, int maxPlayers, CreateLobbyOptions options)
        {
            try
            {
                var lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
                
                _heartbeat.StartHeartbeat(lobby.Id);
                _updater.StartUpdating(lobby.Id);

                OnLobbyCreated?.Invoke(this, new LobbyEventArgs 
                { 
                    Lobby = lobby, 
                    Success = true, 
                    Message = $"Lobby '{lobby.Name}' created successfully" 
                });

                Debug.Log($"Lobby created: {lobby.Name} ({lobby.Id})");
                return lobby;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to create lobby: {e}");
                OnLobbyCreated?.Invoke(this, new LobbyEventArgs 
                { 
                    Success = false, 
                    Message = e.Message 
                });
                return null;
            }
        }
        
        
        public async Task<bool> JoinLobbyAsync(string lobbyCode, string password)
        {
            try
            {
                var joinOptions = new JoinLobbyByCodeOptions
                {
                    Player = GetPlayerData(),
                    Password = password
                };

                var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, joinOptions);
                if (lobby != null)
                {
                    _updater.StartUpdating(lobby.Id);
                    
                    Debug.Log($"Joined lobby: {lobby.Name} ({lobby.Id})");
                    return true;
                }
                Debug.Log($"Failed to join lobby with code {lobbyCode}: Lobby not found, password incorrect or full.");
                return false;

            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to join lobby with code {lobbyCode}: {e}");
                OnLobbyJoined?.Invoke(this, new LobbyEventArgs 
                { 
                    Success = false, 
                    Message = e.Message 
                });
                return false;
            }
        }

        public async Task<bool> LeaveLobbyAsync(string lobbyId, string playerId, bool isHost = false)
        {
            try
            {
                if (isHost)
                {
                    // Host leaving = delete lobby
                    await RemoveLobbyAsync(lobbyId);
                    return true;
                }
                else
                {
                    // Regular player leaving
                    await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
                    _updater.StopUpdating();
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to leave lobby: {e}");
                return false;
            }
        }
        
        public async Task<bool> RemoveLobbyAsync(string lobbyId)
        {
            try
            {
                await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
                
                _heartbeat.StopHeartbeat();
                _updater.StopUpdating();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to remove lobby: {e}");
                return false;
            }
        }
        
        public async Task<bool> KickPlayerAsync(string lobbyId, string playerId)
        {
            try
            {
                await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
                Debug.Log($"Player kicked: {playerId}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to kick player {playerId}: {e}");
                return false;
            }
        }

        public async Task<bool> UpdateLobbyAsync(string lobbyId, UpdateLobbyOptions updateOptions = null)
        {
            try
            {
                var updatedLobby = await LobbyService.Instance.UpdateLobbyAsync(lobbyId, updateOptions);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to update lobby: {e}");
                return false;
            }
        }

        private Unity.Services.Lobbies.Models.Player GetPlayerData()
        {
            return new Unity.Services.Lobbies.Models.Player
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { "playerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, $"Player_{AuthenticationService.Instance.PlayerId}") },
                    { "isReady", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "false") }
                }
            };
        }
        
        protected override void OnDestroy()
        {
            if (Instance == this)
            {
                _heartbeat?.StopHeartbeat();
                _updater?.StopUpdating();
                base.OnDestroy();
            }
        }
        
    }
}
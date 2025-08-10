using System;
using System.Collections.Generic;
using QFSW.QC;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

public class TestLobby : MonoBehaviour
{
    private Lobby hostLobby;

    [Header("Configuration")] [SerializeField]
    private string lobbyName = "My Test Lobby";

    [SerializeField] private int maxPlayers = 4;

    private readonly float heartbeatInterval = 15f; // Lobby heartbeat interval in seconds
    private float heartbeatTimer; // Lobby heartbeat interval in seconds


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private async void Start()
    {
        try
        {
            await UnityServices.InitializeAsync();
            Debug.Log("Unity Services initialized successfully.");

            // Setup callback for sign in action
            AuthenticationService.Instance.SignedIn += () =>
            {
                Debug.Log($"Signed in successfully with player ID: {AuthenticationService.Instance.PlayerId}");
            };

            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        catch (Exception e)
        {
            Debug.LogError(e); // TODO handle exception
        }
    }

    [Command]
    private async void CreateLobby()
    {
        try
        {
            // Create a new lobby with the specified name and maximum players
            var lobbyOptions = new CreateLobbyOptions
            {
                IsPrivate = false, // Set to true if you want a private lobby
                Data = new System.Collections.Generic.Dictionary<string, DataObject>
                {
                    { "lobbyName", new DataObject(DataObject.VisibilityOptions.Public, lobbyName) },
                    { "maxPlayers", new DataObject(DataObject.VisibilityOptions.Public, maxPlayers.ToString()) }
                }
            };
            try
            {
                var lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, lobbyOptions);
                if (lobby == null)
                {
                    Debug.LogError("Lobby creation failed, received null lobby.");
                    return;
                }

                hostLobby = lobby;
                Debug.Log($"Lobby created successfully: {hostLobby.Name} with ID: {hostLobby.Id}");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"Failed to create lobby: {e}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to create lobby: {e}");
        }
    }

    [Command]
    private async void GetListLobbies()
    {
        try
        {
            //Query the list of lobbies
            var queryOptions = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.Created, "0", QueryFilter.OpOptions.EQ)
                },
                Order = new List<QueryOrder>
                {
                    new QueryOrder(false, QueryOrder.FieldOptions.Created)
                }
            };
            // Fetch the list of lobbies
            var lobbies = await LobbyService.Instance.QueryLobbiesAsync();
            Debug.Log($"Found {lobbies.Results.Count} lobbies:");
            foreach (var lobby in lobbies.Results)
            {
                Debug.Log($"Lobby: {lobby.Name}, ID: {lobby.Id}, Players: {lobby.Players.Count}/{lobby.MaxPlayers}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to get list of lobbies: {e}");
        }
    }

    // Update is called once per frame
    void Update()
    {
        HandleHeartbeat();
    }

    [Command]
    private async void HandleHeartbeat()
    {
        try
        {
            if (hostLobby == null) return;
            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer < 0)
            {
                heartbeatTimer = heartbeatInterval; // Reset the timer
                // Send a heartbeat to keep the lobby alive
                try
                {
                    await LobbyService.Instance.SendHeartbeatPingAsync(hostLobby.Id);
                    Debug.Log($"Heartbeat sent for lobby: {hostLobby.Name}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to send heartbeat: {e}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in HandleHeartbeat: {e}");
        }
    }
    
    
    [Command]
    private async void JoinLobbyById(string lobbyId)
    {
        try
        {
            // Join the specified lobby by ID
            var lobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);
            if (lobby == null)
            {
                Debug.LogError("Failed to join lobby, received null lobby.");
                return;
            }

            Debug.Log($"Joined lobby successfully: {lobby.Name} with ID: {lobby.Id}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to join lobby: {e}");
        }
    }
    
    [Command]
    private async void JoinLobbyByCode(string lobbyCode)
    {
        try
        {
            // Join the specified lobby by ID
            var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);
            if (lobby == null)
            {
                Debug.LogError("Failed to join lobby, received null lobby.");
                return;
            }

            Debug.Log($"Joined lobby successfully: {lobby.Name} with Code: {lobby.LobbyCode}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to join lobby: {e}");
        }
    }
    
    [Command]
    private async void QuickJoinLobby()
    {
        
    }
}
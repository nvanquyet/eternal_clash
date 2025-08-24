using System;
using System.Collections.Generic;
using _GAME.Scripts.Authenticator;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Data;
using _GAME.Scripts.Lobbies;
using _GAME.Scripts.Lobbies.UI;
using _GAME.Scripts.Networking.Lobbies;
using _GAME.Scripts.Networking.Relay;
using _GAME.Scripts.UI.Base;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace _GAME.Scripts.UI.Home
{
    public class HomeUI : BaseUI
    {
        [SerializeField] private Button btnBtnHost, btnJoin;
        [SerializeField] private TMP_InputField lobbyCodeInputField;
        [SerializeField] private LobbyPasswordConfirmation lobbyPasswordConfirmation;
        
        [SerializeField] private Button btnLogout;
        private bool isProcessing = false;
        
        protected void Awake()
        {
            btnBtnHost.onClick.AddListener(OnHostButtonClicked);
            btnLogout.onClick.AddListener(OnClickLogout);
            btnJoin.onClick.AddListener(OnJoinButtonClicked);
        }
        
        
        //Sign in anonymously with UnityAuthenticator
        private async void Start()
        {
            try
            {
                await UnityServices.InitializeAsync();
                AuthenticationService.Instance.SignedIn += () =>
                {
                    Debug.Log($"Signed in as: {AuthenticationService.Instance.PlayerId}");
                };
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to init Unity Services: {e}");
            }
        }

        private void OnClickLogout()
        {
            // Log out from Unity PlayFab
            Debug.Log("Logging out from PlayFab...");
            PlayFabAuthManager.Instance.Logout(null);
            LocalData.UserPassword = string.Empty; // Clear stored password
            LocalData.UserId = string.Empty; // Clear stored user ID
            LocalData.UserName = string.Empty; // Clear stored user name
            SceneController.Instance.LoadSceneAsync((int) SceneDefinitions.Login);
        }

        private void OnEnable()
        {
            // Subscribe to network events for feedback
            LobbyEvents.OnRelayHostReady += OnRelayHostReady;
            LobbyEvents.OnRelayClientReady += OnRelayClientReady;
            LobbyEvents.OnRelayError += OnRelayError;
        }

        private void OnDisable()
        {
            // Always unsubscribe to prevent memory leaks
            LobbyEvents.OnRelayHostReady -= OnRelayHostReady;
            LobbyEvents.OnRelayClientReady -= OnRelayClientReady;
            LobbyEvents.OnRelayError -= OnRelayError;
        }

        private async void OnHostButtonClicked()
        {
            if (isProcessing) return;
            
            try
            {
                isProcessing = true;
                
                // Show loading with initial progress
                LoadingUI.Instance.SetProgress(0.1f, 1f, "Creating lobby..");
                
                // Disable buttons during processing
                SetButtonsInteractable(false);
                
                string defaultPassword = "12345678";
                var createLobbyOption = new CreateLobbyOptions
                {
                    Password = defaultPassword, // Built-in password protection
                    Data = new Dictionary<string, DataObject>
                    {
                        // Store password in lobby data for UI display
                        { LobbyConstants.LobbyData.PASSWORD, new DataObject(DataObject.VisibilityOptions.Member, defaultPassword) },
                        // Set initial phase
                        { LobbyConstants.LobbyData.PHASE, new DataObject(DataObject.VisibilityOptions.Member, LobbyConstants.Phases.WAITING) }
                    },
                };

                // Update progress
                LoadingUI.Instance.SetProgress(0.3f, 1f, "Setting up Lobby...");
                var lobby = await LobbyHandler.Instance.CreateLobbyAsync("MyLobby", 4, createLobbyOption);
                
                if (lobby != null)
                {
                    Debug.Log($"[Home UI] Lobby created successfully: {lobby.Name} with code {lobby.LobbyCode}");
                    //Trigger lobby update
                    LobbyEvents.TriggerLobbyCreated(lobby, true, "Lobby created successfully");
                    // Update progress - lobby created, now setting up network
                    LoadingUI.Instance.SetProgress(0.5f, 1f, "Setting up game server...");
                    
                    // NetSessionManager will handle the Relay creation and scene transition
                    // through the LobbyEvents.OnLobbyCreated event
                }
                else
                {
                    throw new Exception("Failed to create lobby");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error while creating lobby: {e.Message}");
                HandleError($"Failed to create lobby: {e.Message}");
            }
        }

        private void OnJoinButtonClicked()
        {
            if (isProcessing) return;
            
            string lobbyCode = lobbyCodeInputField.text.Trim();
            if (string.IsNullOrEmpty(lobbyCode))
            {
                Debug.LogWarning("Lobby code is empty. Please enter a valid code.");
                PopupNotification.Instance?.ShowPopup(false, "Please enter a lobby code");
                return;
            }
            
            // Subscribe to join result
            LobbyEvents.OnLobbyJoined += OnLobbyJoined;
            
            // Show password confirmation dialog
            lobbyPasswordConfirmation.Initialized(async (password) =>
            {
                if (isProcessing) return;
                
                try
                {
                    isProcessing = true;
                    
                    // Show loading
                    LoadingUI.Instance.SetProgress(0.2f, 1f, "Joining lobby...");
                    
                    // Disable buttons
                    SetButtonsInteractable(false);
                    
                    Debug.Log($"Attempting to join lobby with code: {lobbyCode}");
                    var joinResult = await LobbyHandler.Instance.JoinLobbyAsync(lobbyCode, password);
                    
                    if (!joinResult)
                    {
                        throw new Exception("Failed to join lobby - check code and password");
                    }
                    
                    // Update progress - joined lobby, now connecting to network
                    LoadingUI.Instance.SetProgress(0.6f, 1f, "Connecting to game server...");
                    
                    // NetSessionManager will handle the Relay connection and scene transition
                    // through the LobbyEvents.OnLobbyJoined event
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error while joining lobby: {e.Message}");
                    HandleError($"Failed to join lobby: {e.Message}");
                    
                    // Unsubscribe on error
                    LobbyEvents.OnLobbyJoined -= OnLobbyJoined;
                }
            });
        }
        
        private void OnLobbyJoined(Lobby lobby, bool isSuccess, string message)
        {
            if (isSuccess)
            {
                Debug.Log($"Successfully joined lobby: {lobby.Name}");
                
                // Update progress
                LoadingUI.Instance.SetProgress(0.7f, 1f, "Waiting for network setup...");
                
                // Check if relay code is available immediately
                if (lobby.HasValidRelayCode())
                {
                    LoadingUI.Instance.SetProgress(0.8f, 1f, "Connecting to relay server...");
                }
                else
                {
                    LoadingUI.Instance.SetProgress(0.6f, 1f, "Waiting for host to setup game server...");
                }
            }
            else
            {
                Debug.LogError($"Failed to join lobby: {message}");
                HandleError(message);
            }
            
            // Unsubscribe from the event
            LobbyEvents.OnLobbyJoined -= OnLobbyJoined;
        }

        // Relay event handlers
        private void OnRelayHostReady(string joinCode)
        {
            Debug.Log($"[HomeUI] Relay host ready with join code: {joinCode}");
            ResetProcessingState();
        }

        private void OnRelayClientReady()
        {
            Debug.Log("[HomeUI] Relay client connected successfully");
            ResetProcessingState();
        }

        private void OnRelayError(string errorMessage)
        {
            Debug.LogError($"[HomeUI] Relay error: {errorMessage}");
            HandleError($"Network error: {errorMessage}");
            
            // Cleanup network state if needed
            if (NetworkManager.Singleton != null && 
                (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        // Helper methods
        private void HandleError(string errorMessage)
        {
            LoadingUI.Instance.Hide();
            PopupNotification.Instance?.ShowPopup(false, errorMessage);
            ResetProcessingState();
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if(btnBtnHost) btnBtnHost.interactable = interactable;
            if(btnJoin) btnJoin.interactable = interactable;
        }

        private void ResetProcessingState()
        {
            isProcessing = false;
            SetButtonsInteractable(true);
        }

        public void ReInit()
        {
            // Hide loading if showing
            LoadingUI.Instance.Hide();
            
            // Reset processing state
            ResetProcessingState();
            
            // Deactivate confirm pass and set empty input 
            if (lobbyPasswordConfirmation != null)
            {
                lobbyPasswordConfirmation.gameObject.SetActive(false);
            }
            if (lobbyCodeInputField != null)
            {
                lobbyCodeInputField.text = string.Empty;
            }

            // Cleanup any pending network state
            if (NetworkManager.Singleton != null && 
                (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        // Cleanup on destroy
        private void OnDestroy()
        {
            // Ensure we don't have hanging subscriptions
            LobbyEvents.OnLobbyJoined -= OnLobbyJoined;
            
            btnBtnHost.onClick.RemoveListener(OnHostButtonClicked);
            btnLogout.onClick.RemoveListener(OnClickLogout);
            btnJoin.onClick.RemoveListener(OnJoinButtonClicked);
        }
    }
}
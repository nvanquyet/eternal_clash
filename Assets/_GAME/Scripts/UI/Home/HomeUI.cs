using System;
using _GAME.Scripts.Authenticator;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Data;
using _GAME.Scripts.Lobbies.UI;
using _GAME.Scripts.Networking;
using _GAME.Scripts.UI.Base;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.UI.Home
{
    public class HomeUI : BaseUI
    {
        [SerializeField] private Button btnBtnHost, btnJoin;
        [SerializeField] private TMP_InputField lobbyCodeInputField;
        [SerializeField] private LobbyPasswordConfirmation lobbyPasswordConfirmation;
        
        [SerializeField] private Button btnLogout;
        
        private bool _isProcessing = false;
        
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
                
                //Hide Loading
                LoadingUI.Instance.Complete();
                
                AudioManager.Instance.PlayMenuMusic();
            }
            catch (Exception e)
            {
                //Force quit
                //Application.Quit();
                Debug.LogError($"Failed to init Unity Services: {e}");
            }    
        }

        private void OnClickLogout()
        {
            // Log out from Unity PlayFab
            Debug.Log("Logging out from PlayFab...");
            GameNet.Instance.Auth.Logout();
            LocalData.UserPassword = string.Empty; // Clear stored password
            LocalData.UserId = string.Empty; // Clear stored user ID
            LocalData.UserName = string.Empty; // Clear stored user name
            SceneController.Instance.LoadSceneAsync((int) SceneDefinitions.Login);
        }

        private async void OnHostButtonClicked()
        {
            try
            {
                if (_isProcessing) return;
            
                try
                {
                    _isProcessing = true;
                    // Disable buttons during processing
                    SetButtonsInteractable(false);
                    // Update progress
                    await GameNet.Instance.HostLobby();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error while creating lobby: {e.Message}");
                    HandleError($"Failed to create lobby: {e.Message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[HomeUI] Unexpected error: {e.Message}");
            }
        }
        
        
        private void OnJoinButtonClicked()
        {
            if (_isProcessing) return;
            
            string lobbyCode = lobbyCodeInputField.text.Trim();
            if (string.IsNullOrEmpty(lobbyCode))
            {
                Debug.LogWarning("Lobby code is empty. Please enter a valid code.");
                PopupNotification.Instance?.ShowPopup(false, "Please enter a lobby code");
                return;
            }
            
            // Show password confirmation dialog
            lobbyPasswordConfirmation.Initialized(async (password) =>
            {
                if (_isProcessing) return;
                
                try
                {
                    _isProcessing = true;
                    
                    // Disable buttons
                    SetButtonsInteractable(false);
                    
                    Debug.Log($"Attempting to join lobby with code: {lobbyCode}");
                    await GameNet.Instance.JoinLobby(lobbyCode, password);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error while joining lobby: {e.Message}");
                    HandleError($"Failed to join lobby: {e.Message}");
                }
            });
        }
  
        // Helper methods
        private void HandleError(string errorMessage)
        {
            LoadingUI.Instance.Complete(() =>
            {
                PopupNotification.Instance?.ShowPopup(false, errorMessage);
                ResetProcessingState();
            });
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if(btnBtnHost) btnBtnHost.interactable = interactable;
            if(btnJoin) btnJoin.interactable = interactable;
        }

        private void ResetProcessingState()
        {
            _isProcessing = false;
            SetButtonsInteractable(true);
        }

        // Cleanup on destroy
        private void OnDestroy()
        {
            btnBtnHost.onClick.RemoveListener(OnHostButtonClicked);
            btnLogout.onClick.RemoveListener(OnClickLogout);
            btnJoin.onClick.RemoveListener(OnJoinButtonClicked);
        }
    }
}
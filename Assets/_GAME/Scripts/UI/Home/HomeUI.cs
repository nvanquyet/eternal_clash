using System;
using System.Collections.Generic;
using _GAME.Scripts.Lobbies;
using _GAME.Scripts.Lobbies.UI;
using _GAME.Scripts.UI.Base;
using TMPro;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.UI.Home
{
    public class HomeUI : BaseUI
    {
        [SerializeField] private Button btnBtnHost, btnJoin;
        [SerializeField] private TMP_InputField lobbyCodeInputField;
        [SerializeField] private LobbyPasswordConfirmation lobbyPasswordConfirmation;
        
        protected void Awake()
        {
            btnBtnHost.onClick.AddListener(OnHostButtonClicked);
            btnJoin.onClick.AddListener(OnJoinButtonClicked);
        }
        
        private async void OnHostButtonClicked()
        {
            try
            {
                // Todo: Create a new lobby and show LobbyUI
                Debug.Log("Host button clicked. Implement hosting logic here.");
                string defaultPassword = "12345678"; // ✅ Mật khẩu mặc định
                var createLobbyOption = new CreateLobbyOptions
                {
                    Password = defaultPassword, // Built-in password (Unity Lobby quản lý)
                    Data = new Dictionary<string, DataObject>
                    {
                        // Mirror password ra Data để member nào cũng thấy được (nếu bạn cho phép)
                        { "Password", new DataObject(DataObject.VisibilityOptions.Member, defaultPassword) }
                    },
                };

                var lobby = await LobbyHandler.Instance.CreateLobbyAsync("MyLobby", 4, createLobbyOption);
                if (lobby != null)
                {
                    Debug.Log($"Lobby created successfully: {lobby.Name} with code {lobby.LobbyCode}");
                    // Show LobbyUI or perform any other action after successfully creating the lobby
                    
                    var lobbyUI = HUD.Instance.GetUI<LobbyUI>(UIType.Lobby);
                    if (lobbyUI != null)
                    {
                        lobbyUI.SetLobby(lobby);
                        HUD.Instance.Show(UIType.Lobby);
                    }
                    else
                    {
                        Debug.LogError("LobbyUI not found in HUD.");
                    }
                }
                else
                {
                    Debug.LogError("Failed to create lobby.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error while creating lobby: {e.Message}");
            }
        }

        private void OnJoinButtonClicked()
        {
            string lobbyCode = lobbyCodeInputField.text.Trim();
            if (string.IsNullOrEmpty(lobbyCode))
            {
                Debug.LogWarning("Lobby code is empty. Please enter a valid code.");
                return;
            }
            
            //Add Action CallBack
            LobbyEvents.OnLobbyJoined += OnLobbyJoined;
            lobbyPasswordConfirmation.Initialized(async (password) =>
            {
                //Todo: Call a method to join the lobby with code and show Lobby if successful
                // Logic to join an existing lobby using the provided code
                Debug.Log($"Join button clicked with lobby code: {lobbyCode}. Implement joining logic here.");
                var joinLobbyResult = await LobbyHandler.Instance.JoinLobbyAsync(lobbyCode, password);
            });
        }
        
        private void OnLobbyJoined(Lobby lobby, bool isSuccess, string message)
        {
            //sender is successful or not
            if (isSuccess)
            {
                Debug.Log($"Successfully joined lobby: {lobby.Name}");
                // Show LobbyUI or perform any other action after successfully joining the lobby
                
                var lobbyUI = HUD.Instance.GetUI<LobbyUI>(UIType.Lobby);
                if (lobbyUI != null)
                {
                    lobbyUI.SetLobby(lobby);
                    HUD.Instance.Show(UIType.Lobby);
                }
                else
                {
                    Debug.LogError("LobbyUI not found in HUD.");
                }
            }
            else
            {
                Debug.LogError($"Failed to join lobby: {message}");
                PopupNotification.Instance.ShowPopup(false, message);
                // Show error message or handle failure
            }
            
            // Unsubscribe from the event to avoid memory leaks
            LobbyEvents.OnLobbyJoined -= OnLobbyJoined;
        }

        public void ReInit()
        {
            //Deactivate confirm pass and set empty input
            if (lobbyPasswordConfirmation != null)
            {
                lobbyPasswordConfirmation.gameObject.SetActive(false);
            }
            if (lobbyCodeInputField != null)
            {
                lobbyCodeInputField.text = string.Empty;
            }
        }
    }
}

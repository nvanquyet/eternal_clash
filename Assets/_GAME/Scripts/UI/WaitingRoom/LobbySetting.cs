using System;
using _GAME.Scripts.Config;
using _GAME.Scripts.HideAndSeek;
using _GAME.Scripts.Networking;
using _GAME.Scripts.Networking.Lobbies;
using Michsky.MUIP;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace _GAME.Scripts.UI.WaitingRoom
{
    public class LobbySetting : MonoBehaviour
    {
        [SerializeField] private TMP_InputField lobbyNameInputField;
        [SerializeField] private TMP_InputField passwordInputField;
        [SerializeField] private CustomDropdown dropdownMaxPlayers;

        private void Start()
        {
            // Initialize the dropdown with max players options
            InitDropDown();
            
            if (GameNet.Instance.Network.IsHost)
            {
                lobbyNameInputField.interactable = true;
                passwordInputField.interactable = true;
                dropdownMaxPlayers.Interactable(true);
                
                lobbyNameInputField.onEndEdit.AddListener(OnLobbyNameChanged);
                passwordInputField.onEndEdit.AddListener(OnLobbyPasswordChanged);
                dropdownMaxPlayers.onValueChanged.AddListener(OnMaxPlayersChanged);
            }
            else
            {
                // Disable the input fields and dropdown for non-host players
                lobbyNameInputField.interactable = false;
                passwordInputField.interactable = false;
                dropdownMaxPlayers.Interactable(false);
            }
        }

        private void OnDestroy()
        {
            if (GameNet.Instance.Network.IsHost)
            {
                lobbyNameInputField.onEndEdit.RemoveListener(OnLobbyNameChanged);
                passwordInputField.onEndEdit.RemoveListener(OnLobbyPasswordChanged);
                dropdownMaxPlayers.onValueChanged.RemoveListener(OnMaxPlayersChanged);
            }
        }


        public void Initialized(Lobby lobby)
        {
            if (lobby == null)
            {
                Debug.LogError("[LobbySetting] Lobby is not initialized in LobbySetting.");
                return;
            }
            
            lobbyNameInputField.text = lobby.Name;
            passwordInputField.text = lobby.GetLobbyPassword();
            
            var maxPlayers = lobby.MaxPlayers;
            // Set the dropdown value based on the current max players
            if (maxPlayers > 0)
            {
                int index = System.Array.IndexOf(GameConfig.Instance.maxPlayersPerLobby, maxPlayers);
                if (index >= 0)
                {
                    dropdownMaxPlayers.ChangeDropdownInfo(index);
                }
                else
                {
                    Debug.LogWarning($"[LobbySetting] Max players {maxPlayers} not found in the configured options.");
                }
            }
            else
            {
                Debug.LogWarning("[LobbySetting] Lobby Max Players is not set or invalid.");
            }
        }

        private void InitDropDown()
        {
            if (dropdownMaxPlayers == null)
            {
                Debug.LogError("[LobbySetting] Max Players Dropdown is not assigned in LobbySetting.");
                return;
            }

            // Clear existing options
            dropdownMaxPlayers.items.Clear();

            // Get options array from GameConfig
            var options = GameConfig.Instance.maxPlayersPerLobby;
            
            // Add options for max players using CreateNewItem
            foreach (var t in options)
            {
                // Use false in loop to avoid rebuilding UI multiple times
                // sprite icon can be null if you don't need icons
                dropdownMaxPlayers.CreateNewItem(t.ToString(), null, false);
            }
            
            // Initialize the dropdown after adding all items
            dropdownMaxPlayers.SetupDropdown();
            
            // Optionally set the default selected index (first item)
            if (options.Length > 0)
            {
                dropdownMaxPlayers.ChangeDropdownInfo(0);
            } 
        }

        private async void OnMaxPlayersChanged(int arg0)
        {
            try
            {
                if (!GameNet.Instance.Network.IsHost)
                {
                    throw new Exception("Only the host can change the max players.");
                }
                await GameNet.Instance.UpdateLobbyMaxPlayerAsync(GameConfig.Instance.maxPlayersPerLobby[arg0]);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbySetting] {e.Message}");
            }
        }

        private async void OnLobbyPasswordChanged(string arg0)
        {
            try
            {
                // Check if the password is empty or null
                if (string.IsNullOrEmpty(arg0) || string.IsNullOrWhiteSpace(arg0) || arg0.Length < 8)
                {
                    Debug.LogWarning("[LobbySetting] Password is empty or null, setting to default.");
                    arg0 = "12345678"; // Set to default if empty
                    PopupNotification.Instance.ShowPopup(false, "Password must be at least 8 characters long.\nUsing default password", "Warning");
                }
                
                await GameNet.Instance.UpdateLobbyPasswordAsync(arg0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbySetting] {e.Message}");
            }
        }

        private async void OnLobbyNameChanged(string arg0)
        {
            try
            {
                await GameNet.Instance.UpdateLobbyNameAsync(arg0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbySetting] {e.Message}");
            }
        }
    }
}
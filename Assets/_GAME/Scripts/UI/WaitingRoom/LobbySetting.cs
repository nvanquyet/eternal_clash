using System;
using _GAME.Scripts.Config;
using _GAME.Scripts.Networking;
using _GAME.Scripts.Networking.Lobbies;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.UI.WaitingRoom
{
    public class LobbySetting : MonoBehaviour
    {
        [SerializeField] private TMP_InputField lobbyNameInputField;
        [SerializeField] private TMP_InputField passwordInputField;
        [SerializeField] private TMP_Dropdown maxPlayersDropdown;

        private void Start()
        {
            // Initialize the dropdown with max players options
            InitDropDown();

            lobbyNameInputField.onEndEdit.AddListener(OnLobbyNameChanged);
            passwordInputField.onEndEdit.AddListener(OnLobbyPasswordChanged);
            maxPlayersDropdown.onValueChanged.AddListener(OnMaxPlayersChanged);
        }
        
        
        private void OnDestroy()
        {
            lobbyNameInputField.onEndEdit.RemoveListener(OnLobbyNameChanged);
            passwordInputField.onEndEdit.RemoveListener(OnLobbyPasswordChanged);
            maxPlayersDropdown.onValueChanged.RemoveListener(OnMaxPlayersChanged);
        }


        public void Initialized()
        {
            var lobby = LobbyManager.Instance.CurrentLobby;
            if (lobby == null)
            {
                Debug.LogError("[LobbySetting] Lobby is not initialized in LobbySetting.");
                return;
            }
            lobbyNameInputField.text = lobby.Name;
            passwordInputField.text = lobby.LobbyPassword();
            var maxPlayers = lobby.MaxPlayers;
            // Set the dropdown value based on the current max players
            if (maxPlayers > 0)
            {
                int index = System.Array.IndexOf(GameConfig.Instance.maxPlayersPerLobby, maxPlayers);
                if (index >= 0)
                {
                    maxPlayersDropdown.value = index;
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
            if (maxPlayersDropdown == null)
            {
                Debug.LogError("[LobbySetting] Max Players Dropdown is not assigned in LobbySetting.");
                return;
            }

            // Clear existing options
            maxPlayersDropdown.ClearOptions();

            // Add options for max players
            var options = GameConfig.Instance.maxPlayersPerLobby;
            foreach(var o in options)
            {
                maxPlayersDropdown.options.Add(new TMP_Dropdown.OptionData(o.ToString()));
            }
        }

        private async void OnMaxPlayersChanged(int arg0)
        {
            try
            {
                if (!NetworkController.Instance.IsHost)
                {
                    throw new Exception("Only the host can change the max players.");
                }
                var result =  await LobbyManager.Instance.UpdateMaxPlayersAsync(GameConfig.Instance.maxPlayersPerLobby[arg0]);
                if (NetworkController.Instance.IsHost && !result)
                {
                    Debug.LogError("[LobbySetting] Failed to update max players.");
                }
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
                //Check if the password is empty or null
                if (string.IsNullOrEmpty(arg0) || string.IsNullOrWhiteSpace(arg0) || arg0.Length < 8)
                {
                    Debug.LogWarning("[LobbySetting] Password is empty or null, setting to null.");
                    arg0 = "12345678"; // Set to null if empty
                    PopupNotification.Instance.ShowPopup(false, "Password must be at least 8 characters long.\n Using default password", "Warning");
                }
                
                var result = await LobbyManager.Instance.UpdateLobbyPasswordAsync(arg0);
                if (!result)
                {
                    Debug.LogError("[LobbySetting] Failed to update lobby password.");
                }
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
                var result = await LobbyManager.Instance.UpdateLobbyNameAsync(arg0);
                if (!result)
                {
                    Debug.LogError("[LobbySetting] Failed to update lobby password.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbySetting] {e.Message}");
            }
        }
    }
}
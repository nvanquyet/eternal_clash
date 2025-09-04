using System;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Lobbies;
using _GAME.Scripts.Networking;
using _GAME.Scripts.Networking.Lobbies;
using _GAME.Scripts.UI.Base;
using TMPro;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace _GAME.Scripts.UI.WaitingRoom
{
    public class WaitingRoomUI : BaseUI
    {
        [Header("Waiting Room UI Elements")]
        [SerializeField] private Button btnStartGame;
        [SerializeField] private Button btnLeaveRoom;
        [SerializeField] private Button btnRoomInformation;
        [SerializeField] private TextMeshProUGUI lobbyCodeText;
        [SerializeField] private Button btnReady;
        
        [Header("Toggle")]
        [SerializeField] private ToggleShowExtension allPlayerToggle;
        [SerializeField] private ToggleShowExtension lobbySettingsToggle;

        [Header("References")]
        [SerializeField] private PlayerList playerList;  
        [SerializeField] private LobbySetting lobbySetting;
        
        private bool isReady = false;
#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            playerList ??= GetComponentInChildren<PlayerList>();
            lobbySetting ??= GetComponentInChildren<LobbySetting>();
        }
#endif                    
        
        private void SetLobbyCode(string roomCode)
        {
            if (lobbyCodeText == null)
            {
                Debug.LogError("[WaitingRoomUI] roomCodeText is not assigned in WaitingRoomUI.");
                return;
            }
            lobbyCodeText.text = string.IsNullOrEmpty(roomCode) ? "" : $"{roomCode}";
        }
        
        
        private void Start()
        {
            RegisterEvent();
            
            //Initial UI state
            if (LobbyManager.Instance.CurrentLobby != null)
            {
                OnLobbyUpdated(LobbyManager.Instance.CurrentLobby, "Initial update");
            }
            else
            {
                Debug.LogWarning("[WaitingRoomUI] No current lobby found on start.");
            }
        }

        
        private void OnDestroy()
        {
            UnregisterEvent();
        }

        private void RegisterEvent()
        {
            btnStartGame.onClick.AddListener(OnClickStartGame);
            btnLeaveRoom.onClick.AddListener(OnClickLeaveRoom);
            btnRoomInformation.onClick.AddListener(OnClickRoomInformation);
            btnReady.onClick.AddListener(OnClickReady);
            
            //Register event lobby Update
            LobbyEvents.OnLobbyUpdated += OnLobbyUpdated;
            LobbyEvents.OnPlayerUpdated += OnPlayerUpdated;
            LobbyEvents.OnPlayerKicked += OnPlayerKicked;
        }
        
        private void UnregisterEvent()
        {
            btnStartGame.onClick.RemoveListener(OnClickStartGame);
            btnLeaveRoom.onClick.RemoveListener(OnClickLeaveRoom);
            btnRoomInformation.onClick.RemoveListener(OnClickRoomInformation);
            btnReady.onClick.RemoveListener(OnClickReady);
            
            LobbyEvents.OnLobbyUpdated -= OnLobbyUpdated;
            LobbyEvents.OnPlayerUpdated -= OnPlayerUpdated;
            LobbyEvents.OnPlayerKicked -= OnPlayerKicked;
        }
        
        
        
        private async void OnPlayerKicked(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message)
        {
            try
            {
                //Check null and id
                if (player == null || lobby == null || string.IsNullOrEmpty(player.Id) || player.Id != PlayerIdManager.PlayerId)
                {
                    Debug.LogError("[WaitingRoomUI] Player kicked event received with invalid data.");
                    return;
                }
                Debug.Log("You have been kicked from the lobby.");
                await NetworkController.Instance.DisconnectAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[WaitingRoomUI] Error handling player kicked event: {e.Message}");
            }
        }

        private void OnPlayerUpdated(Unity.Services.Lobbies.Models.Player p, Lobby arg2, string arg3)
        {
            //Check isMe 
            if (p == null) return;
            if (p.Id != PlayerIdManager.PlayerId) return;
            isReady = p.IsPlayerReady();
            // Update the ready button state
            if (btnReady != null)
            {
                btnReady.GetComponentInChildren<TextMeshProUGUI>().text = isReady ? "Unready" : "Ready";
                btnReady.interactable = true; // Enable the button
            }
            else
            {
                Debug.LogError("[WaitingRoomUI] btnReady is not assigned in WaitingRoomUI.");
            }
        }

        private void OnClickReady()
        {
            //Dísable the button while processing
            if (btnReady != null)
            {
                btnReady.interactable = false; // Disable the button to prevent multiple clicks
            }
            _ = LobbyManager.Instance.SetPlayerReadyAsync(!isReady);
        }

        private void OnLobbyUpdated(Lobby lobby, string arg2)
        {
            if (lobby == null) return;
            // Update the lobby code text
            SetLobbyCode(lobby.LobbyCode);
                
            // Initialize the player list
            playerList.Initialized();
                
            // Update the lobby UI setting
            lobbySetting.Initialized();
        }

        private void OnClickRoomInformation()
        {
            //Todo: Open Room Information UI
            HUD.Instance.Show(UIType.Lobby);
        }

        private void OnClickLeaveRoom()
        {
            //Todo: Leave Room
            //Remove lobby
            _ = NetworkController.Instance.IsHost ? LobbyManager.Instance.RemoveLobbyAsync() : LobbyManager.Instance.LeaveLobbyAsync();
        }

        private void OnClickStartGame()
        {
            //Todo: Start Game
            Debug.Log("[WaitingRoomUI] Start Game button clicked. Implement game start logic here.");
        }

    }
}

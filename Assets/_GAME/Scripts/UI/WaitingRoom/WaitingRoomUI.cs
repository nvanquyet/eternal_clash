using System;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Lobbies;
using _GAME.Scripts.Networking.Lobbies;
using _GAME.Scripts.UI.Base;
using TMPro;
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
                Debug.LogError("roomCodeText is not assigned in WaitingRoomUI.");
                return;
            }
            lobbyCodeText.text = string.IsNullOrEmpty(roomCode) ? "" : $"{roomCode}";
        }
        
        
        private void Start()
        {
            btnStartGame.onClick.AddListener(OnClickStartGame);
            btnLeaveRoom.onClick.AddListener(OnClickLeaveRoom);
            btnRoomInformation.onClick.AddListener(OnClickRoomInformation);
            btnReady.onClick.AddListener(OnClickReady);
            
            //Register event lobby Update
            LobbyEvents.OnLobbyUpdated += OnLobbyUpdated;
            LobbyEvents.OnPlayerUpdated += OnPlayerUpdated;
            LobbyEvents.OnLobbyLeft += OnLobbyLeft;
            LobbyEvents.OnLobbyRemoved += OnLobbyRemoved;
        }

        private void OnLobbyRemoved(Lobby arg1, bool arg2, string arg3)
        {
            //Swtich to the home scene
            Debug.Log("Lobby has been removed.");
            //Fake loading
            LoadingUI.Instance.RunTimed(1f, () =>
            {
                //After loading time is over, initialize services and personal information
                Debug.Log("[FirstCtrl] Fake loading done!");
            });
            SceneController.Instance.LoadSceneAsync((int) SceneDefinitions.Home);
        }

        private void OnLobbyLeft(Lobby arg1, bool arg2, string arg3)
        {
            //Switch to the home scene
            Debug.Log("You left lobby");
            LoadingUI.Instance.RunTimed(1f, () =>
            {
                //After loading time is over, initialize services and personal information
                Debug.Log("[FirstCtrl] Fake loading done!");
            });
            SceneController.Instance.LoadSceneAsync((int) SceneDefinitions.Home);
        }

        private void OnPlayerUpdated(Unity.Services.Lobbies.Models.Player p, Lobby arg2, string arg3)
        {
            isReady = LobbyExtensions.IsPlayerReady(p);
            // Update the ready button state
            if (btnReady != null)
            {
                btnReady.GetComponentInChildren<TextMeshProUGUI>().text = isReady ? "Unready" : "Ready";
                btnReady.interactable = true; // Enable the button
            }
            else
            {
                Debug.LogError("btnReady is not assigned in WaitingRoomUI.");
            }
        }

        private void OnClickReady()
        {
            //Dísable the button while processing
            if (btnReady != null)
            {
                btnReady.interactable = false; // Disable the button to prevent multiple clicks
            }
            _ = LobbyExtensions.SetPlayerReadyAsync(!isReady);
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
            _ = LobbyExtensions.IsHost() ? LobbyExtensions.RemoveLobby() : LobbyExtensions.LeaveLobby();
        }

        private void OnClickStartGame()
        {
            //Todo: Start Game
            Debug.Log("Start Game button clicked. Implement game start logic here.");
        }

        private void OnDestroy()
        {
            btnStartGame.onClick.RemoveListener(OnClickStartGame);
            btnLeaveRoom.onClick.RemoveListener(OnClickLeaveRoom);
            btnRoomInformation.onClick.RemoveListener(OnClickRoomInformation);
            btnReady.onClick.RemoveListener(OnClickReady);
            
            LobbyEvents.OnLobbyUpdated -= OnLobbyUpdated;
            LobbyEvents.OnPlayerUpdated -= OnPlayerUpdated;
            LobbyEvents.OnLobbyLeft -= OnLobbyLeft;
            LobbyEvents.OnLobbyRemoved -= OnLobbyRemoved;
        }
    }
}

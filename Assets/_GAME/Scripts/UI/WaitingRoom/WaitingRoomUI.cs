using System;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Data;
using _GAME.Scripts.Networking;
using _GAME.Scripts.Networking.Lobbies;
using _GAME.Scripts.UI.Base;
using Michsky.MUIP;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.UI.WaitingRoom
{
    public class WaitingRoomUI : BaseUI
    {
        [Header("Waiting Room UI Elements")]
        [SerializeField] private Button btnLeaveRoom;
        [SerializeField] private Button  btnSetting;
        [SerializeField] private TextMeshProUGUI lobbyCodeText;
        [SerializeField] private TextMeshProUGUI playerUsernameText;
        [SerializeField] private TextMeshProUGUI playerUserIdText;
        
        [SerializeField] private ButtonManager  btnReady;
        [SerializeField] private ButtonManager btnStartGame;

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
            if (GameNet.Instance.Lobby.CurrentLobby != null)
            {
                OnLobbyUpdated(GameNet.Instance.Lobby.CurrentLobby);
            }
            else
            {
                Debug.LogWarning("[WaitingRoomUI] No current lobby found on start.");
            }

            LobbyEvents.OnPlayerLeft += OnPlayerLeft;
            
            //Call to hide loading
            LoadingUI.Instance.SetProgress(1, 1, "Finished", () =>
            {
                LoadingUI.Instance.Complete();
            });
            
            InitializeUserInformation();
        }

        private void InitializeUserInformation()
        {
            if (playerUsernameText != null)
            {
                playerUsernameText.text = LocalData.UserName;
            }
            else
            {
                Debug.LogError("[WaitingRoomUI] playerUsernameText is not assigned in WaitingRoomUI.");
            }

            if (playerUserIdText != null)
            {
                playerUserIdText.text = PlayerIdManager.PlayerId;
            }
            else
            {
                Debug.LogError("[WaitingRoomUI] playerUserIdText is not assigned in WaitingRoomUI.");
            }
        }

        private async void OnPlayerLeft(Unity.Services.Lobbies.Models.Player p, Lobby lobby, string message)
        {
            try
            {
                //if player is me, Disconnect myself
                if (PlayerIdManager.PlayerId == p.Id)
                {
                    Debug.Log($"[WaitingSpawnController] I was kicked from lobby: {message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[WaitingRoomUI] Error handling player left: {e}");
            }
        }


        private void OnDestroy()
        {
            LobbyEvents.OnPlayerLeft += OnPlayerLeft;
            UnregisterEvent();
        }

        private void RegisterEvent()
        {
            btnStartGame.onClick.AddListener(OnClickStartGame);
            btnLeaveRoom.onClick.AddListener(OnClickLeaveRoom);
            btnSetting.onClick.AddListener(OnClickSetting);
            btnReady.onClick.AddListener(OnClickReady);
            
            //Register event lobby Update
            LobbyEvents.OnPlayerUpdated += OnPlayerUpdated;
            LobbyEvents.OnLobbyUpdated += OnLobbyUpdated;
            LobbyEvents.OnLobbyNotFound += OnLobbyNotFound;
        }
        
        private void UnregisterEvent()
        {
            btnStartGame.onClick.RemoveListener(OnClickStartGame);
            btnLeaveRoom.onClick.RemoveListener(OnClickLeaveRoom);
            btnSetting.onClick.RemoveListener(OnClickSetting);
            btnReady.onClick.RemoveListener(OnClickReady);
            
            LobbyEvents.OnPlayerUpdated -= OnPlayerUpdated;
            LobbyEvents.OnLobbyUpdated -= OnLobbyUpdated;
            LobbyEvents.OnLobbyNotFound -= OnLobbyNotFound;
        }
        
        
        
        private async void OnLobbyNotFound()
        {
            //Return to home UI
            await GameNet.Instance.Network.StopAsync();
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
                btnReady.SetText(isReady ? "Unready" : "Ready");
                btnReady.Interactable(true); // Enable the button
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
                btnReady.Interactable(false); // Disable the button to prevent multiple clicks
            }
            _ = GameNet.Instance.SetPlayerReadyAsync(!isReady);
        }

        private void OnLobbyUpdated(Lobby lobby)
        {
            if (lobby == null) return;
            
            btnStartGame?.gameObject.SetActive(GameNet.Instance.Network.IsHost);
            
            // Update the lobby code text
            SetLobbyCode(lobby.LobbyCode);
                
            // Initialize the player list
            playerList.Initialized(lobby);
                
            // Update the lobby UI setting
            lobbySetting.Initialized(lobby);
        }

        private void OnClickSetting()
        {
            //Todo: Open Room Information UI
            SettingsPopup.Instance.Show();
        }

        private void OnClickLeaveRoom()
        {
            //Remove lobby
            _ = GameNet.Instance.Network.IsHost ? GameNet.Instance.RemoveLobbyAsync() : GameNet.Instance.LeaveLobbyAsync();
        }

        private void OnClickStartGame()
        {
            //Todo: Start Game
            Debug.Log("[WaitingRoomUI] Start Game button clicked. Implement game start logic here.");
            //Switch to game scene
            if (GameNet.Instance.Network.IsHost)
            {
                GameNet.Instance.Network.StartGame();
            }
            else
            {
                Debug.LogWarning("[WaitingRoomUI] Only the host can start the game.");
            }
        }

    }
}

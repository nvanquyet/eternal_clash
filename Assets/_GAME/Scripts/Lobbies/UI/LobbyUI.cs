using System;
using System.Collections.Generic;
using System.Linq;
using _GAME.Scripts.Controller;
using _GAME.Scripts.UI.Base;
using _GAME.Scripts.UI.Home;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.Lobbies.UI
{
    public class LobbyUI : BaseUI
    {
        [Header("Top Bar")]
        [SerializeField] private TMP_InputField lobbyNameInput;   // chỉ host sửa
        [SerializeField] private TMP_Text lobbyCodeText;
        [SerializeField] private TMP_InputField passwordInput;    // host sửa; mọi người đều **thấy** giá trị
        [SerializeField] private HostOnlyInteractable topBarHostOnly;
        [SerializeField] private Button btnBack;

        [Header("Players Grid")]
        [SerializeField] private PlayerSlotUI[] allSlots;
        [SerializeField] private GameObject topSlots;
        [SerializeField] private GameObject bottomSlots;

        [Header("Bottom Bar")]
        [SerializeField] private Button readyButton;              
        [SerializeField] private TMP_Text readyButtonLabel;
        [SerializeField] private Button startGameButton;          
        [SerializeField] private TMP_Text startGameLabel;
        [SerializeField] private NumberPlayerDropdown numberPlayerDropdown;
        [SerializeField] private HostOnlyInteractable bottomHostOnly;

        [Header("Config")]
        [SerializeField] private List<int> numberPlayerAvailable = new List<int> { 4, 8 };

        private Unity.Services.Lobbies.Models.Lobby _lobby;
        private LobbyHandler _lobbyHandler;

        private bool IsHosting => _lobby != null &&
                                  _lobby.HostId == AuthenticationService.Instance.PlayerId;

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            allSlots = GetComponentsInChildren<PlayerSlotUI>(true);
        }
#endif

        private void Awake()
        {
           
            if (!startGameLabel) startGameLabel = startGameButton.GetComponentInChildren<TMP_Text>(true);
            if (!readyButtonLabel) readyButtonLabel = readyButton.GetComponentInChildren<TMP_Text>(true);

            // Get lobby handler reference
            _lobbyHandler = LobbyHandler.Instance;

            // Setup UI event listeners
            lobbyNameInput.onEndEdit.AddListener(OnLobbyNameChanged);
            passwordInput.onEndEdit.AddListener(OnPasswordChanged);
            readyButton.onClick.AddListener(OnClickReady);
            startGameButton.onClick.AddListener(OnClickStartGame);
            btnBack.onClick.AddListener(OnPlayerQuit);
            numberPlayerDropdown.SubscribeCallback(ApplyPlayerAmount);
        }

        private void OnEnable()
        {
            AppExitController.Instance.RegisterCleanupTask("lobby_cleanup", OnQuit, order: 0);
        }
        
        private void OnDisable()
        {
            AppExitController.Instance.UnregisterCleanupTask("lobby_cleanup");
        }

        private void Start()
        {
            // Subscribe to lobby events
            SubscribeToLobbyEvents();
        }

        private void SubscribeToLobbyEvents()
        {
            // if (_lobbyHandler != null)
            // {
            //     _lobbyHandler.OnLobbyUpdated += OnLobbyUpdated;
            //     _lobbyHandler.OnLobbyLeft += OnLobbyLeft;
            //     _lobbyHandler.OnLobbyRemoved += OnLobbyRemoved;
            // }

            // Subscribe to static events
            LobbyEvents.OnPlayerJoined += OnPlayerJoined;
            LobbyEvents.OnPlayerLeft += OnPlayerLeft;
            LobbyEvents.OnPlayerUpdated += OnPlayerUpdated;
            LobbyEvents.OnGameStarted += OnGameStarted;
            
            //LobbyEvent
            LobbyEvents.OnLobbyUpdated += OnLobbyUpdated;
            LobbyEvents.OnLobbyLeft += OnLobbyLeft;
            LobbyEvents.OnLobbyRemoved += OnLobbyRemoved;
            LobbyEvents.OnLobbyCreated += OnLobbyCreated;
        }

        private void UnsubscribeFromLobbyEvents()
        {
            // if (_lobbyHandler != null)
            // {
            //     _lobbyHandler.OnLobbyUpdated -= OnLobbyUpdated;
            //     _lobbyHandler.OnLobbyLeft -= OnLobbyLeft;
            //     _lobbyHandler.OnLobbyRemoved -= OnLobbyRemoved;
            // }

            // Unsubscribe from static events
            LobbyEvents.OnPlayerJoined -= OnPlayerJoined;
            LobbyEvents.OnPlayerLeft -= OnPlayerLeft;
            LobbyEvents.OnPlayerUpdated -= OnPlayerUpdated;
            LobbyEvents.OnGameStarted -= OnGameStarted;
            
            //LobbyEvent
            LobbyEvents.OnLobbyUpdated -= OnLobbyUpdated;
            LobbyEvents.OnLobbyLeft -= OnLobbyLeft;
            LobbyEvents.OnLobbyRemoved -= OnLobbyRemoved;
            LobbyEvents.OnLobbyCreated -= OnLobbyCreated;
        }

        private void OnDestroy()
        {
            readyButton.onClick.RemoveListener(OnClickReady);
            startGameButton.onClick.RemoveListener(OnClickStartGame);
            lobbyNameInput.onEndEdit.RemoveListener(OnLobbyNameChanged);
            passwordInput.onEndEdit.RemoveListener(OnPasswordChanged);
            btnBack.onClick.RemoveListener(OnPlayerQuit);
            numberPlayerDropdown.UnsubscribeCallback(ApplyPlayerAmount);
            UnsubscribeFromLobbyEvents();
        }

        // ====== Event Handlers ======
        private void OnLobbyUpdated(Lobby lobby, string message)
        {
            if (lobby != null)
            {
                SetLobby(lobby);
            }
        }
        
        private void OnLobbyCreated(Lobby lobby, bool isSuccess, string message)
        {
            if (isSuccess && lobby != null)
            {
                SetLobby(lobby);
            }
        }
        
        private void OnLobbyLeft(Lobby lobby, bool isSuccess, string message)
        {
            Debug.Log("Left lobby, closing UI");
            // Close lobby UI and return to main menu
            HUD.Instance.Show(UIType.Home);
            var homeUI = HUD.Instance.GetUI<HomeUI>(UIType.Home);
            homeUI.ReInit();
        }

        private void OnLobbyRemoved(Lobby lobby, bool isSuccess, string message)
        {
            Debug.Log("Lobby was removed, closing UI");
            HUD.Instance.Show(UIType.Home);
            var homeUI = HUD.Instance.GetUI<HomeUI>(UIType.Home);
            homeUI.ReInit();
        }

        private void OnPlayerJoined(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message)
        {
            Debug.Log($"Player joined: {GetPlayerDisplayName(player)}");
            // SetLobby will be called automatically by OnLobbyUpdated
        }

        private void OnPlayerLeft(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message)
        {
            Debug.Log($"Player left: {GetPlayerDisplayName(player)}");
            // SetLobby will be called automatically by OnLobbyUpdated
        }

        private void OnPlayerUpdated(Unity.Services.Lobbies.Models.Player player, Lobby lobby, string message)
        {
            Debug.Log($"Player updated: {GetPlayerDisplayName(player)}");
            // SetLobby will be called automatically by OnLobbyUpdated
        }

        private void OnGameStarted(Lobby lobby, string message)
        {
            Debug.Log("Game started! Loading game scene...");
            // TODO: Load game scene
            // SceneManager.LoadScene("GameScene");
        }

        // ====== Entry point gọi từ code mạng khi join/host/update ======
        public void SetLobby(Lobby info)
        {
            _lobby = info;
            if (_lobby == null) return;

            // Top Bar
            lobbyNameInput.text = _lobby.Name ?? string.Empty;
            lobbyCodeText.text = _lobby.LobbyCode ?? string.Empty;

            // Password: lấy từ Lobby.Data
            string pwd = string.Empty;
            if (_lobby.Data != null &&
                _lobby.Data.TryGetValue("Password", out var pwdObj) &&
                pwdObj != null)
            {
                pwd = pwdObj.Value ?? string.Empty;
            }
            passwordInput.SetTextWithoutNotify(pwd);
            passwordInput.interactable = IsHosting;

            // Host permissions
            topBarHostOnly.Apply(IsHosting);
            bottomHostOnly.Apply(IsHosting);

            // Player count dropdown
            numberPlayerDropdown.Init(IsHosting, _lobby.MaxPlayers);

            // Show/hide bottom slots
            ShowBottomSlots(_lobby.MaxPlayers);

            // Bind player slots
            for (int i = 0; i < allSlots.Length; i++)
            {
                if (i < _lobby.Players.Count)
                {
                    var p = _lobby.Players[i];
                    bool isMe = p.Id == AuthenticationService.Instance.PlayerId;
                    allSlots[i].Bind(p, IsHosting, isMe, OnKickPlayer);
                    allSlots[i].SetUserNameValueChange(isMe, OnPlayerUserNameChanged);
                }
                else
                {
                    allSlots[i].BindEmpty();
                }
            }

            RefreshButtons();
        }

        private async void OnPlayerUserNameChanged(string username)
        {
            try
            {
                if (_lobbyHandler == null || _lobby == null) return;
            
                try
                {
                    await _lobbyHandler.SetDisplayNameAsync(_lobby.Id, username);
                    Debug.Log($"Updated display name to: {username}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to update display name: {e.Message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during display name change: {e.Message}");
                throw; // TODO handle exception
            }
        }

        private static bool TryGetBool(Unity.Services.Lobbies.Models.Player p, string key, out bool value)
        {
            value = false;
            if (p?.Data == null) return false;
            if (!p.Data.TryGetValue(key, out var obj) || obj == null) return false;
            return bool.TryParse(obj.Value, out value);
        }

        private void RefreshButtons()
        {
            if (_lobby == null) return;

            var me = _lobby.Players.FirstOrDefault(p => p.Id == AuthenticationService.Instance.PlayerId);
            bool iAmReady = TryGetBool(me, "IsReady", out var ready) && ready;

            readyButtonLabel.text = iAmReady ? "Unready" : "Ready";
            startGameLabel.text = "Start Game";

            // Game start conditions
            bool enoughPlayers = _lobby.Players.Count >= 2;
            bool everyoneReady = _lobby.Players.Count > 0 &&
                                 _lobby.Players.All(p => TryGetBool(p, "IsReady", out var r) && r);

            startGameButton.interactable = IsHosting && enoughPlayers && everyoneReady;
        }

        // ====== UI Events ======
        private async void OnClickReady()
        {
            try
            {
                if (_lobbyHandler == null || _lobby == null) return;

                var me = _lobby.Players.FirstOrDefault(p => p.Id == AuthenticationService.Instance.PlayerId);
                bool currentReady = TryGetBool(me, "IsReady", out var ready) && ready;

                try
                {
                    await _lobbyHandler.ToggleReadyAsync(_lobby.Id, !currentReady);
                    Debug.Log($"Toggled ready state to: {!currentReady}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to toggle ready: {e.Message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during ready toggle: {e.Message}");
                throw; // TODO handle exception
            }
        }

        private async void OnClickStartGame()
        {
            try
            {
                if (!IsHosting || _lobbyHandler == null || _lobby == null) return;

                try
                {
                    Debug.Log("Game starting...");
                    //Todo: Start game logic here
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to start game: {e.Message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during start game: {e.Message}");
                throw; // TODO handle exception
            }
        }

        // ====== Host-only Actions ======
        private async void OnLobbyNameChanged(string newName)
        {
            if (_lobby == null || _lobbyHandler == null) return;

            if (!IsHosting)
            {
                lobbyNameInput.SetTextWithoutNotify(_lobby.Name ?? string.Empty);
                return;
            }

            try
            {
                await _lobbyHandler.UpdateLobbyNameAsync(_lobby.Id, newName);
                Debug.Log($"Updated lobby name to: {newName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to update lobby name: {e.Message}");
                // Revert on failure
                lobbyNameInput.SetTextWithoutNotify(_lobby.Name ?? string.Empty);
            }
        }

        private async void OnPasswordChanged(string newPwd)
        {
            if (_lobby == null || _lobbyHandler == null) return;

            if (!IsHosting)
            {
                // Non-host: revert to current value
                string current = string.Empty;
                if (_lobby.Data != null &&
                    _lobby.Data.TryGetValue("Password", out var obj) &&
                    obj != null)
                {
                    current = obj.Value ?? string.Empty;
                }
                passwordInput.SetTextWithoutNotify(current);
                return;
            }
            if (string.IsNullOrEmpty(newPwd) || newPwd.Length < 8)
            {
                Debug.LogWarning("Password phải có ít nhất 8 ký tự!");
        
                // Revert lại giá trị cũ
                string current = string.Empty;
                if (_lobby.Data != null &&
                    _lobby.Data.TryGetValue("Password", out var obj) &&
                    obj != null)
                {
                    current = obj.Value ?? string.Empty;
                }
                passwordInput.SetTextWithoutNotify(current);
                return;
            }

            try
            {
                await _lobbyHandler.UpdateLobbyPasswordInDataAsync(_lobby.Id, newPwd, true, true);
                Debug.Log($"Updated lobby password");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to update password: {e.Message}");
            }
        }

        private async void ApplyPlayerAmount(int value)
        {
            if (_lobby == null || _lobbyHandler == null) return;

            if (!IsHosting) return;

            if (value == 4 && _lobby.Players.Count > 4)
            {
                Debug.Log("Cannot reduce max players to 4 while lobby has more than 4 players.");
                numberPlayerDropdown.SetValueWithoutNotify(8);
                return;
            }

            try
            {
                ShowBottomSlots(value);
                for (int i = 4; i < allSlots.Length; i++)
                {
                    if (value == 4) allSlots[i].BindEmpty();
                }
                //Set interact dropdown if is host
                numberPlayerDropdown.SetInteractable(false);
                await _lobbyHandler.UpdateLobbyMaxPlayersAsync(_lobby.Id, value);
                numberPlayerDropdown.SetInteractable(true);
                Debug.Log($"Updated max players to: {value}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to update max players: {e.Message}");
                // Revert on failure
                numberPlayerDropdown.SetValueWithoutNotify(_lobby.MaxPlayers);
            }
        }

        private bool ShowBottomSlots(int maxPlayer)
        {
            bool show = maxPlayer >= 8;

            if (!bottomSlots)
            {
                Debug.LogWarning("Bottom slots GameObject is not assigned.");
                return false;
            }

            bottomSlots.SetActive(show);
            return show;
        }

        private async void OnKickPlayer(Unity.Services.Lobbies.Models.Player p, PlayerSlotUI slot)
        {
            if (!IsHosting || p == null || _lobbyHandler == null || _lobby == null) return;

            try
            {
                await _lobbyHandler.KickPlayerAsync(_lobby.Id, p.Id);
                Debug.Log($"Kicked player: {GetPlayerDisplayName(p)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to kick player: {e.Message}");
            }
        }

        private async void OnPlayerQuit()
        {
            try
            {
                if (_lobbyHandler == null || _lobby == null) return;

                try
                {
                    string myId = AuthenticationService.Instance.PlayerId;
                    if (IsHosting)
                    {
                        // If hosting, remove the lobby
                        await _lobbyHandler.RemoveLobbyAsync(_lobby.Id);
                        Debug.Log("Removed lobby as host");
                    }
                    else
                    {
                        // If not hosting, leave the lobby
                        await _lobbyHandler.LeaveLobbyAsync(_lobby.Id, myId, IsHosting);
                        Debug.Log("Left lobby");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to leave lobby: {e.Message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during player quit: {e.Message}");
                // TODO handle exception
            }
        }

        // ====== Helper Methods ======
        private string GetPlayerDisplayName(Unity.Services.Lobbies.Models.Player player)
        {
            if (player?.Data != null && player.Data.TryGetValue("DisplayName", out var nameData))
            {
                return nameData.Value;
            }
            return $"Player_{player?.Id?[..6] ?? "Unknown"}";
        }

        private void OnQuit()
        {
            //Remove or leave lobby when application quits
            if (_lobbyHandler != null && _lobby != null)
            {
                try
                {
                    _ = IsHosting ? _lobbyHandler.RemoveLobbyAsync(_lobby.Id) : _lobbyHandler.LeaveLobbyAsync(_lobby.Id, AuthenticationService.Instance.PlayerId, IsHosting);
                    Debug.Log("Application quitting, leaving lobby...");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to leave lobby on quit: {e.Message}");
                }
            }
        }
    }
}
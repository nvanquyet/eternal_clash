using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.Lobbies.UI.Version2
{
    public class LobbyUI : MonoBehaviour
    {
        
        [Header("Top Bar")]
        [SerializeField] private TMP_InputField lobbyNameInput;   // chỉ host sửa
        [SerializeField] private TMP_Text lobbyCodeText;
        [SerializeField] private TMP_InputField passwordInput;    // chỉ host sửa (nhưng show toggle ở script khác)
        [SerializeField] private HostOnlyInteractable topBarHostOnly;

        [Header("Players Grid")]
        [SerializeField] private PlayerSlotUI[] allSlots;

        [SerializeField] private GameObject topSlots;
        [SerializeField] private GameObject bottomSlots;
        [Header("Bottom Bar")]
        [SerializeField] private Button readyButton;              // Enter Ready/Unready
        [SerializeField] private TMP_Text readyButtonLabel;
        [SerializeField] private Button startGameButton;          // chỉ host
        [SerializeField] private TMP_Text startGameLabel;
        [SerializeField] private NumberPlayerDropdown numberPlayerDropdown;
        [SerializeField] private HostOnlyInteractable bottomHostOnly;
        
        
        [Header("Config")]
        [SerializeField] private List<int> numberPlayerAvailable = new List<int>{ 4; 8 };


        // State hiện tại (đẩy từ service mạng vào)
        private LobbyInfo _lobby;

        private void Awake()
        {
            // Chặn null
            startGameLabel ??= startGameButton.GetComponentInChildren<TMP_Text>();
            readyButtonLabel ??= readyButton.GetComponentInChildren<TMP_Text>();

            readyButton.onClick.AddListener(OnClickReady);
            startGameButton.onClick.AddListener(OnClickStartGame);
        }

        private void OnDestroy()
        {
            readyButton.onClick.RemoveListener(OnClickReady);
            startGameButton.onClick.RemoveListener(OnClickStartGame);
        }

        // ====== Entry point gọi từ code mạng khi join/host/update ======
        public void SetLobby(LobbyInfo info)
        {
            _lobby = info;

            // Top
            lobbyNameInput.text = info.LobbyName;
            lobbyCodeText.text  = info.LobbyCode;
            passwordInput.text  = info.Password;

            // Quyền host
            topBarHostOnly.Apply(info.LocalIsHost);
            bottomHostOnly.Apply(info.LocalIsHost);
            startGameButton.interactable = info.LocalIsHost;

            // Dropdown số người
            numberPlayerDropdown.Init(info.LocalIsHost, numberPlayerAvailable, ApllyPlayerAmount);
            ApllyPlayerAmount(numberPlayerAvailable[0]);
            4
            // Gắn dữ liệu người chơi
            for (int i = 0; i < info.Players.Count && i < allSlots.Length; i++)
            {
                var p = info.Players[i];
                bool isLocal = p.PlayerId == info.LocalPlayerId;
                allSlots[i].Bind(p, info.LocalIsHost, isLocal);
            }

            ApplyMaxPlayersToSlots();
            RefreshButtons();
        }

        // Gọi khi server báo có người Ready/Unready/Join/Leave/Đổi tên
        public void RefreshFromServer(LobbyInfo latest)
        {
            SetLobby(latest);
        }

        private void ApplyMaxPlayersToSlots()
        {
            // Đồng bộ ẩn/hiện theo dropdown (nếu server khác báo, set lại dropdown cũng được)
            if (numberPlayerDropdown != null && numberPlayerDropdown.CurrentMax != _lobby.MaxPlayers)
            {
                // nếu quyền/luồng từ server khác, đồng bộ lại
                numberPlayerDropdown.Init(_lobby.LocalIsHost, _lobby.MaxPlayers);
            }
        }

        private void RefreshButtons()
        {
            var me = _lobby.Players.FirstOrDefault(p => p.PlayerId == _lobby.LocalPlayerId);
            bool iAmReady = me != null && me.IsReady;

            readyButtonLabel.text = iAmReady ? "Unready" : "Ready";
            startGameLabel.text = "Start Game";

            // Chỉ host start được; và thường chỉ khi >=2 người & tất cả Ready (tùy luật)
            bool everyoneReady = _lobby.Players
                .Where(p => p.IsConnected)
                .All(p => p.IsReady);

            startGameButton.interactable = _lobby.LocalIsHost && everyoneReady;
        }

        // ====== UI events ======
        private void OnClickReady()
        {
            // TODO: Gọi service: ToggleReady(localPlayerId)
            // Sau đó, service trả về state mới -> RefreshFromServer(...)
            Debug.Log("[LobbyUI] Toggle Ready clicked");
        }

        private void OnClickStartGame()
        {
            if (!_lobby.LocalIsHost) return;
            // TODO: Gọi service StartGame() => load scene, lock lobby, v.v.
            Debug.Log("[LobbyUI] Host Start Game clicked");
        }

        // ====== Hook các Input chỉ Host được sửa ======
        public void OnLobbyNameChanged(string newName)
        {
            if (!_lobby.LocalIsHost) { lobbyNameInput.text = _lobby.LobbyName; return; }
            // TODO: Gọi service UpdateLobbyName(newName)
        }

        public void OnPasswordChanged(string newPwd)
        {
            if (!_lobby.LocalIsHost) { passwordInput.text = _lobby.Password; return; }
            // TODO: Gọi service UpdateLobbyPassword(newPwd)
        }

        private void ApllyPlayerAmount(int value)
        {
            Debug.Log($"Value Selected: {value}");

            var showBottomSlots = value == 8;
            bottomSlots?.SetActive(showBottomSlots);
            if (showBottomSlots)
            {
                //BindEmpty
                for (var i = 4; i < allSlots.Count; i++)
                {
                    allSlots[i].BindEmpty();
                }
            }
            else
            {
                //KickOtherPlayer
                for (var i = 4; i < allSlots.Count; i++)
                {
                    //Check player available
                    var available = true;
                    //Kick player
                        
                }
            }
        }
    }
}

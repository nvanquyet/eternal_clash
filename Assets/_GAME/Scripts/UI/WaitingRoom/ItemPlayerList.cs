using _GAME.Scripts.Networking;
using _GAME.Scripts.Networking.Lobbies;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.UI.WaitingRoom
{
    public class ItemPlayerList : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI playerNameText;
        [SerializeField] private Toggle readyToggle;
        [SerializeField] private Button kickButton;

        private Unity.Services.Lobbies.Models.Player player;
        
#if UNITY_EDITOR
        private void OnValidate()
        {
            playerNameText ??= GetComponentInChildren<TMPro.TextMeshProUGUI>();
            readyToggle ??= GetComponentInChildren<Toggle>();
            kickButton ??= GetComponentInChildren<Button>();
        }
#endif


        private void Start()
        {
            this.kickButton.onClick.AddListener(OnKickButtonClicked);
            this.readyToggle.onValueChanged.AddListener(OnToggleValueChanged);
        }

        private void OnDestroy()
        {
            this.kickButton.onClick.RemoveListener(OnKickButtonClicked);
            this.readyToggle.onValueChanged.RemoveListener(OnToggleValueChanged);
        }

        private void OnKickButtonClicked()
        {
            //Todo: Call the method to kick player from lobby
            if (player == null)
            {
                Debug.LogError("[ItemPlayerList] Player is not initialized in ItemPlayerList.");
                return;
            }
            //Disable the kick button to prevent multiple clicks
            kickButton.interactable = false;
            GameNet.Instance.KickPlayerAsync(player.Id);
        }

        private void OnToggleValueChanged(bool arg0)
        {
            //Todo: Call the method to set player ready state
            if (player == null)
            {
                Debug.LogError("[ItemPlayerList] Player is not initialized in ItemPlayerList.");
                return;
            }
            //Disable the toggle to prevent multiple clicks
            GameNet.Instance.SetPlayerReadyAsync(arg0);
        }

        public void Initialize(Unity.Services.Lobbies.Models.Player player, bool isMe, bool isHost)
        {
            this.player = player;
            
            if(isHost && isMe) SetKickButtonActive(false);
            else SetKickButtonActive(isHost);
            
            SetPlayerName(player.GetPlayerDisplayName());
            SetReadyState(player.IsPlayerReady());
        }        
        private void SetPlayerName(string playerName)
        {
            if (playerNameText == null)
            {
                Debug.LogError("[ItemPlayerList] playerNameText is not assigned in ItemPlayerList.");
                return;
            }
            playerNameText.text = playerName;
        }
        
        private void SetReadyState(bool isReady)
        {
            if (readyToggle == null)
            {
                Debug.LogError("[ItemPlayerList] readyToggle is not assigned in ItemPlayerList.");
                return;
            }
            readyToggle.isOn = isReady;
        }
        
        private void SetKickButtonActive(bool isActive)
        {
            if (kickButton == null)
            {
                Debug.LogError("[ItemPlayerList] kickButton is not assigned in ItemPlayerList.");
                return;
            }
            kickButton.gameObject.SetActive(isActive);
        }
    }
}

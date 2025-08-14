using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.Lobbies.UI.Version2
{
    public class PlayerSlotUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private TMP_InputField nameInput; // nếu chỉ hiển thị -> set .interactable=false
        [SerializeField] private Image avatarImage;
        [SerializeField] private GameObject readyBadge;    // ví dụ đổi frame/hiệu ứng khi Ready
        [SerializeField] private GameObject notReadyFrame; // khung khác khi chưa Ready (tùy chọn)
        [SerializeField] private GameObject waitingPanel;  // hiển thị "Waiting...".

        [Header("Styles")]
        [SerializeField] private Sprite defaultAvatar;
        
        public string BoundPlayerId { get; private set; }
        public void BindEmpty()
        {
            nameInput.text = "Unknow";
            nameInput.interactable = false;

            waitingPanel.SetActive(true);
            avatarImage.enabled = false;

            readyBadge.SetActive(false);
            if (notReadyFrame) notReadyFrame.SetActive(false);
        }

        public void Bind(PlayerInfo p, bool localIsHost, bool isLocalPlayer)
        {
            BoundPlayerId = p.PlayerId;

            waitingPanel.SetActive(!p.IsConnected);
            avatarImage.enabled = p.IsConnected;

            nameInput.text = string.IsNullOrEmpty(p.DisplayName) ? "Player" : p.DisplayName;
            nameInput.interactable = isLocalPlayer; // chỉ cho chính mình sửa tên (UI). Đồng bộ tên lên server ở nơi khác

            avatarImage.sprite = p.Avatar ? p.Avatar : defaultAvatar;

            readyBadge.SetActive(p.IsReady);
            if (notReadyFrame) notReadyFrame.SetActive(!p.IsReady && p.IsConnected);
        }

        // Để LobbyUI gọi khi ai đó Ready/Unready
        public void SetReadyVisual(bool isReady)
        {
            readyBadge.SetActive(isReady);
            if (notReadyFrame) notReadyFrame.SetActive(!isReady);
        }

        public void SetHidden(bool hidden) => gameObject.SetActive(!hidden);
    }
}

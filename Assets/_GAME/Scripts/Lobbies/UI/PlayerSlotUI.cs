using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace _GAME.Scripts.Lobbies.UI
{
    public class PlayerSlotUI : MonoBehaviour
    {
        [Header("Refs")] [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private Image avatarImage;
        [SerializeField] private GameObject readyBadge;
        [SerializeField] private GameObject notReadyFrame;
        [SerializeField] private GameObject waitingPanel;

        [Header("Kick Button")] [SerializeField]
        private Button kickButton; // chỉ hiện khi host & không phải chính mình

        [SerializeField] private Button confirmKickButton; // hiện khi nhấn kick để xác nhận

        private Unity.Services.Lobbies.Models.Player currentPlayer;
        private Action<Unity.Services.Lobbies.Models.Player, PlayerSlotUI> onKickPlayer;

        private bool isKickConfirmationVisible;

        private void Start()
        {
            if (kickButton) kickButton.onClick.AddListener(ToggleKickConfirmation);
            if (confirmKickButton)
            {
                confirmKickButton.onClick.AddListener(() =>
                {
                    if (currentPlayer == null) return;
                    onKickPlayer?.Invoke(currentPlayer, this);
                    isKickConfirmationVisible = false;
                    confirmKickButton.gameObject.SetActive(false);
                    Debug.Log($"Kick player with ID: {currentPlayer.Id}");
                });

                confirmKickButton.gameObject.SetActive(false); // ẩn nút xác nhận ban đầu
            }
        }

        private void OnDestroy()
        {
            //Remove listeners to prevent memory leaks
            if (kickButton) kickButton.onClick.RemoveListener(ToggleKickConfirmation);
            if (confirmKickButton) confirmKickButton.onClick.RemoveAllListeners();
            if (nameInput) nameInput.onEndEdit.RemoveAllListeners();
        }

        private void ToggleKickConfirmation()
        {
            if (!confirmKickButton) return;
            isKickConfirmationVisible = !isKickConfirmationVisible;
            confirmKickButton.gameObject.SetActive(isKickConfirmationVisible);
        }

        public void BindEmpty()
        {
            currentPlayer = null;
            onKickPlayer = null;

            if (nameInput)
            {
                nameInput.SetTextWithoutNotify("Waiting ...");
                nameInput.interactable = false;
            }

            if (waitingPanel) waitingPanel.SetActive(true);
            if (avatarImage) avatarImage.enabled = false;

            if (readyBadge) readyBadge.SetActive(false);
            if (notReadyFrame) notReadyFrame.SetActive(false);

            if (kickButton)
            {
                kickButton.gameObject.SetActive(false);
                kickButton.interactable = false;
            }

            if (confirmKickButton) confirmKickButton.gameObject.SetActive(false);
            isKickConfirmationVisible = false;
        }

        public void Bind(Unity.Services.Lobbies.Models.Player p, bool localIsHost, bool isMe,
            Action<Unity.Services.Lobbies.Models.Player, PlayerSlotUI> onKickPlayer)
        {
            if (p == null)
            {
                BindEmpty();
                return;
            }

            currentPlayer = p;
            this.onKickPlayer = onKickPlayer;

            // Hiển thị slot đã có người
            if (waitingPanel) waitingPanel.SetActive(false);
            if (avatarImage) avatarImage.enabled = true;

            // Tên hiển thị
            string displayName = TryGetString(p, "DisplayName", out var dn)
                ? dn
                : $"P{p.Id[..Mathf.Min(4, p.Id.Length)]}";
            if (nameInput)
            {
                nameInput.SetTextWithoutNotify(displayName);
                nameInput.interactable = isMe; // chỉ cho chính mình sửa tên (đồng bộ ở nơi khác)
            }

            // Avatar
            if (TryGetString(p, "AvatarId", out var avatarId))
            {
                // TODO: Map avatarId -> Sprite
                // avatarImage.sprite = AvatarDatabase.Get(avatarId);
            }

            // Ready visual
            bool isReady = TryGetBool(p, "IsReady", out var r) && r;
            SetReadyVisual(isReady);

            // Kick UI
            if (kickButton)
            {
                bool canKick = localIsHost && !isMe;
                kickButton.gameObject.SetActive(canKick);
                kickButton.interactable = canKick;
            }

            if (confirmKickButton)
            {
                confirmKickButton.gameObject.SetActive(false);
                isKickConfirmationVisible = false;
            }
        }

        public void SetUserNameValueChange(bool isMe, UnityAction<string> onValueChange)
        {
            if (!nameInput) return;
            nameInput.onEndEdit.RemoveAllListeners();
            if (isMe)
            {
                nameInput.onEndEdit.AddListener(onValueChange);
            }

            nameInput.interactable = isMe;
        }


        // Để LobbyUI gọi khi ai đó Ready/Unready
        public void SetReadyVisual(bool isReady)
        {
            if (readyBadge) readyBadge.SetActive(isReady);
            if (notReadyFrame) notReadyFrame.SetActive(!isReady);
        }

        public void SetHidden(bool hidden) => gameObject.SetActive(!hidden);

        // ===== Helpers =====
        private static bool TryGetString(Unity.Services.Lobbies.Models.Player p, string key, out string value)
        {
            value = null;
            if (p?.Data == null) return false;
            if (!p.Data.TryGetValue(key, out var obj) || obj == null) return false;
            value = obj.Value;
            return true;
        }

        private static bool TryGetBool(Unity.Services.Lobbies.Models.Player p, string key, out bool value)
        {
            value = false;
            if (!TryGetString(p, key, out var s)) return false;
            return bool.TryParse(s, out value);
        }
    }
}
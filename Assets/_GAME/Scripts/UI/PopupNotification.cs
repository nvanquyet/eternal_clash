using GAME.Scripts.DesignPattern;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.UI
{
    public class PopupNotification : SingletonDontDestroy<PopupNotification>
    {
        [Header("UI References")]
        [SerializeField] private GameObject popupPanel;
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Sprite successIcon;
        [SerializeField] private Sprite errorIcon;

        [SerializeField] private Button closeButton;
        
        protected override void OnAwake()
        {
            base.OnAwake();
            closeButton?.onClick.AddListener(HidePopup);
            popupPanel.SetActive(false);
        }

        public void ShowPopup(bool isSuccess, string message, string title = "")
        {
            popupPanel.SetActive(true);

            iconImage.sprite = isSuccess ? successIcon : errorIcon;

            titleText.text = string.IsNullOrEmpty(title) ? (isSuccess ? "Successful" : "Error") : title;
            messageText.text = message;

            CancelInvoke(nameof(HidePopup));
            Invoke(nameof(HidePopup), 3f);
        }

        public void HidePopup()
        {
            popupPanel.SetActive(false);
        }
        
    }
}
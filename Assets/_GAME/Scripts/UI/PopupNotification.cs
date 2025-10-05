using GAME.Scripts.DesignPattern;
using Michsky.MUIP;
using UnityEngine;

namespace _GAME.Scripts.UI
{
    public class PopupNotification : SingletonDontDestroy<PopupNotification>
    {
        [Header("UI References")]
        [SerializeField] private NotificationManager notification; 
        
#if UNITY_EDITOR
        private void OnValidate()
        {
            notification = GetComponentInChildren<NotificationManager>();
        }
#endif

        private void Start()
        {
            notification.Close();
        }


        public void ShowPopup(bool isSuccess, string message, string title = "")
        {
            notification.title = string.IsNullOrEmpty(title) ? "Notification" : title; // Change title
            notification.description = message; 
            notification.UpdateUI(); 
            notification.Open();
            Debug.Log($"[PopupNotification] ShowPopup: isSuccess={isSuccess}, title={title}, message={message}");
            CancelInvoke(nameof(HidePopup));
            Invoke(nameof(HidePopup), 3f);
        }

        public void HidePopup()
        {
            notification.Close(); 
        }
        
    }
}
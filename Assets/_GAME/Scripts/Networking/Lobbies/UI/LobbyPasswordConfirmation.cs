using _GAME.Scripts.UI.Base;
using Michsky.MUIP;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.Lobbies.UI
{
    public class LobbyPasswordConfirmation : BasePopUp
    {
        [SerializeField] private TMP_InputField passwordInputField;
        [SerializeField] private ButtonManager  confirmButton;
        [SerializeField] private ButtonManager  cancelButton;
        public void Initialized(System.Action<string> onConfirm)
        {
            gameObject.SetActive(true);
            passwordInputField.text = string.Empty;
            confirmButton.onClick.AddListener(() => 
            {
                if (!string.IsNullOrEmpty(passwordInputField.text))
                {
                    onConfirm?.Invoke(passwordInputField.text);
                }
            });

            cancelButton.onClick.AddListener(() => 
            {
                passwordInputField.text = string.Empty;
                gameObject.SetActive(false);
            });
        }
    }
}

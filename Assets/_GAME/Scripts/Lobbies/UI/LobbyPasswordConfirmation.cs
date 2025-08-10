using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.Lobbies.UI
{
    public class LobbyPasswordConfirmation : MonoBehaviour
    {
        [SerializeField] private TMP_InputField passwordInputField;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Toggle showPasswordToggle;

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
            
            showPasswordToggle.onValueChanged.AddListener(isOn => 
            {
                passwordInputField.contentType = isOn ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
                passwordInputField.ForceLabelUpdate();
            });
        }
    }
}

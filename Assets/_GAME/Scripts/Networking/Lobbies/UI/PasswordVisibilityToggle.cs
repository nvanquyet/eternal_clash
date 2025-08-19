using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.Lobbies.UI
{
    public class PasswordVisibilityToggle : MonoBehaviour
    {
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private Toggle showToggle;

        private TMP_InputField.ContentType _origContentType;

        private void Awake()
        {
            _origContentType = passwordInput.contentType;
            showToggle.onValueChanged.AddListener(OnToggle);
            Apply(showToggle.isOn);
        }

        private void OnDestroy()
        {
            showToggle.onValueChanged.RemoveListener(OnToggle);
        }

        private void OnToggle(bool isOn) => Apply(isOn);

        private void Apply(bool show)
        {
            passwordInput.contentType = show ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;
            passwordInput.ForceLabelUpdate();
        }
    }
}
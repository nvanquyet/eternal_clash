using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;
using _GAME.Scripts.UI;

namespace _GAME.Scripts.Authenticator
{
    public class AuthUIManager : MonoBehaviour
    {
        [Header("Input Fields")] 
        public TMP_InputField emailInput;
        public TMP_InputField usernameOrEmailInput;
        public TMP_InputField passwordInput;
        public TMP_InputField confirmPasswordInput;
        
        [Header("Panel Fields")] 
        public GameObject emailFieldPanel;
        public GameObject confirmPasswordFieldPanel;
        public ScrollRect scrollRect;
        
        [Header("Text Fields")] 
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI switchLabelText;
        public TextMeshProUGUI mainButtonText;
        public TextMeshProUGUI switchButtonText;
        
        [Header("Button Fields")]
        public Button mainButton;
        public Button forgotPasswordButton;
        public Button switchButton;
        
        [Header("Toggles")]
        public Toggle showPasswordToggle;
        public Toggle showConfirmPasswordToggle;

        private bool isRegister = true;
        
        Action<string, string> OnLoginRequested = null;
        Action<string, string, string, string> OnRegisterRequested = null;
        
        public void Initialized(Action<string, string> OnLoginRequested,
            Action<string, string, string, string> OnRegisterRequested,
            Action<string> OnForgotPasswordRequested)
        {
            this.OnLoginRequested = OnLoginRequested;
            this.OnRegisterRequested = OnRegisterRequested;
            
            forgotPasswordButton.onClick.AddListener(() =>
                OnForgotPasswordRequested?.Invoke(usernameOrEmailInput.text.Trim()));
            
            switchButton.onClick.AddListener(SwitchPanel);
            
            SetUpToggles();
            SwitchPanel();
        }

        private void SetUpToggles()
        {
            showPasswordToggle?.onValueChanged.AddListener(isOn =>
            {
                passwordInput.contentType = isOn ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
                passwordInput.ForceLabelUpdate();
            });
            showConfirmPasswordToggle?.onValueChanged.AddListener(isOn =>
            {
                confirmPasswordInput.contentType = isOn ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
                confirmPasswordInput.ForceLabelUpdate();
            });
        }

        private void SwitchPanel()
        {
            isRegister = !isRegister;
            if (isRegister)
            {
                mainButtonText.text = "Register";
                titleText.text = "Register";
                switchButtonText.text = "Login";
                switchLabelText.text = "Already have an account?";
                
                emailFieldPanel.gameObject.SetActive(true);
                confirmPasswordFieldPanel.gameObject.SetActive(true);
                
                mainButton.onClick.RemoveAllListeners();
                mainButton.onClick.AddListener(() =>
                {
                    if (string.IsNullOrEmpty(usernameOrEmailInput.text) || 
                        string.IsNullOrEmpty(passwordInput.text) || 
                        string.IsNullOrEmpty(confirmPasswordInput.text) || 
                        string.IsNullOrEmpty(emailInput.text))
                    {
                        PopupNotification.Instance.ShowPopup(false, "Please fill all fields.");
                        return;
                    }
                    
                    if (passwordInput.text != confirmPasswordInput.text)
                    {
                        PopupNotification.Instance.ShowPopup(false, "Passwords do not match.");
                        return;
                    }
                    
                    OnRegisterRequested?.Invoke(usernameOrEmailInput.text.Trim(), emailInput.text.Trim(), passwordInput.text.Trim(), confirmPasswordInput.text.Trim());
                });
            }
            else
            {
                mainButtonText.text = "Login";
                titleText.text = "Login";
                switchButtonText.text = "Register";
                switchLabelText.text = "Don't have an account?";
                
                emailFieldPanel.gameObject.SetActive(false);
                confirmPasswordFieldPanel.gameObject.SetActive(false);
                
                mainButton.onClick.RemoveAllListeners();
                mainButton.onClick.AddListener(() =>
                {
                    if (string.IsNullOrEmpty(usernameOrEmailInput.text) || string.IsNullOrEmpty(passwordInput.text))
                    {
                        PopupNotification.Instance.ShowPopup(false, "Please fill all fields.");
                        return;
                    }
                    
                    OnLoginRequested?.Invoke(usernameOrEmailInput.text.Trim(), passwordInput.text.Trim());
                });
            }
            
            // Reset input fields
            passwordInput.text = string.Empty;
            confirmPasswordInput.text = string.Empty;
            emailInput.text = string.Empty;
            scrollRect.verticalNormalizedPosition = 1f; // Reset scroll position to top
        }
    }
}
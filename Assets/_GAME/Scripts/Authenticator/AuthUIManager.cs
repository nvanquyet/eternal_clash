using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

namespace _GAME.Scripts.Authenticator
{
    public class AuthUIManager : MonoBehaviour
    {
        [Header("Panels")] public GameObject loginPanel;
        public GameObject registerPanel;

        [Header("Login Fields")] public TMP_InputField loginUsernameOrEmailInput;
        public TMP_InputField loginPasswordInput;
        public Button loginButton;
        public Button forgotPasswordButton;
        public Button goToRegisterButton;

        [Header("Register Fields")] public TMP_InputField registerUsernameInput;
        public TMP_InputField registerEmailInput;
        public TMP_InputField registerPasswordInput;
        public TMP_InputField registerConfirmPasswordInput;
        public Button registerButton;
        public Button goToLoginButton;


        public void Initialized(Action<string, string> OnLoginRequested,
            Action<string, string, string, string> OnRegisterRequested,
            Action<string> OnForgotPasswordRequested)
        {
            loginButton.onClick.AddListener(() => OnLoginRequested?.Invoke(
                loginUsernameOrEmailInput.text.Trim(), 
                loginPasswordInput.text.Trim()
            ));
            
            forgotPasswordButton.onClick.AddListener(() =>
                OnForgotPasswordRequested?.Invoke(loginUsernameOrEmailInput.text.Trim()));
            goToRegisterButton.onClick.AddListener(() => SwitchPanel(false));

            registerButton.onClick.AddListener(() => OnRegisterRequested?.Invoke(
                registerUsernameInput.text.Trim(),
                registerEmailInput.text.Trim(),
                registerPasswordInput.text.Trim(),
                registerConfirmPasswordInput.text.Trim()
            ));
            goToLoginButton.onClick.AddListener(() => SwitchPanel(true));
            SwitchPanel(true);
        }

        private void SwitchPanel(bool showLogin)
        {
            loginPanel.SetActive(showLogin);
            registerPanel.SetActive(!showLogin);
        }
    }
}
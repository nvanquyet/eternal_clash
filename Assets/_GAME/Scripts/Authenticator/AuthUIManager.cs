using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;
using _GAME.Scripts.Data;
using _GAME.Scripts.UI;
using Michsky.MUIP;

namespace _GAME.Scripts.Authenticator
{
    public class AuthUIManager : MonoBehaviour
    {
        [Header("Login Input Fields")] 
        public TMP_InputField usernameOrEmailInput;
        public TMP_InputField passwordInput;

        [Header("Register Input Fields")]
        public TMP_InputField emailRegisterInput;
        public TMP_InputField usernameRegisterInput;
        public TMP_InputField passwordRegisterInput;
        public TMP_InputField confirmRegisterPasswordInput;
        [Header("Forgot Password Fields")]
        public TMP_InputField emailForgotInput;
        
        [Header("Button Fields")]
        public Button loginButton;
        public Button registerButton;
        public Button forgotPasswordButton;
        
        [Header("Switch Fields")]
        [SerializeField] private SwitchManager stayLoggedInSwitch;
        [SerializeField] private SwitchManager termsSwitch;
        
        
        Action<string, string> OnLoginRequested = null;
        Action<string, string, string, string> OnRegisterRequested = null;
        private Action<string> OnForgotPasswordRequested = null;
        
        public void Initialized(Action<string, string> OnLoginRequested,
            Action<string, string, string, string> OnRegisterRequested,
            Action<string> OnForgotPasswordRequested)
        {
            this.OnLoginRequested = OnLoginRequested;
            this.OnRegisterRequested = OnRegisterRequested;
            this.OnForgotPasswordRequested = OnForgotPasswordRequested;
            loginButton.onClick.AddListener(OnClickLogin);
            registerButton.onClick.AddListener(OnClickRegister);
            forgotPasswordButton.onClick.AddListener(OnClickResetPassword);
            
        }

      
        private void OnDestroy()
        {
            loginButton.onClick.RemoveListener(OnClickLogin);
            registerButton.onClick.RemoveListener(OnClickRegister);
            forgotPasswordButton.onClick.RemoveListener(OnClickResetPassword);

        }

        private void OnClickRegister()
        {
            //Validate inputs
            if (string.IsNullOrEmpty(emailRegisterInput.text) ||
                string.IsNullOrEmpty(usernameRegisterInput.text) ||
                string.IsNullOrEmpty(passwordRegisterInput.text) ||
                string.IsNullOrEmpty(confirmRegisterPasswordInput.text))
            {
                PopupNotification.Instance.ShowPopup(false, "Please fill in all fields.");
                return;
            }
            if (!termsSwitch.isOn)
            {
                PopupNotification.Instance.ShowPopup(false, "Please agree to the terms and conditions.");
                return;
            }
            if (passwordRegisterInput.text != confirmRegisterPasswordInput.text)
            {
                PopupNotification.Instance.ShowPopup(false, "Confirm password does not match.");
                return;
            }
            OnRegisterRequested?.Invoke(usernameRegisterInput.text, emailRegisterInput.text,
                passwordRegisterInput.text, confirmRegisterPasswordInput.text);
        }

        private void OnClickLogin()
        {
            //Validate inputs
            if (string.IsNullOrEmpty(usernameOrEmailInput.text) ||
                string.IsNullOrEmpty(passwordInput.text))
            {
                PopupNotification.Instance.ShowPopup(false, "Please fill in all fields.");
                return;
            }
            //Check if remeber me
            LocalData.StayLoggedIn = stayLoggedInSwitch.isOn;
            OnLoginRequested?.Invoke(usernameOrEmailInput.text, passwordInput.text);
        }
        
        private void OnClickResetPassword()
        {
           //Validate inputs
            if (string.IsNullOrEmpty(emailForgotInput.text))
            {
                PopupNotification.Instance.ShowPopup(false, "Please fill in all fields.");
                return;
            }
            OnForgotPasswordRequested?.Invoke(emailForgotInput.text);
        }

    }
}
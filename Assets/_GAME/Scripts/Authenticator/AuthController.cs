using System;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Data;
using _GAME.Scripts.UI;
using UnityEngine;

namespace _GAME.Scripts.Authenticator
{
    public class AuthController : MonoBehaviour
    {
        [SerializeField] private AuthUIManager uiManager;
        private IAuthManager<PlayFabAuthManager> authManager;
    
        private void Awake()
        {
            authManager = PlayFabAuthManager.Instance;
            uiManager.Initialized(async (user, pass) =>
            {
                LoadingUI.Instance.RunTimed(1, () =>
                {
                    Debug.Log($"[AuthController] Attempting login for user: {user}");
                }, "Please wait...");
                var (success, message) = await authManager.LoginAsync(user, pass);
                //Show popup notification
                if (success)
                {
                    LocalData.UserName = user;
                    LocalData.UserId = authManager.UserId;
                    LocalData.UserPassword = pass;
                    
                    //Change the scene or perform any other action after successful login
                    SceneController.Instance.LoadSceneAsync((int) SceneDefinitions.Home);
                }
                PopupNotification.Instance.ShowPopup(success, message);
            }, async (username, email, pass, confirm) =>
            {
                LoadingUI.Instance.RunTimed(1, () =>
                {
                    Debug.Log($"[AuthController] Attempting Register for user");
                }, "Please wait...");
                var (success, message) = await authManager.RegisterAsync(username, email, pass, confirm);
                //Show popup notification
                LoadingUI.Instance.Complete(() =>
                {
                    Debug.Log($"[AuthController] Done");
                    PopupNotification.Instance.ShowPopup(success, message);
                });
            }, async email =>
            {
                LoadingUI.Instance.RunTimed(1, () =>
                {
                    Debug.Log($"[AuthController] Attempting forgot pass for user");
                }, "Please wait...");
                var (success, message) = await authManager.ForgotPasswordAsync(email);
                //Show popup notification
                LoadingUI.Instance.Complete(() =>
                {
                    Debug.Log($"[AuthController] Done");
                    PopupNotification.Instance.ShowPopup(success, message);
                });
            });
            
            RegisterEvent();
        }

        private void Start()
        {
            //Hide loading UI if any
            LoadingUI.Instance.Complete();
        }

        private void OnDestroy()
        {
            UnRegisterEvent();
        }
        
        private void RegisterEvent()
        {
            if (authManager is PlayFabAuthManager pfManager)
            {
                pfManager.OnSecurityAlert += OnSecurityAlert;
            }
        }

        private void OnSecurityAlert(SecurityAlertInfo alertInfo)
        {
            var alertMessage = $"Cảnh báo bảo mật: Có người cố đăng nhập vào tài khoản của bạn từ {alertInfo.AttemptDeviceInfo} lúc {alertInfo.AttemptTime:HH:mm:ss}";
            PopupNotification.Instance.ShowPopup(false, alertMessage);
        }
        
        
        private void UnRegisterEvent()
        {
            if (authManager is PlayFabAuthManager pfManager)
            {
                pfManager.OnSecurityAlert -= OnSecurityAlert;
            }
        }
    }
}
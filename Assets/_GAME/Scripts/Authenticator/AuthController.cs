using System;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Data;
using _GAME.Scripts.Networking;
using _GAME.Scripts.UI;
using UnityEngine;

namespace _GAME.Scripts.Authenticator
{
    public class AuthController : MonoBehaviour
    {
        [SerializeField] private AuthUIManager uiManager;

        private IAuthManager AuthManager => GameNet.Instance.Auth;
        
        private bool isProcessingAuth = false; // prevent double clicks

        private void Awake()
        {
            uiManager.Initialized(
                OnLoginRequested,
                OnRegisterRequested,
                OnForgotPasswordRequested
            );

            RegisterEvent();
        }

        private void Start()
        {
            //Hide loading UI if any
            LoadingUI.Instance.Complete();
            
            //PlayMusic
            AudioManager.Instance.PlayMenuMusic();
        }

        private void OnDestroy()
        {
            UnRegisterEvent();
        }

        private async void OnLoginRequested(string user, string pass)
        {
            if (isProcessingAuth)
            {
                PopupNotification.Instance.ShowPopup(false, "Processing request, please wait...");
                return;
            }

            isProcessingAuth = true;

            try
            {
                Debug.Log($"[AuthController] Attempting login for user: {user}");
                
                // Show loading với timeout phù hợp với loginProcessTimeoutSeconds
                LoadingUI.Instance.RunTimed(35, () => {
                    Debug.Log($"[AuthController] Login process completed");
                }, "Login processing ...", false);

                var (success, message) = await AuthManager.LoginAsync(user, pass);
                
                Debug.Log($"[AuthController] Login result - Success: {success}, Message: {message}");

                if (success)
                {
                    // Lưu thông tin user
                    LocalData.UserName = user;
                    LocalData.UserId = AuthManager.UserId;
                    LocalData.UserPassword = pass;

                    // Show success message trước khi chuyển scene
                    PopupNotification.Instance.ShowPopup(true, message);
                    
                    // Delay nhỏ trước khi chuyển scene để user thấy thông báo
                    await System.Threading.Tasks.Task.Delay(1500);
                    
                    //Change the scene
                    SceneController.Instance.LoadSceneAsync((int)SceneDefinitions.Home);
                }
                else
                {
                    LoadingUI.Instance.Complete();
                    PopupNotification.Instance.ShowPopup(false, message);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthController] Login exception: {ex.Message}");
                LoadingUI.Instance.Complete();
                PopupNotification.Instance.ShowPopup(false, "Has occurred an unexpected error. Please try again.");
            }
            finally
            {
                isProcessingAuth = false;
            }
        }

        private async void OnRegisterRequested(string username, string email, string pass, string confirm)
        {
            if (isProcessingAuth)
            {
                PopupNotification.Instance.ShowPopup(false, "Processing request, please wait...");
                return;
            }

            isProcessingAuth = true;

            try
            {
                Debug.Log($"[AuthController] Attempting Register for user: {username}");
                
                LoadingUI.Instance.RunTimed(15, () => {
                    Debug.Log($"[AuthController] Register process completed");
                }, "Register processing ...", false);

                var (success, message) = await AuthManager.RegisterAsync(username, email, pass, confirm);
                
                Debug.Log($"[AuthController] Register result - Success: {success}, Message: {message}");
                PopupNotification.Instance.ShowPopup(success, message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthController] Register exception: {ex.Message}");
                PopupNotification.Instance.ShowPopup(false, "Has occurred an unexpected error. Please try again.");
            }
            finally
            {
                LoadingUI.Instance.Complete();
                isProcessingAuth = false;
            }
        }

        private async void OnForgotPasswordRequested(string email)
        {
            if (isProcessingAuth)
            {
                PopupNotification.Instance.ShowPopup(false, "Processing request, please wait...");
                return;
            }

            isProcessingAuth = true;

            try
            {
                Debug.Log($"[AuthController] Attempting forgot password for email: {email}");
                
                LoadingUI.Instance.RunTimed(10, () => {
                    Debug.Log($"[AuthController] Forgot password process completed");
                }, "Forgot password processing ...", false);

                var (success, message) = await AuthManager.ForgotPasswordAsync(email);
                
                Debug.Log($"[AuthController] Forgot password result - Success: {success}, Message: {message}");
                PopupNotification.Instance.ShowPopup(success, message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthController] Forgot password exception: {ex.Message}");
                PopupNotification.Instance.ShowPopup(false, "Has occurred an unexpected error. Please try again.");
            }
            finally
            {
                LoadingUI.Instance.Complete();
                isProcessingAuth = false;
            }
        }

        private void RegisterEvent()
        {
            if (AuthManager is PlayFabAuthManager pfManager)
            {
                pfManager.OnSecurityAlert += OnSecurityAlert;
                pfManager.OnSessionExpired += OnSessionExpired;
                pfManager.OnLoginSuccess += OnLoginSuccess;
                pfManager.OnLoginFailed += OnLoginFailed;
            }
        }

        private void OnSecurityAlert(SecurityAlertInfo alertInfo)
        {
            var alertMessage = $"⚠️ Cảnh báo bảo mật!\n" +
                              $"Có người cố đăng nhập vào tài khoản từ:\n" +
                              $"Thiết bị: {alertInfo.AttemptDeviceInfo}\n" +
                              $"Thời gian: {alertInfo.AttemptTime:dd/MM/yyyy HH:mm:ss}\n" +
                              $"Vị trí: {alertInfo.AttemptLocation}";
            
            PopupNotification.Instance.ShowPopup(false, alertMessage);
            Debug.LogWarning($"[AuthController] Security Alert: {alertMessage}");
        }

        private void OnSessionExpired()
        {
            Debug.LogWarning("[AuthController] Session expired - returning to auth scene");
            
            // Clear local data
            LocalData.UserName = string.Empty;
            LocalData.UserId = string.Empty;
            LocalData.UserPassword = string.Empty;
            
            PopupNotification.Instance.ShowPopup(false, "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
            
            // Return to auth scene
            SceneController.Instance.LoadSceneAsync((int)SceneDefinitions.Login);
        }

        private void OnLoginSuccess(string message)
        {
            Debug.Log($"[AuthController] Login Success Event: {message}");
        }

        private void OnLoginFailed(string message)
        {
            Debug.Log($"[AuthController] Login Failed Event: {message}");
        }

        private void UnRegisterEvent()
        {
            if (AuthManager is PlayFabAuthManager pfManager)
            {
                pfManager.OnSecurityAlert -= OnSecurityAlert;
                pfManager.OnSessionExpired -= OnSessionExpired;
                pfManager.OnLoginSuccess -= OnLoginSuccess;
                pfManager.OnLoginFailed -= OnLoginFailed;
            }
        }
    }
}
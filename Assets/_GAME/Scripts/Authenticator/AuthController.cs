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
                var (success, message) = await authManager.RegisterAsync(username, email, pass, confirm);
                //Show popup notification
                PopupNotification.Instance.ShowPopup(success, message);
            }, async email =>
            {
                var (success, message) = await authManager.ForgotPasswordAsync(email);
                //Show popup notification
                PopupNotification.Instance.ShowPopup(success, message);
            });
        }
    }
}
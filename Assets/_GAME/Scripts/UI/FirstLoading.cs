using System;
using System.Threading.Tasks;
using _GAME.Scripts.Authenticator;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Data;
using UnityEngine;

namespace _GAME.Scripts.UI
{
    public class FirstLoading : MonoBehaviour
    {      
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            //Show Loading UI
            LoadingUI.Instance.RunTimed(5f, () =>
            {
                //After loading time is over, initialize services and personal information
                Debug.Log("[FirstCtrl] Fake loading done!");
            });
            //Initialize services, settings, etc.
            //InitializeServices();

            InitializePersonalInformation();
            //Load main menu scene or next scene
            Debug.Log("[FirstLoading] First loading started...");
        }

        private async void InitializePersonalInformation()
        {
            try
            {
                //You can check login if successful go to main scene
                //If not, show login UI
                Debug.Log("[FirstLoading] Personal information initialized.");
                // Example: Check if user is logged in
                if (await UserIsLoggedIn())
                {
                    SceneController.Instance.LoadSceneAsync((int)SceneDefinitions.Home);
                }
                else
                {
                    SceneController.Instance.LoadSceneAsync((int)SceneDefinitions.Login, () =>
                    {
                        PopupNotification.Instance.ShowPopup(false, "Please login to continue.", "Login Required");
                    });
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        private async Task<bool> UserIsLoggedIn()
        {
            var authManager = PlayFabAuthManager.Instance;
            var isLogin = await authManager.LoginAsync(LocalData.UserName, LocalData.UserPassword);
            return isLogin.success;
        }

        private void InitializeServices()
        {
            // Here you can initialize your services, settings, etc.
            // For example, initializing PlayFab, Unity Services, etc.
            Debug.Log("Services initialized.");
        }

        
    }
}

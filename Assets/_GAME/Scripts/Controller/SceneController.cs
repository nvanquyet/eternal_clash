using System;
using System.Collections;
using GAME.Scripts.DesignPattern;
using UnityEngine;

namespace _GAME.Scripts.Controller
{
    public enum SceneDefinitions
    {
        FirstLoadingScene = 0,
        Login = 1,
        Home = 2,
        WaitingRoom = 3,
        GamePlay = 4
    }
    
    public static class SceneHelper
    {
        public static string ToSceneName(SceneDefinitions def)
        {
            return def switch
            {
                SceneDefinitions.Home        => "HomeScene",
                SceneDefinitions.WaitingRoom => "WaitingScene",
                SceneDefinitions.GamePlay    => "Gameplay",
                _ => throw new ArgumentOutOfRangeException(nameof(def), def, null)
            };
        }
    }
    
    public class SceneController : SingletonDontDestroy<SceneController>
    {
        //Loading scene asynchronously with callback
        public void LoadSceneAsync(string sceneName, System.Action onSuccessful = null,  System.Action onFailed = null)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("Scene name is null or empty.");
                onFailed?.Invoke();
                return;
            }
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == sceneName)
            {
                Debug.LogWarning($"Scene '{sceneName}' is already loaded.");
                onSuccessful?.Invoke();
                return;
            }
            if (UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName).isLoaded)
            {
                Debug.LogWarning($"Scene '{sceneName}' is already loaded.");
                onSuccessful?.Invoke();
                return;
            }
            Debug.Log($"Loading scene '{sceneName}' asynchronously...");
            //Get build index of the scene
            var sceneIndex = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(sceneName);
            StartCoroutine(LoadSceneCoroutine(sceneIndex, onSuccessful, onFailed));
        }
        
        public void LoadSceneAsync(int sceneIndex, System.Action onSuccessful = null,  System.Action onFailed = null)
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == sceneIndex)
            {
                Debug.LogWarning($"Scene '{sceneIndex}' is already loaded.");
                onSuccessful?.Invoke();
                return;
            }
            if (UnityEngine.SceneManagement.SceneManager.GetSceneByBuildIndex(sceneIndex).isLoaded)
            {
                Debug.LogWarning($"Scene '{sceneIndex}' is already loaded.");
                onSuccessful?.Invoke();
                return;
            }
            Debug.Log($"[SceneCtrl] Loading scene '{sceneIndex}' asynchronously...");
            //Check if the scene index is valid
            StartCoroutine(LoadSceneCoroutine(sceneIndex, onSuccessful, onFailed));
        }

        private IEnumerator LoadSceneCoroutine(int sceneIndex, Action onSuccessful, Action onFailed)
        {
            var asyncOperation = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneIndex);
            if (asyncOperation == null)
            {
                Debug.LogError($"Failed to load scene with index {sceneIndex}.");
                onFailed?.Invoke();
                yield break;
            }
            
            asyncOperation.allowSceneActivation = false;

            while (!asyncOperation.isDone)
            {
                // Optionally, you can log the progress
                Debug.Log($"[SceneCtrl] Loading progress: {asyncOperation.progress * 100}%");
                
                // Check if the loading is complete
                if (asyncOperation.progress >= 0.9f)
                {
                    asyncOperation.allowSceneActivation = true;
                    break;
                }
                
                yield return null; // Wait for the next frame
            }

            Debug.Log($"[SceneCtrl] Scene '{UnityEngine.SceneManagement.SceneManager.GetSceneByBuildIndex(sceneIndex).name}' loaded successfully.");
            onSuccessful?.Invoke();
        }
    }
}

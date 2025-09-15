using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Networking.UI
{
    public class NetworkLoadingManager : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool showLoadingOnSceneChange = true;
        [SerializeField] private float minLoadingTime = 1f;
        [SerializeField] private string[] loadingTips = new string[]
        {
            "Synchronizing with other players...",
            "Loading new environment...",
            "Preparing multiplayer session...",
            "Almost ready to play!",
            "Setting up game world..."
        };

        private NetworkVariable<bool> isLoading = new NetworkVariable<bool>(false, 
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        
        private NetworkVariable<float> loadingProgress = new NetworkVariable<float>(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private bool _isSubscribedToSceneEvents = false;
        private Coroutine _loadingCoroutine;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            // Subscribe to loading state changes
            isLoading.OnValueChanged += OnLoadingStateChanged;
            loadingProgress.OnValueChanged += OnLoadingProgressChanged;
            
            // Subscribe to scene events (only once)
            if (!_isSubscribedToSceneEvents)
            {
                SubscribeToSceneEvents();
                _isSubscribedToSceneEvents = true;
            }
        }

        public override void OnNetworkDespawn()
        {
            // Unsubscribe from loading state changes
            isLoading.OnValueChanged -= OnLoadingStateChanged;
            loadingProgress.OnValueChanged -= OnLoadingProgressChanged;
            
            base.OnNetworkDespawn();
        }

        private void OnDestroy()
        {
            UnsubscribeFromSceneEvents();
        }

        private void SubscribeToSceneEvents()
        {
            if (NetworkManager.Singleton?.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
                Debug.Log("[NetworkLoadingManager] Subscribed to scene events");
            }
        }

        private void UnsubscribeFromSceneEvents()
        {
            if (NetworkManager.Singleton?.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
                Debug.Log("[NetworkLoadingManager] Unsubscribed from scene events");
            }
        }

        private void OnSceneEvent(SceneEvent sceneEvent)
        {
            if (!showLoadingOnSceneChange) return;

            Debug.Log($"[NetworkLoadingManager] Scene Event: {sceneEvent.SceneEventType} for client {sceneEvent.ClientId}");

            switch (sceneEvent.SceneEventType)
            {
                case SceneEventType.LoadEventCompleted:
                    HandleSceneLoadStart(sceneEvent);
                    break;
                    
                case SceneEventType.LoadComplete:
                    HandleSceneLoadComplete(sceneEvent);
                    break;
                    
                case SceneEventType.UnloadEventCompleted:
                    HandleSceneUnloadStart(sceneEvent);
                    break;
                    
                case SceneEventType.UnloadComplete:
                    HandleSceneUnloadComplete(sceneEvent);
                    break;
            }
        }

        private void HandleSceneLoadStart(SceneEvent sceneEvent)
        {
            // Chỉ server mới set loading state
            if (IsServer)
            {
                Debug.Log($"[NetworkLoadingManager] Starting scene load: {sceneEvent.SceneName}");
                SetLoadingStateServerRpc(true, 0f, GetRandomLoadingTip());
            }
        }

        private void HandleSceneLoadComplete(SceneEvent sceneEvent)
        {
            if (IsServer)
            {
                Debug.Log($"[NetworkLoadingManager] Scene load completed: {sceneEvent.SceneName}");
                
                // Đảm bảo loading hiển thị đủ lâu
                if (_loadingCoroutine != null)
                    StopCoroutine(_loadingCoroutine);
                    
                _loadingCoroutine = StartCoroutine(CompleteLoadingAfterDelay());
            }
        }

        private void HandleSceneUnloadStart(SceneEvent sceneEvent)
        {
            if (IsServer)
            {
                Debug.Log($"[NetworkLoadingManager] Starting scene unload: {sceneEvent.SceneName}");
                SetLoadingStateServerRpc(true, 0f, "Leaving current area...");
            }
        }

        private void HandleSceneUnloadComplete(SceneEvent sceneEvent)
        {
            if (IsServer)
            {
                Debug.Log($"[NetworkLoadingManager] Scene unload completed: {sceneEvent.SceneName}");
                // Unload thường được theo sau bởi load, nên không tắt loading ngay
            }
        }

        private IEnumerator CompleteLoadingAfterDelay()
        {
            // Simulate progress to 100%
            for (float progress = 0.5f; progress <= 1f; progress += 0.1f)
            {
                loadingProgress.Value = progress;
                yield return new WaitForSeconds(0.1f);
            }
            
            // Đợi thêm một chút để đảm bảo smooth
            yield return new WaitForSeconds(minLoadingTime);
            
            // Tắt loading
            SetLoadingStateServerRpc(false, 1f, "");
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetLoadingStateServerRpc(bool loading, float progress, string tip)
        {
            isLoading.Value = loading;
            loadingProgress.Value = progress;
            
            // Gửi tip đến all clients
            if (!string.IsNullOrEmpty(tip))
            {
                UpdateLoadingTipClientRpc(tip);
            }
        }

        [ClientRpc]
        private void UpdateLoadingTipClientRpc(string tip)
        {
            if (_GAME.Scripts.UI.LoadingUI.Instance != null)
            {
                // Cập nhật tip text trực tiếp
                _GAME.Scripts.UI.LoadingUI.Instance.SetTipText(tip);
            }
        }

        private void OnLoadingStateChanged(bool oldValue, bool newValue)
        {
            Debug.Log($"[NetworkLoadingManager] Loading state changed: {oldValue} -> {newValue}");
            
            if (_GAME.Scripts.UI.LoadingUI.Instance == null) return;

            if (newValue) // Show loading
            {
                _GAME.Scripts.UI.LoadingUI.Instance.ShowNetworkLoading(GetRandomLoadingTip());
            }
            else // Hide loading
            {
                _GAME.Scripts.UI.LoadingUI.Instance.HideNetworkLoading();
            }
        }

        private void OnLoadingProgressChanged(float oldValue, float newValue)
        {
            if (_GAME.Scripts.UI.LoadingUI.Instance != null && isLoading.Value)
            {
                _GAME.Scripts.UI.LoadingUI.Instance.UpdateNetworkProgress(newValue);
            }
        }

        private string GetRandomLoadingTip()
        {
            if (loadingTips == null || loadingTips.Length == 0) 
                return "Loading...";
                
            return loadingTips[UnityEngine.Random.Range(0, loadingTips.Length)];
        }

        // Public methods for manual control
        [ServerRpc(RequireOwnership = false)]
        public void ShowLoadingServerRpc(string tip = null)
        {
            SetLoadingStateServerRpc(true, 0f, tip ?? GetRandomLoadingTip());
        }

        [ServerRpc(RequireOwnership = false)]
        public void HideLoadingServerRpc()
        {
            SetLoadingStateServerRpc(false, 1f, "");
        }

        [ServerRpc(RequireOwnership = false)]
        public void UpdateProgressServerRpc(float progress, string tip = null)
        {
            loadingProgress.Value = Mathf.Clamp01(progress);
            if (!string.IsNullOrEmpty(tip))
                UpdateLoadingTipClientRpc(tip);
        }
    }
}
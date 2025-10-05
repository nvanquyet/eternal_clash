using System;
using _GAME.Scripts.Networking.UI;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Networking
{
    /// Server lắng scene events và phát ClientRpc → gọi UI local trên mỗi client
    [DisallowMultipleComponent]
    public class SceneLoadingBroadcaster : MonoBehaviour
    {
        public static SceneLoadingBroadcaster Instance { get; private set; }

        [SerializeField] private bool autoSpawnOnServerStart = true;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnServerStarted += OnServerStarted;
                nm.OnServerStopped += OnServerStopped;
            }
        }

        private void OnDisable()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnServerStarted -= OnServerStarted;
                nm.OnServerStopped  -= OnServerStopped;
            }
            UnsubscribeServerSceneEvents();
        }

        // ========= Server lifecycle =========
        private void OnServerStarted()
        {
            // bảo đảm có NetworkObject và đã Spawn để RPC hoạt động
            var no = GetComponent<NetworkObject>();
            if (no != null && !no.IsSpawned && autoSpawnOnServerStart)
            {
                try { no.Spawn(true); }
                catch (Exception e) { Debug.LogError($"[SLB] Spawn failed: {e.Message}"); }
            }
            SubscribeServerSceneEvents();
        }

        private void OnServerStopped(bool wasHost) => UnsubscribeServerSceneEvents();

        private void SubscribeServerSceneEvents()
        {
            if (!GameNet.Instance.Network.IsHost) return;
            var nsm = NetworkManager.Singleton?.SceneManager;
            if (nsm == null) { StartCoroutine(RetrySub()); return; }

            nsm.OnSceneEvent -= OnServerSceneEvent;
            nsm.OnSceneEvent += OnServerSceneEvent;
            Debug.Log("[SLB] Subscribed to OnSceneEvent");
        }

        private System.Collections.IEnumerator RetrySub() { yield return null; SubscribeServerSceneEvents(); }

        private void UnsubscribeServerSceneEvents()
        {
            if (!GameNet.Instance.Network.IsHost) return;
            var nsm = NetworkManager.Singleton?.SceneManager;
            if (nsm != null) nsm.OnSceneEvent -= OnServerSceneEvent;
        }

        // ========= Chuyển scene → phát RPC =========
        private void OnServerSceneEvent(SceneEvent e)
        {
            switch (e.SceneEventType)
            {
                case SceneEventType.Load:
                    ShowLoadingClientRpc($"Loading {e.SceneName}...", GetTip());
                    break;

                case SceneEventType.LoadComplete:
                    UpdateLoadingProgressClientRpc(0.8f, "Synchronizing...");
                    break;

                case SceneEventType.LoadEventCompleted:
                    CompleteLoadingClientRpc();
                    break;

                case SceneEventType.Unload:
                    ShowLoadingClientRpc("Leaving current area...", "Preparing to switch scenes...");
                    break;
            }
        }

        // ========= ClientRpc gọi UI local trên từng máy =========
        [ClientRpc]
        private void ShowLoadingClientRpc(string mainText, string tipText)
        {
            var ui = NetworkLoadingManager.Instance;
            if (ui == null) return;
            ui.ShowFromBroadcaster(mainText, tipText);
        }

        [ClientRpc]
        private void UpdateLoadingProgressClientRpc(float progress, string tipText = "")
        {
            var ui = NetworkLoadingManager.Instance;
            if (ui == null) return;
            ui.UpdateFromBroadcaster(progress, string.IsNullOrEmpty(tipText) ? null : tipText);
        }

        [ClientRpc]
        private void CompleteLoadingClientRpc()
        {
            var ui = NetworkLoadingManager.Instance;
            if (ui == null) return;
            ui.CompleteFromBroadcaster();
        }

        [ClientRpc]
        private void HideLoadingClientRpc()
        {
            var ui = NetworkLoadingManager.Instance;
            if (ui == null) return;
            ui.HideFromBroadcaster();
        }

        // API để host gọi “pre-show” trước khi LoadScene (tuỳ chọn)
        public void PreShowAllClients(string tip = "Loading...")
        {
            if (!GameNet.Instance.Network.IsHost) return;
            ShowLoadingClientRpc("Loading...", tip);
        }

        private static readonly string[] tips =
        {
            "Synchronizing with other players...",
            "Loading new environment...",
            "Preparing multiplayer session...",
            "Almost ready to play!",
            "Setting up game world..."
        };
        private string GetTip() => tips[UnityEngine.Random.Range(0, tips.Length)];
    }
}

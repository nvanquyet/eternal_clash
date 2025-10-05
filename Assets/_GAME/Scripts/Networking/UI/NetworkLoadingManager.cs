using System.Collections;
using GAME.Scripts.DesignPattern;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Networking.UI
{
    /// <summary>
    /// Pure UI manager for network loading overlay.
    /// - Not a NetworkBehaviour. No RPC here.
    /// - Shows/hides progress locally.
    /// - Receives calls from a networked broadcaster (SceneLoadingBroadcaster) via public methods.
    /// </summary>
    public class NetworkLoadingManager : SingletonDontDestroy<NetworkLoadingManager>
    {
        [Header("Settings")]
        [SerializeField] private float minLoadingTime = 1f;

        [SerializeField] private string[] loadingTips =
        {
            "Synchronizing with other players...",
            "Loading new environment...",
            "Preparing multiplayer session...",
            "Almost ready to play!",
            "Setting up game world..."
        };

        private bool _isCurrentlyLoading = false;
        private Coroutine _loadingCoroutine;
        private float _loadingProgress = 0f;

        protected override void OnAwake()
        {
            base.OnAwake();
            Debug.Log("[NetworkLoadingManager] Awake (UI-only, no RPC inside)");
        }

        protected override void OnDestroy()
        {
            if (_loadingCoroutine != null) StopCoroutine(_loadingCoroutine);
            base.OnDestroy();
        }

        // -------------------------
        // Entry points called BY broadcaster (runs on each client)
        // -------------------------

        /// <summary>
        /// Called by SceneLoadingBroadcaster (ClientRpc) to show overlay locally.
        /// </summary>
        public void ShowFromBroadcaster(string mainText, string tipText)
        {
            _isCurrentlyLoading = true;
            _loadingProgress = 0f;

            ShowLoadingUI(mainText, string.IsNullOrEmpty(tipText) ? GetRandomLoadingTip() : tipText);

            if (_loadingCoroutine != null) StopCoroutine(_loadingCoroutine);
            _loadingCoroutine = StartCoroutine(SimulateLoadingProgress());
        }

        /// <summary>
        /// Called by SceneLoadingBroadcaster (ClientRpc) to update progress locally.
        /// </summary>
        public void UpdateFromBroadcaster(float progress, string tipText = null)
        {
            _loadingProgress = Mathf.Max(_loadingProgress, progress);
            UpdateLoadingProgress(_loadingProgress, tipText);
        }

        /// <summary>
        /// Called by SceneLoadingBroadcaster (ClientRpc) to finish and hide overlay locally.
        /// </summary>
        public void CompleteFromBroadcaster()
        {
            if (_loadingCoroutine != null) StopCoroutine(_loadingCoroutine);
            _loadingCoroutine = StartCoroutine(CompleteLoading());
        }

        /// <summary>
        /// Called by SceneLoadingBroadcaster (ClientRpc) to force hide overlay locally.
        /// </summary>
        public void HideFromBroadcaster()
        {
            if (_loadingCoroutine != null) StopCoroutine(_loadingCoroutine);
            _isCurrentlyLoading = false;
            HideLoadingUI();
        }

        // -------------------------
        // Optional local API (host can call pre-show for everyone via broadcaster)
        // -------------------------

        /// <summary>
        /// If server/host: broadcast pre-show to ALL clients via SceneLoadingBroadcaster.
        /// Otherwise: show locally (fallback).
        /// </summary>
        public void ForceShowLoading(string tip = null)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsServer &&
                SceneLoadingBroadcaster.Instance != null)
            {
                SceneLoadingBroadcaster.Instance.PreShowAllClients(tip ?? GetRandomLoadingTip());
            }
            else
            {
                // Local fallback (useful for standalone or before network starts)
                ShowLoadingUI("Loading...", tip ?? GetRandomLoadingTip());
                if (_loadingCoroutine != null) StopCoroutine(_loadingCoroutine);
                _isCurrentlyLoading = true;
                _loadingProgress = 0f;
                _loadingCoroutine = StartCoroutine(SimulateLoadingProgress());
            }
        }

        public void ForceHideLoading()
        {
            if (_loadingCoroutine != null) StopCoroutine(_loadingCoroutine);
            _isCurrentlyLoading = false;
            HideLoadingUI();
        }

        public bool IsCurrentlyLoading => _isCurrentlyLoading;

        // -------------------------
        // Internal UI helpers
        // -------------------------

        private IEnumerator SimulateLoadingProgress()
        {
            // Simulate from 0 → ~0.7 while waiting for real sync events from broadcaster
            while (_loadingProgress < 0.7f && _isCurrentlyLoading)
            {
                _loadingProgress += Random.Range(0.05f, 0.15f);
                _loadingProgress = Mathf.Clamp01(_loadingProgress);
                UpdateLoadingProgress(_loadingProgress);
                yield return new WaitForSeconds(Random.Range(0.1f, 0.3f));
            }
        }

        private IEnumerator CompleteLoading()
        {
            // Smoothly fill to 1.0
            const float step = 0.1f;
            while (_loadingProgress < 1f)
            {
                _loadingProgress = Mathf.Clamp01(_loadingProgress + step);
                UpdateLoadingProgress(_loadingProgress, "Almost ready...");
                yield return new WaitForSeconds(0.05f);
            }

            // Small delay to avoid flicker
            yield return new WaitForSeconds(minLoadingTime);

            _isCurrentlyLoading = false;
            HideLoadingUI();
            _loadingCoroutine = null;
        }

        private void ShowLoadingUI(string mainText, string tipText = null)
        {
            var ui = _GAME.Scripts.UI.LoadingUI.Instance;
            if (ui != null)
            {
                ui.ShowNetworkLoading(tipText ?? GetRandomLoadingTip());
            }

            Debug.Log($"[NetworkLoadingManager] Show Loading: {mainText} | tip: {tipText}");
        }

        private void UpdateLoadingProgress(float progress, string tipText = null)
        {
            var ui = _GAME.Scripts.UI.LoadingUI.Instance;
            if (ui != null)
            {
                ui.UpdateNetworkProgress(progress);
                if (!string.IsNullOrEmpty(tipText))
                    ui.SetTipText(tipText);
            }
        }

        private void HideLoadingUI()
        {
            var ui = _GAME.Scripts.UI.LoadingUI.Instance;
            if (ui != null)
            {
                ui.HideNetworkLoading();
            }

            Debug.Log("[NetworkLoadingManager] Hide Loading");
        }

        private string GetRandomLoadingTip()
        {
            if (loadingTips == null || loadingTips.Length == 0) return "Loading...";
            return loadingTips[Random.Range(0, loadingTips.Length)];
        }

        // Auto-initialize singleton early (optional)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            var _ = Instance;
            Debug.Log("[NetworkLoadingManager] Auto-initialized");
        }
    }
}

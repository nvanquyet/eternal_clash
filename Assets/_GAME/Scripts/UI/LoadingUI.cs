using System;
using System.Collections;
using DG.Tweening;
using GAME.Scripts.DesignPattern;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.UI
{
    public class LoadingUI : SingletonDontDestroy<LoadingUI>
    {
        [Header("Refs")] [SerializeField] private Slider loadingBar;
        [SerializeField] private GameObject loadingPanel; // root panel của loading
        [SerializeField] private CanvasGroup canvasGroup; // để fade
        [SerializeField] private TextMeshProUGUI percentText;
        [SerializeField] private TextMeshProUGUI tipText;

        [Header("Visual")] [SerializeField, Range(0.05f, 1f)]
        private float fadeDuration = 0.25f;

        [SerializeField] private float minDisplayTime = 0.35f; // tránh “nháy” nếu xong quá nhanh
        [SerializeField] private bool blockRaycasts = true; // chặn input nền khi loading

        [Header("Tips")] [TextArea] [SerializeField]
        private string[] loadingTips = new string[]
        {
            "Loading assets...",
            "Preparing game environment...",
            "Setting up player data...",
            "Loading textures and models...",
            "Initializing game systems...",
            "Almost there, hang tight!",
            "Finalizing setup..."
        };

        private Coroutine _fadeCo;
        private Coroutine _runCo;
        private float _shownAt = -1f;
        private int _lastTipIndex = -1;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Tự động “bắt” reference nếu quên drag
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (loadingBar == null) loadingBar = GetComponentInChildren<Slider>(true);
        }
#endif

        protected override void OnAwake()
        {
            base.OnAwake();

            if (loadingPanel == null) loadingPanel = gameObject;
            if (canvasGroup == null)
            {
                canvasGroup = loadingPanel.GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = loadingPanel.AddComponent<CanvasGroup>();
            }

            // Khởi đầu ẩn
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            if (loadingPanel != null) loadingPanel.SetActive(false);

            ApplyProgressUI(0f);
            SetPercentText("0%");
            SetTipText(string.Empty);
        }

        // =================== PUBLIC API (HIỂN/ẨN) ===================

        /// <summary>Hiện loading UI (không đổi progress) với tip (optional).</summary>
        private void Show(string tip = null)
        {
            EnsurePanelActive();
            if (!string.IsNullOrEmpty(tip)) SetTipText(tip);
            else SetTipText(PickRandomTip());

            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(FadeCanvas(1f));
            _shownAt = Time.unscaledTime;
        }

        /// <summary>Chỉ hiện nếu đang ẩn (dùng khi cập nhật progress đẩy vào).</summary>
        private void ShowIfHidden(string tip = null)
        {
            if (!IsVisible())
            {
                Show(tip);
                ApplyProgressUI(0f);
                SetPercentText("0%");
            }
        }

        /// <summary>Ẩn loading UI (tôn trọng minDisplayTime).</summary>
        private void Hide()
        {
            if (!IsVisible()) return;

            float elapsed = (Time.unscaledTime - _shownAt);
            float wait = Mathf.Max(0f, minDisplayTime - elapsed);

            if (_runCo != null)
            {
                StopCoroutine(_runCo);
                _runCo = null;
            }

            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(Co_DelayedHide(wait));
        }

        // =================== PUBLIC API (PUSH PROGRESS) ===================

        /// <summary>Đặt tiến trình [0..1]. Dùng khi bạn tự đẩy tiến độ.</summary>
        private void SetProgress01(float p, Action callback = null)
        {
            ShowIfHidden();
            p = Mathf.Clamp01(p);
            ApplyProgressUI(p, callback);
        }

        // =================== PUBLIC API (TIMED) ===================

        /// <summary>Đặt tiến trình theo current/max.</summary>
        public void SetProgress(float current, float max, string tip = null, Action callback = null)
        {
            Debug.Log("[LoadingUI] SetProgress: " + current + "/" + max);
            if (max <= 0f)
            {
                SetProgress01(0f);
                return;
            }

            if (!string.IsNullOrEmpty(tip)) SetTipText(tip);
            SetProgress01(current / max, callback);
        }


        /// <summary>Hoàn tất (đặt 100% và ẩn sau minDisplayTime).</summary>
        public void Complete(Action onHidden = null)
        {
            Debug.Log($"[LoadingUI] Complete()");
            ShowIfHidden();
            ApplyProgressUI(1f);
            if (_runCo != null)
            {
                StopCoroutine(_runCo);
                _runCo = null;
            }

            _runCo = StartCoroutine(Co_CompleteThenHide(onHidden));
        }

        public void RunTimed(float seconds, Action onComplete = null, string tipAtStart = null, bool autoHide = true, float delay = 0f)
        {
            if (_runCo != null) StopCoroutine(_runCo);
            _runCo = StartCoroutine(Co_RunTimed(seconds, onComplete, tipAtStart, autoHide,delay));
        }

        // =================== PRIVATE ===================

        private bool IsVisible() => loadingPanel != null && loadingPanel.activeSelf && canvasGroup.alpha > 0.001f;

        private void EnsurePanelActive()
        {
            if (loadingPanel != null && !loadingPanel.activeSelf) loadingPanel.SetActive(true);
            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = blockRaycasts;
                canvasGroup.interactable = true;
            }
        }

        private void ApplyProgressUI(float p01, Action callback = null)
        {
            if (loadingBar != null)
            {
                loadingBar
                    .DOValue(p01, 0.25f)
                    .SetEase(Ease.Linear)
                    .OnUpdate(() =>
                    {
                        // đọc giá trị hiện tại của slider để hiển thị %
                        SetPercentText($"{Mathf.RoundToInt(loadingBar.value * 100f)}%");
                    })
                    .OnComplete(() =>
                    {
                        // đảm bảo về đúng % cuối cùng
                        SetPercentText($"{Mathf.RoundToInt(p01 * 100f)}%");
                        callback?.Invoke();
                    });
            }
        }

        public void SetTipText(string tip)
        {
            if (tipText != null)
            {
                tipText.text = string.IsNullOrEmpty(tip) ? "Loading..." : $"{tip}\n\nLoading...";
            }
        }

        private void SetPercentText(string text)
        {
            if (percentText != null) percentText.text = text ?? "0%";
        }

        private string PickRandomTip()
        {
            if (loadingTips == null || loadingTips.Length == 0) return string.Empty;
            if (loadingTips.Length == 1) return loadingTips[0];

            int idx;
            do
            {
                idx = UnityEngine.Random.Range(0, loadingTips.Length);
            } while (idx == _lastTipIndex);

            _lastTipIndex = idx;
            return loadingTips[idx];
        }

        private IEnumerator FadeCanvas(float target)
        {
            EnsurePanelActive();

            float start = canvasGroup.alpha;
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, target, t / fadeDuration);
                yield return null;
            }

            canvasGroup.alpha = target;

            if (Mathf.Approximately(target, 0f))
            {
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
                if (loadingPanel != null) loadingPanel.SetActive(false);
            }
        }

        private IEnumerator Co_DelayedHide(float wait)
        {
            if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
            yield return FadeCanvas(0f);

            // reset UI
            ApplyProgressUI(0f);
            SetPercentText("0%");
            SetTipText(string.Empty);
            _runCo = null;
        }

        private IEnumerator Co_CompleteThenHide(Action onHidden)
        {
            // Đảm bảo minDisplayTime trước khi ẩn
            float elapsed = (Time.unscaledTime - _shownAt);
            if (elapsed < minDisplayTime)
                yield return new WaitForSecondsRealtime(minDisplayTime - elapsed);
            yield return FadeCanvas(0f);
            onHidden?.Invoke();

            // reset
            ApplyProgressUI(0f);
            SetPercentText("0%");
            SetTipText(string.Empty);
            _runCo = null;
        }


        private IEnumerator Co_RunTimed(float seconds, Action onComplete, string tipAtStart, bool autoHide = true, float delay = 0f)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime( delay);
            seconds = Mathf.Max(0f, seconds);

            Show(string.IsNullOrEmpty(tipAtStart) ? PickRandomTip() : tipAtStart);
            ApplyProgressUI(0f);

            float t = 0f;
            bool tipSwapped = false;

            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(t / seconds);

                // Nếu không auto hide, giới hạn tối đa 95%
                if (!autoHide)
                {
                    progress = Mathf.Min(progress, 0.95f);
                }

                ApplyProgressUI(progress);

                if (!tipSwapped && t >= seconds * 0.5f)
                {
                    tipSwapped = true;
                    SetTipText(PickRandomTip());
                }

                yield return null;
            }

            if (autoHide)
            {
                // Behavior cũ: hoàn thành và ẩn
                ApplyProgressUI(1f);

                // bảo đảm minDisplayTime
                float elapsed = (Time.unscaledTime - _shownAt);
                if (elapsed < minDisplayTime)
                    yield return new WaitForSecondsRealtime(minDisplayTime - elapsed);

                yield return FadeCanvas(0f);
                onComplete?.Invoke();

                // reset
                ApplyProgressUI(0f);
                SetPercentText("0%");
                SetTipText(string.Empty);
                _runCo = null;
            }
            else
            {
                // Behavior mới: dừng ở 95% và đợi Complete()
                ApplyProgressUI(0.95f);
                SetPercentText("95%");
                SetTipText("Finalizing...");

                // Gọi callback nhưng KHÔNG ẩn loading và KHÔNG reset _runCo
                onComplete?.Invoke();
            }
        }


        /// <summary>Hiện loading UI cho network scene loading</summary>
        public void ShowNetworkLoading(string tip = null)
        {
            Debug.Log("[LoadingUI] ShowNetworkLoading");

            // Cancel any existing loading operation
            if (_runCo != null)
            {
                StopCoroutine(_runCo);
                _runCo = null;
            }

            Show(tip ?? "Connecting to game...");
            ApplyProgressUI(0f);
            SetPercentText("0%");
        }

        /// <summary>Ẩn network loading</summary>
        public void HideNetworkLoading()
        {
            Debug.Log("[LoadingUI] HideNetworkLoading");

            // Set progress to 100% first
            ApplyProgressUI(1f);
            SetPercentText("100%");

            // Then hide after a short delay
            StartCoroutine(DelayedHideNetwork());
        }

        /// <summary>Cập nhật progress từ network</summary>
        public void UpdateNetworkProgress(float progress01)
        {
            if (!IsVisible()) return;

            progress01 = Mathf.Clamp01(progress01);
            ApplyProgressUI(progress01);

            // Update percent text immediately (no tween)
            SetPercentText($"{Mathf.RoundToInt(progress01 * 100f)}%");
        }

        // =================== PRIVATE NETWORK HELPERS ===================

        private IEnumerator DelayedHideNetwork()
        {
            // Đảm bảo người dùng nhìn thấy 100% một chút
            yield return new WaitForSecondsRealtime(0.3f);

            // Respect minimum display time
            float elapsed = (Time.unscaledTime - _shownAt);
            if (elapsed < minDisplayTime)
                yield return new WaitForSecondsRealtime(minDisplayTime - elapsed);

            // Fade out
            yield return FadeCanvas(0f);

            // Reset UI
            ApplyProgressUI(0f);
            SetPercentText("0%");
            SetTipText(string.Empty);
        }
    }
}
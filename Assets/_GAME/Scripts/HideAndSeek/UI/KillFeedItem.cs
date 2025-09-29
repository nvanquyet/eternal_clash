using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.UI
{
    /// <summary>
    /// Component for individual kill feed item
    /// </summary>
    public class KillFeedItem : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI textComponent;
        [SerializeField] private CanvasGroup canvasGroup;

        public void Initialize(string message, Color color)
        {
            textComponent.text = message;
            textComponent.color = color;
            canvasGroup.alpha = 0f;
        }

        public IEnumerator FadeIn(float duration)
        {
            return Fade(0f, 1f, duration);
        }

        public IEnumerator FadeOut(float duration)
        {
            return Fade(1f, 0f, duration);
        }

        private IEnumerator Fade(float startAlpha, float endAlpha, float duration)
        {
            if (canvasGroup == null) yield break;

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
                yield return null;
            }

            canvasGroup.alpha = endAlpha;
        }
    }
}
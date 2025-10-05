using _GAME.Scripts.Controller;
using GAME.Scripts.DesignPattern;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Michsky.MUIP;

namespace _GAME.Scripts.UI
{
    public class SettingsPopup : SingletonDontDestroy<SettingsPopup>
    {
        [Header("UI References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform popupPanel;
        
        [SerializeField] private WindowManager myWindowManager;
        
        [SerializeField] private Button closeButton;
        
        [Header("Animation Settings")]
        [SerializeField] private float fadeDuration = 0.3f;
        [SerializeField] private float scaleDuration = 0.3f;
        [SerializeField] private Ease scaleEase = Ease.OutBack;
        [SerializeField] private AnimationType animType = AnimationType.ScaleAndFade;
        
        [Header("Audio Settings")]
        [SerializeField] private bool saveOnChange = true;
        
        private Sequence _showSequence;
        private Sequence _hideSequence;

        private enum AnimationType
        {
            Fade,
            Scale,
            ScaleAndFade,
            SlideFromTop,
            SlideFromBottom
        }
        
        protected override void Awake()
        {
            base.Awake();
            SetupListeners();
        }
        
        private void Start()
        {
            HideImmediate();
        }
        
        
        private void SetupListeners()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Hide);
        }
        
        public void Show()
        {
            myWindowManager.OpenWindowByIndex(0);
            
            _showSequence?.Kill();
            _hideSequence?.Kill();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = true;
            
            _showSequence = DOTween.Sequence();
            
            switch (animType)
            {
                case AnimationType.Fade:
                    canvasGroup.alpha = 0f;
                    _showSequence.Append(canvasGroup.DOFade(1f, fadeDuration));
                    break;
                    
                case AnimationType.Scale:
                    popupPanel.localScale = Vector3.zero;
                    _showSequence.Append(popupPanel.DOScale(Vector3.one, scaleDuration).SetEase(scaleEase));
                    break;
                    
                case AnimationType.ScaleAndFade:
                    canvasGroup.alpha = 0f;
                    popupPanel.localScale = Vector3.zero;
                    _showSequence.Append(canvasGroup.DOFade(1f, fadeDuration));
                    _showSequence.Join(popupPanel.DOScale(Vector3.one, scaleDuration).SetEase(scaleEase));
                    break;
                    
                case AnimationType.SlideFromTop:
                    canvasGroup.alpha = 0f;
                    Vector2 startPosTop = popupPanel.anchoredPosition;
                    startPosTop.y += 1000f;
                    popupPanel.anchoredPosition = startPosTop;
                    _showSequence.Append(canvasGroup.DOFade(1f, fadeDuration));
                    _showSequence.Join(popupPanel.DOAnchorPosY(0f, scaleDuration).SetEase(scaleEase));
                    break;
                    
                case AnimationType.SlideFromBottom:
                    canvasGroup.alpha = 0f;
                    Vector2 startPosBottom = popupPanel.anchoredPosition;
                    startPosBottom.y -= 1000f;
                    popupPanel.anchoredPosition = startPosBottom;
                    _showSequence.Append(canvasGroup.DOFade(1f, fadeDuration));
                    _showSequence.Join(popupPanel.DOAnchorPosY(0f, scaleDuration).SetEase(scaleEase));
                    break;
            }
            
            _showSequence.OnComplete(() =>
            {
                canvasGroup.interactable = true;
            });
        }

        private void Hide()
        {
            _showSequence?.Kill();
            _hideSequence?.Kill();
            
            canvasGroup.interactable = false;
            
            _hideSequence = DOTween.Sequence();
            
            switch (animType)
            {
                case AnimationType.Fade:
                    _hideSequence.Append(canvasGroup.DOFade(0f, fadeDuration));
                    break;
                    
                case AnimationType.Scale:
                    _hideSequence.Append(popupPanel.DOScale(Vector3.zero, scaleDuration).SetEase(Ease.InBack));
                    break;
                    
                case AnimationType.ScaleAndFade:
                    _hideSequence.Append(canvasGroup.DOFade(0f, fadeDuration));
                    _hideSequence.Join(popupPanel.DOScale(Vector3.zero, scaleDuration).SetEase(Ease.InBack));
                    break;
                    
                case AnimationType.SlideFromTop:
                case AnimationType.SlideFromBottom:
                    _hideSequence.Append(canvasGroup.DOFade(0f, fadeDuration));
                    float targetY = animType == AnimationType.SlideFromTop ? 1000f : -1000f;
                    _hideSequence.Join(popupPanel.DOAnchorPosY(targetY, scaleDuration).SetEase(Ease.InBack));
                    break;
            }
            
            _hideSequence.OnComplete(() =>
            {
                canvasGroup.blocksRaycasts = false;
            });
        }
        
        private void HideImmediate()
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        
        protected override void OnDestroy()
        {
            _showSequence?.Kill();
            _hideSequence?.Kill();
            
                
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Hide);
            
            base.OnDestroy();
        }
    }
}
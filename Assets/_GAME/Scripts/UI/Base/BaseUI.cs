using System;
using DG.Tweening;
using UnityEngine;

namespace _GAME.Scripts.UI.Base
{
    public enum UIType
    {
        None,
        Home,
        MainMenu,
        Lobby,
        GamePlay,
        Settings,
        PauseMenu,
        GameOver
    }
    
    [RequireComponent(typeof(CanvasGroup))]
    public class BaseUI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private UIType uiType;
        
        public UIType UIType => uiType;
        
#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            canvasGroup = GetComponentInChildren<CanvasGroup>();
        }
#endif
      
        public void Show(Action callback,bool fade = true)
        {
            gameObject.SetActive(true);
            if (fade)
            {
                canvasGroup.DOFade(1f, 0.25f).From(0).OnComplete(() => callback?.Invoke());
            }
            else
            {
                canvasGroup.alpha = 1f;
                callback?.Invoke();
            }
        }
        
        public void Hide(Action callback, bool fade = true)
        {
            if (fade)
            {
                canvasGroup.DOFade(0f, 0.25f).From(1).OnComplete(() =>
                {
                    callback?.Invoke();
                    gameObject.SetActive(false);
                });
            }
            else
            {
                callback?.Invoke();
                gameObject.SetActive(false);
            }
        }
        
    }
}

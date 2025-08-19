using System;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.UI
{
    [RequireComponent(typeof(Toggle))]
    public class ToggleShowExtension : MonoBehaviour
    {
        [SerializeField] private Toggle toggleShowExtensions;
        [SerializeField] private GameObject extensionsPanel;
        
        private Action onShowActionCallback;
        
        
        public void SetOnShowActionCallback(Action callback)
        {
            onShowActionCallback += callback;
        }
        
        public void UnRegisterOnShowActionCallback(Action callback)
        {
            onShowActionCallback -= callback;
        }
        
#if UNITY_EDITOR
        private void OnValidate()
        {
            toggleShowExtensions = GetComponent<Toggle>();
        }
#endif
        
        private void Start()
        {
            if (toggleShowExtensions == null)
            {
                Debug.LogError("ToggleShowExtensions is not assigned in ToggleListExtension.");
                return;
            }
            
            toggleShowExtensions.onValueChanged.AddListener(OnToggleValueChanged);
        }

        private void OnEnable()
        {
            if(toggleShowExtensions) toggleShowExtensions.isOn = false;
            if (extensionsPanel)  extensionsPanel.SetActive(false);
        }


        private void OnDestroy()
        {
            //Remove listener to prevent memory leaks
            if (toggleShowExtensions != null)
            {
                toggleShowExtensions.onValueChanged.RemoveListener(OnToggleValueChanged);
            }
            onShowActionCallback = null;
        }

        private void OnToggleValueChanged(bool arg0)
        {
            extensionsPanel?.SetActive(arg0);
            if (arg0)
            {
                onShowActionCallback?.Invoke();
            }
        }
    }
}

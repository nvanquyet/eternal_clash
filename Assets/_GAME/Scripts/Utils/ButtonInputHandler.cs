using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace _GAME.Scripts.Utils
{
    /// <summary>
    /// Handles button press and release events for continuous input
    /// </summary>
    public class ButtonInputHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Header("Button Settings")]
        [SerializeField] private Button button;
        [SerializeField] private bool enableContinuousInput = true;
        
        // Events
        public Action OnButtonPressed;
        public Action OnButtonReleased;
        
        // State
        private bool isPressed = false;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            // Get button component if not assigned
            if (button == null)
                button = GetComponent<Button>();
            
            // Ensure button is interactable
            if (button != null)
                button.interactable = true;
        }
        
        private void OnDisable()
        {
            // Release button when disabled
            if (isPressed)
            {
                ReleaseButton();
            }
        }
        
        #endregion
        
        #region IPointerHandler Implementation
        
        public void OnPointerDown(PointerEventData eventData)
        {
            if (button != null && !button.interactable) return;
            
            PressButton();
        }
        
        public void OnPointerUp(PointerEventData eventData)
        {
            if (isPressed)
            {
                ReleaseButton();
            }
        }
        
        public void OnPointerExit(PointerEventData eventData)
        {
            // Release when pointer exits button area (for mobile/touch)
            if (isPressed)
            {
                ReleaseButton();
            }
        }
        
        #endregion
        
        #region Button State Management
        
        private void PressButton()
        {
            if (isPressed) return;
            
            isPressed = true;
            
            Debug.Log($"[ButtonInputHandler] Button {gameObject.name} pressed");
            OnButtonPressed?.Invoke();
        }
        
        private void ReleaseButton()
        {
            if (!isPressed) return;
            
            isPressed = false;
            
            Debug.Log($"[ButtonInputHandler] Button {gameObject.name} released");
            OnButtonReleased?.Invoke();
        }
        
        #endregion
        
        #region Public Methods
        
        public bool IsPressed => isPressed;
        
        public void SetInteractable(bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
            
            // Release if becoming non-interactable
            if (!interactable && isPressed)
            {
                ReleaseButton();
            }
        }
        
        /// <summary>
        /// Manually trigger button press (useful for testing or external triggers)
        /// </summary>
        public void TriggerPress()
        {
            PressButton();
        }
        
        /// <summary>
        /// Manually trigger button release (useful for testing or external triggers)
        /// </summary>
        public void TriggerRelease()
        {
            ReleaseButton();
        }
        
        /// <summary>
        /// Force release button (useful for cleanup)
        /// </summary>
        public void ForceRelease()
        {
            if (isPressed)
            {
                ReleaseButton();
            }
        }
        
        #endregion
        
        #region Debug
        
        [ContextMenu("Test Press")]
        private void TestPress()
        {
            TriggerPress();
        }
        
        [ContextMenu("Test Release")]
        private void TestRelease()
        {
            TriggerRelease();
        }
        
        #endregion
    }
}
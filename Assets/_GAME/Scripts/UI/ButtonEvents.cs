using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.UI
{
    [RequireComponent(typeof(Button))]
    public class ButtonEvents : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler
    {
        public System.Action OnPointerDownEvent;
        public System.Action OnPointerUpEvent;

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData eventData)
        {
            OnPointerDownEvent?.Invoke();
        }

        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData eventData)
        {
            OnPointerUpEvent?.Invoke();
        }
    }
}
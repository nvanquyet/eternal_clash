using UnityEngine;
using UnityEngine.Serialization;

namespace _GAME.Scripts.UI.Base
{
    public enum PopUpType
    {
        None,
        Success,
        Error,
        Warning,
        Info
    }
    
    public class BasePopUp : MonoBehaviour
    {
        [SerializeField] private PopUpType popUpType;
        
        public PopUpType PopUpPopUpType => popUpType;
    }
}

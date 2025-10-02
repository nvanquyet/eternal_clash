using _GAME.Scripts.HideAndSeek;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.Test
{
    public class TestUI : MonoBehaviour
    {
        [SerializeField] private Button hostButton; 
        [SerializeField] private Button clientButton;
        
        
        private void Awake()
        {
            hostButton.onClick.AddListener(() =>
            {
                Debug.Log("[TestUI] Host button clicked");
                 NetworkManager.Singleton.StartHost();
            });
            
            clientButton.onClick.AddListener(() =>
            {
                Debug.Log("[TestUI] Client button clicked");
                 NetworkManager.Singleton.StartClient();
            });
        }
    }
}

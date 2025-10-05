using _GAME.Scripts.Data;
using _GAME.Scripts.UI.Base;
using Michsky.MUIP;
using TMPro;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.UI
{
    public class GamePlayUI : BaseUI
    {
        [Header("Information")]
        [SerializeField] private TextMeshProUGUI usernameText;
        [SerializeField] private TextMeshProUGUI scoreText;
        
        private void Start()
        {
            InitializeInformation();
        }

        private void InitializeInformation()
        {
            usernameText.text = LocalData.UserName;
            scoreText.text = "Score: 0";
        }
     
    } 
}

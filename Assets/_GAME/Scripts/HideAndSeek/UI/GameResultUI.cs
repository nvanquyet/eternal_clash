using System;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Networking;
using _GAME.Scripts.UI;
using _GAME.Scripts.UI.Base;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.HideAndSeek.UI
{
    public class GameResultUI : BaseUI
    {
        [SerializeField] Button btnContinue;
        [SerializeField] TextMeshProUGUI resultText;

        private void Start()
        {
            GameEvent.OnGameEnded += OnGameEnded;
            btnContinue.onClick.RemoveAllListeners();
            btnContinue.onClick.AddListener(OnClickContinue);
            //Disable at start
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            GameEvent.OnGameEnded -= OnGameEnded;
        }

        private void OnGameEnded(Role obj)
        {
            Show(null, true);
            var ownerRole = GameManager.Instance.GetPlayerRoleWithId(PlayerIdManager.LocalClientId);
            if (obj == Role.None)
            {
                resultText.text = "Game Ended in a Draw!";
            }
            else if (obj == ownerRole)
            {
                resultText.text = "You Win!";
            }
            else
            {
                resultText.text = "You Lose!";
            }

            //Auto call continue after 5 seconds
            Invoke(nameof(OnClickContinue), 5f);
        }

        private void OnClickContinue()
        {
            //Load to home screen
            LoadingUI.Instance.RunTimed(1, GameManager.Instance.Clear, "Finish Game, Return home scene", false);
            SceneController.Instance.LoadSceneAsync(SceneHelper.ToSceneName(SceneDefinitions.Home));
        }
    }
}
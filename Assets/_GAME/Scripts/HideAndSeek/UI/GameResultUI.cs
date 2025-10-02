using System;
using System.Collections;
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

        
        private Coroutine _gameEndCoroutine;
        
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
            btnContinue.onClick.RemoveAllListeners();
            if(_gameEndCoroutine != null) StopCoroutine(_gameEndCoroutine);
            _gameEndCoroutine = null;
        }

        private void OnGameEnded(Role obj)
        {
            if(_gameEndCoroutine != null) StopCoroutine(_gameEndCoroutine);
            _gameEndCoroutine = StartCoroutine(IEGameEnd(obj));
        }
        
        
        private IEnumerator IEGameEnd(Role winner)
        {
            yield return new WaitForSeconds(3f);
            Show(null, true);
            var ownerRole = GameManager.Instance.GetPlayerRoleWithId(PlayerIdManager.LocalClientId);
            if (winner == Role.None)
            {
                resultText.text = "Game Ended in a Draw!";
            }
            else if (winner == ownerRole)
            {
                resultText.text = "You Win!";
            }
            else
            {
                resultText.text = "You Lose!";
            }

            yield return new WaitForSeconds(5f);
            OnClickContinue();
        }
        

        private void OnClickContinue()
        {
            //Load to home screen
            LoadingUI.Instance.RunTimed(1, GameManager.Instance.Clear, "Finish Game, Return home scene", false);
            SceneController.Instance.LoadSceneAsync(SceneHelper.ToSceneName(SceneDefinitions.Home));
        }
    }
}
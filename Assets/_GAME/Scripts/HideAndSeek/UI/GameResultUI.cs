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
        [SerializeField] Button btnHome;
        [SerializeField] TextMeshProUGUI resultText;

        
        private Coroutine _gameEndCoroutine;
        
        private void Start()
        {
            GameEvent.OnGameEnded += OnGameEnded;
            btnContinue.onClick.AddListener(OnClickContinue);
            btnHome.onClick.AddListener(OnClickHome);
            //Disable at start
            gameObject.SetActive(false);
        }


        private void OnDestroy()
        {
            GameEvent.OnGameEnded -= OnGameEnded;
            btnContinue.onClick.RemoveListener(OnClickContinue);
            btnHome.onClick.RemoveListener(OnClickHome);
            if(_gameEndCoroutine != null) StopCoroutine(_gameEndCoroutine);
            _gameEndCoroutine = null;
        }

        private void OnGameEnded(Role obj)
        {
            gameObject.SetActive(true);
            Canvas.alpha = 0;
            if(_gameEndCoroutine != null) StopCoroutine(_gameEndCoroutine);
            _gameEndCoroutine = StartCoroutine(IEGameEnd(obj));
        }
        
        
        private IEnumerator IEGameEnd(Role winner)
        {
            yield return new WaitForSeconds(3f);
            Show(null);
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
            if(GameNet.Instance.Network.IsHost) OnReturnLobby();
        }
        

        private void OnClickContinue()
        {
            //Waiting Host click continue
            if(GameNet.Instance.Network.IsHost) OnReturnLobby();
            else
            {
                //Disable button
                btnContinue.interactable = false;
            }
        }
        
        
        private  async void OnClickHome()
        {
            try
            {
                LoadingUI.Instance.RunTimed(1, GameManager.Instance.Clear, "Finish Game, Return home scene", false);
                //Clear network
                if (GameNet.Instance.Network.IsHost)
                {
                    //Remove lobby
                    await GameNet.Instance.Lobby.RemoveLobbyAsync();
                }
                else
                {
                    await GameNet.Instance.Lobby.LeaveLobbyAsync();
                }
                await GameNet.Instance.Network.StopAsync();
                SceneController.Instance.LoadSceneAsync(SceneHelper.ToSceneName(SceneDefinitions.Home));
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameResultUI] Error returning home: {e.Message}");
            }
        }
        
        private void OnReturnLobby()
        {
            //Load to home screen
            _ = GameNet.Instance.Network.LoadSceneAsync(SceneDefinitions.WaitingRoom);
        }
    }
}
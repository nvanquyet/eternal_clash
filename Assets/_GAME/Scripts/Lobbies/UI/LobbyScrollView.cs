using System;
using System.Threading.Tasks;
using TMPro;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.Lobbies.UI
{
    public class LobbyScrollView : MonoBehaviour
    {
        [SerializeField] private LobbyItemScrollView lobbyItemPrefab;
        [SerializeField] private Transform contentTransform;
        [SerializeField] private Button buttonRefresh;
        
        [SerializeField] private TMP_InputField lobbyCodeInputField;
        [SerializeField] private Button buttonJoinLobby;
        [SerializeField] private Button buttonCreateLobby;
        
        [SerializeField] private LobbyPasswordConfirmation lobbyPasswordConfirmation;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (lobbyItemPrefab == null) lobbyItemPrefab = GetComponentInChildren<LobbyItemScrollView>();
            if (lobbyPasswordConfirmation == null) lobbyPasswordConfirmation = GetComponentInChildren<LobbyPasswordConfirmation>();
        }
#endif

        private void Start()
        {
            //Fetch lobby from LobbyHandler
            InitializedButton();
            FetchLobby();
        }

        private void InitializedButton()
        {
            buttonRefresh?.onClick.RemoveAllListeners();
            buttonJoinLobby?.onClick.RemoveAllListeners();
            buttonCreateLobby?.onClick.RemoveAllListeners();
            
            buttonRefresh?.onClick.AddListener(FetchLobby);
            buttonJoinLobby?.onClick.AddListener(() =>
            {
                var lobbyCode = lobbyCodeInputField.text.Trim();
                if (string.IsNullOrEmpty(lobbyCode))
                {
                     Debug.LogError("Lobby code cannot be empty.");
                     return;
                }
                OnClickJoinLobby(lobbyCode);
            });
            
            buttonCreateLobby?.onClick.AddListener(OnClickCreateLobby);
        }
        
        
        private void OnEnable()
        {
            FetchLobby();
        }

        private async void FetchLobby()
        {
            try
            {
                // Clear existing items
                foreach (Transform child in contentTransform)
                {
                    Destroy(child.gameObject);
                }
            
                // Fetch lobbies from LobbyHandler
                var lobbies = await LobbyUIController.Instance.LobbyHandler.GetLobbyListAsync();
                if (lobbies == null || lobbies.Count == 0)
                {
                    Debug.Log("No lobbies found.");
                    return;
                }
                // Instantiate lobby items
                foreach (var lobby in lobbies)
                {
                    var lobbyItem = Instantiate(lobbyItemPrefab, contentTransform);
                    lobbyItem.Initialize(lobby, OnClickJoinLobby);
                    lobbyItem.transform.SetParent(contentTransform);
                }
                
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }


        private async void OnClickJoinLobby(string lobbyCode)
        {
            try
            {
                if(string.IsNullOrEmpty(lobbyCode)) return;
                var lobby = await  LobbyUIController.Instance.LobbyHandler.GetLobbyInfoAsync(lobbyCode);
                OnClickJoinLobby(lobby);
            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
        }
        
        
        private async void OnClickCreateLobby()
        {
            try
            {
                // Show lobby creation UI or logic
                var lobby = await  LobbyUIController.Instance.LobbyHandler.CreateLobbyAsync("New Lobby", 4, new CreateLobbyOptions());
                Debug.Log("Lobby created successfully.");
                //Show to UI
                if (lobby == null)
                {
                    Debug.LogError("Failed to create lobby, received null lobby.");
                    return;
                }
                LobbyUIController.Instance.ShowLobbySettingUI(lobby);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }
        
        private async void OnClickJoinLobby(Lobby lobby)
        {
            try
            {
                if(lobby == null)
                {
                    Debug.LogError($"Lobby not found.");
                    return;
                }
                if (lobby.HasPassword)
                {
                    // Show password confirmation dialog
                    lobbyPasswordConfirmation.Initialized((password) =>
                    {
                        OnJoinLobby(lobby.Id, password);
                    });
                }
                else
                {
                    // Join the lobby directly
                    await  LobbyUIController.Instance.LobbyHandler.JoinLobbyAsync(lobby.Id, "");
                    Debug.Log($"Joined lobby: {lobby.Name}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        private async void OnJoinLobby(string lobbyId, string password)
        {
            try
            {
                var result = await LobbyHandler.Instance.JoinLobbyAsync(lobbyId, password);
                if (!result)
                {
                    Debug.LogError("Failed to join lobby. Lobby might not exist or password is incorrect.");
                    return;
                }
                Debug.Log($"Joined lobby successfully with ID: {lobbyId}");
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }
        
        
    }
}

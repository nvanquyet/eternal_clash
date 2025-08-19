// using System.Collections.Generic;
// using System.Threading.Tasks;
// using GAME.Scripts.DesignPattern;
// using Unity.Services.Lobbies;
// using Unity.Services.Lobbies.Models;
// using UnityEngine;
// namespace _GAME.Scripts.Lobbies.UI
// {
//     /// <summary>
//     /// Example UI Controller để sử dụng với LobbyManager
//     /// Đây là ví dụ về cách tích hợp UI với hệ thống lobby
//     /// </summary>
//     public class LobbyUIController : Singleton<LobbyUIController>
//     {
//         [SerializeField] private LobbyScrollView lobbyScrollView;
//         [SerializeField] private LobbySettingUI lobbySettingUI;
//         [SerializeField] private LobbyHandler lobbyHandler;
//
//         public LobbyHandler LobbyHandler => lobbyHandler;
//         
// #if UNITY_EDITOR
//         private void OnValidate()
//         {
//             lobbyScrollView = GetComponentInChildren<LobbyScrollView>();
//             lobbySettingUI = GetComponentInChildren<LobbySettingUI>();
//             lobbyHandler = GetComponentInChildren<LobbyHandler>();
//         }
// #endif
//         
//         public void ShowLobbySettingUI(Lobby lobby)
//         {
//             if (lobbySettingUI != null)
//             {
//                 lobbySettingUI.gameObject.SetActive(true);
//                 lobbySettingUI.DisplayLobby(lobby);
//             }
//             else
//             {
//                 Debug.LogError("LobbySettingUI is not assigned or found in the scene.");
//             }
//         }
//
//
//         public async Task<Lobby> CreateLobbyAsync(string lobbyName, int maxPlayers, bool isPrivate = false)
//         {
//             if (lobbyHandler == null)
//             {
//                 Debug.LogError("LobbyHandler is not assigned or found in the scene.");
//                 return null;
//             }
//
//             var lobbyOptions = new CreateLobbyOptions
//             {
//                 IsPrivate = false,
//                 Data = new Dictionary<string, DataObject>
//                 {
//                     { "gameMode", new DataObject(DataObject.VisibilityOptions.Public, "Normal") },
//                     { "gameStarted", new DataObject(DataObject.VisibilityOptions.Member, "false") }
//                 },
//             };
//             var lobby = await lobbyHandler.CreateLobbyAsync(lobbyName, maxPlayers, lobbyOptions);
//             if (lobby != null)
//             {
//                 Debug.Log($"Lobby created successfully: {lobby.Name}");
//             }
//             else
//             {
//                 Debug.LogError("Failed to create lobby.");
//             }
//
//             return lobby;
//         }
//     }
// }
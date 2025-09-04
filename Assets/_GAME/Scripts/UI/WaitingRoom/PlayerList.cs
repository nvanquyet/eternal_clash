using System;
using _GAME.Scripts.Networking;
using _GAME.Scripts.Networking.Lobbies;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.UI.WaitingRoom
{
    public class PlayerList : MonoBehaviour
    {
        [SerializeField] private ItemPlayerList itemPlayerListPrefab;
        [SerializeField] private Transform playerListContainer;
        
        
        //Initialize the player list with lobby param
        public void Initialized()
        {
            try
            {
                var allPlayer = LobbyManager.Instance.CurrentLobby.Players;
                var isHost = NetworkController.Instance.IsHost;
                
                if (playerListContainer == null || itemPlayerListPrefab == null)
                {
                    Debug.LogError("Lobby or player list container is not assigned.");
                    return;
                }

                // Clear existing items
                foreach (Transform child in playerListContainer)
                {
                    Destroy(child.gameObject);
                }

                // Create a new item for each player in the lobby
                foreach (var player in allPlayer)
                {
                    var isMe = PlayerIdManager.IsMe(player.Id);
                    var item = Instantiate(itemPlayerListPrefab, playerListContainer);
                    item.Initialize(player, isMe, isHost);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }
    }
}

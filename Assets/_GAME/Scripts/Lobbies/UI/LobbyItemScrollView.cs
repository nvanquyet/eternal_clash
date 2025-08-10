using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.Lobbies.UI
{
    public class LobbyItemScrollView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI lobbyNameText;
        [SerializeField] private TextMeshProUGUI lobbyCodeText;
        [SerializeField] private TextMeshProUGUI lobbyPlayersText;
        [SerializeField] private Button btnJoinLobby;
        
        
        public void Initialize(string lobbyName, string lobbyCode, int currentPlayers, int maxPlayers, System.Action onJoinClick)
        {
            lobbyNameText.text = lobbyName;
            lobbyCodeText.text = $"Lobby Code: {lobbyCode}";
            lobbyPlayersText.text = $"{currentPlayers}/{maxPlayers}";

            btnJoinLobby.onClick.RemoveAllListeners();
            btnJoinLobby.onClick.AddListener(() => onJoinClick?.Invoke());
            btnJoinLobby.interactable = currentPlayers < maxPlayers;
        }
        
        public void Initialize(Lobby lobby, System.Action<Lobby> onJoinClick)
        {
            if (lobby == null)
            {
                Destroy(this);
                return;
            }
            lobbyNameText.text = lobby.Name;
            lobbyCodeText.text = $"Lobby Code: {lobby.LobbyCode}";
            var currentPlayer = lobby.Players.Count;
            lobbyPlayersText.text = $"{currentPlayer}/{lobby.MaxPlayers}";

            btnJoinLobby.onClick.RemoveAllListeners();
            btnJoinLobby.onClick.AddListener(() => onJoinClick?.Invoke(lobby));
            btnJoinLobby.interactable = currentPlayer < lobby.MaxPlayers;
        }
    }
}

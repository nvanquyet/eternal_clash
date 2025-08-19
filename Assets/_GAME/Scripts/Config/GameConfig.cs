using GAME.Scripts.DesignPattern;
using UnityEngine;

namespace _GAME.Scripts.Config
{
    public class GameConfig : SingletonDontDestroy<GameConfig>
    {
       [Header("Lobby Configuration")]
       public int[] maxPlayersPerLobby = {2, 4, 6, 8}; 
    }
}

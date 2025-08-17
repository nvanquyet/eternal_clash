using System;
using System.Collections.Generic;
using UnityEngine;

namespace _GAME.Scripts.Lobbies.UI
{
    [Serializable]
    public class LobbyInfo
    {
        public string LobbyId;
        public string LobbyName;
        public string LobbyCode;
        public string Password; // có thể rỗng nếu không dùng
        public bool LocalIsHost; // quyền của mình
        public int MaxPlayers = 8; // 4 hoặc 8
        public readonly List<Unity.Services.Lobbies.Models.Player> Players = new();
    }

    

}
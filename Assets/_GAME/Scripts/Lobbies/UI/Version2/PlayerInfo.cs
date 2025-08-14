using System;
using System.Collections.Generic;
using UnityEngine;

namespace _GAME.Scripts.Lobbies.UI.Version2
{
    [Serializable]
    public class PlayerInfo
    {
        public string PlayerId;          
        public string DisplayName;
        public Sprite Avatar;            // có thể null -> dùng default
        public bool IsReady;
        public bool IsHost;              // chỉ true cho host (chủ phòng)
        public bool IsConnected = true;  // false = slot đang “Waiting...”
    }

    [Serializable]
    public class LobbyInfo
    {
        public string LobbyId;
        public string LobbyName;
        public string LobbyCode;
        public string Password;          // có thể rỗng nếu không dùng
        public string LocalPlayerId;     // id của chính mình
        public bool LocalIsHost;         // quyền của mình
        public int MaxPlayers = 8;       // 4 hoặc 8
        public readonly List<PlayerInfo> Players = new();
    }
}
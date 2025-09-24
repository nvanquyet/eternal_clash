using System;

namespace _GAME.Scripts.HideAndSeek
{
    public static class GameEvent
    {
        public static Action<bool> OnGameEnd;
        public static Action OnGameStart;
        
        public static Action OnRoleAssignedSuccess;
    }
}

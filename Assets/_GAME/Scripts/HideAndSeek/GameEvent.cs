using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace _GAME.Scripts.HideAndSeek
{
    public static class GameEvent
    {
        #region Game Play Event
        // EVENTS - Được trigger trên tất cả clients thông qua ClientRpc
        public static Action OnGameStarted;
        public static Action OnRoleAssigned;
        public static Action<Role> OnGameEnded;
        public static Action<ulong, ulong> OnPlayerKilled; // killer, victim
        public static Action<ulong> OnBotKilled; // id killer
        public static Action<string, ulong> OnPlayerDeath; //name and  client ID

        //Game Loop Events
        
        //Task Progess Events
        public static Action<ulong, int> OnTaskCompletion; // ClientId, TasksID
        public static Action<int, int> OnTaskProgressUpdated;
        
        //Player Events
        public static Action<ulong> OnClientDisconnect;
        
        #endregion 
    }
}

using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Config
{
    [CreateAssetMenu(menuName = "_GAME/HideAndSeek/GameSettingsConfig", fileName = "GameSettingsConfig")]
    public class GameSettingsConfig : ScriptableObject
    {
        public GameMode gameMode;
        public float gameDuration = 180f; // Duration of the game in seconds
        public int tasksToComplete;      
        public float hiderKillReward;
        public int requiredTasks = 5;
    }
}
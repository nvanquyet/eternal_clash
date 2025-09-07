using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Config
{
    [CreateAssetMenu(menuName = "_GAME/HideAndSeek/GameSettingsConfig", fileName = "GameSettingsConfig")]
    public class GameSettingsConfig : ScriptableObject
    {
        public GameMode gameMode;
        public float gameDuration = 180f; // Duration of the game in seconds
        public int tasksToComplete;      // Số nhiệm vụ cần hoàn thành (Case 1)
        public float seekerHealth;       // Máu người tìm
        public float environmentDamage;  // Sát thương khi bắn vào môi trường
        public float hiderKillReward; 
    }
}
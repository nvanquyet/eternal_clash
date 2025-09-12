using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Config
{
    [System.Serializable]
    public struct SkillData
    {
        public SkillType type;
        public float cooldown;
        public int usesPerGame;
        public float duration;           // Thời gian hiệu ứng
        public float range;              // Phạm vi tác dụng
        public string description;
    }
    
    [CreateAssetMenu(menuName = "_GAME/HideAndSeek/SkillDataConfig", fileName = "SkillDataConfig")]
    public class SkillDataConfig : BaseData<SkillData, SkillType>
    {
        protected override void InitDictionary()
        {
            DataDictionary.Clear();
            foreach (var skill in data)
            {
                if (!DataDictionary.TryAdd(skill.type, skill))
                {
                    Debug.LogWarning($"Duplicate skill type in SkillDataConfig: {skill.type}");
                }
            }
        }
    }
}
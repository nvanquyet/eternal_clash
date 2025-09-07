using System.Collections.Generic;
using System.Linq;
using _GAME.Scripts.HideAndSeek.Config;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.SkillSystem
{
      public class SkillManager : NetworkBehaviour
    {
        [Header("Skill Database")]
        [SerializeField] private SkillData[] skillDatabase;
        
        private Dictionary<SkillType, SkillData> skillDataDict = new Dictionary<SkillType, SkillData>();
        
        private void Awake()
        {
            // Build skill database dictionary
            foreach (var skillData in skillDatabase)
            {
                skillDataDict[skillData.type] = skillData;
            }
        }
        
        public SkillData GetSkillData(SkillType skillType)
        {
            return skillDataDict.ContainsKey(skillType) ? skillDataDict[skillType] : default;
        }
        
        public static BaseSkill CreateSkill(SkillType skillType, GameObject parent)
        {
            BaseSkill skill = null;
            
            switch (skillType)
            {
                case SkillType.FreezeSeeker:
                case SkillType.FreezeHider:
                    skill = parent.AddComponent<FreezeSkill>();
                    break;
                case SkillType.Teleport:
                    skill = parent.AddComponent<TeleportSkill>();
                    break;
                case SkillType.ShapeShift:
                    skill = parent.AddComponent<ShapeShiftSkill>();
                    break;
                case SkillType.Detect:
                    skill = parent.AddComponent<DetectSkill>();
                    break;
            }
            
            return skill;
        }
        
        [ServerRpc(RequireOwnership = false)]
        public void UseSkillServerRpc(ulong playerId, SkillType skillType, Vector3 targetPosition, bool hasTarget)
        {
            var player = FindObjectsOfType<MonoBehaviour>().OfType<IGamePlayer>().FirstOrDefault(p => p.ClientId == playerId);
            if (player == null) return;
            
            var skillComponent = (player as MonoBehaviour)?.GetComponent<BaseSkill>();
            if (skillComponent != null && skillComponent.Type == skillType)
            {
                Vector3? target = hasTarget ? targetPosition : null;
                skillComponent.UseSkill(player, target);
            }
        }
    }
    
}
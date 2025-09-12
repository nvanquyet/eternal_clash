using System;
using _GAME.Scripts.HideAndSeek.Player;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Config
{
    [Serializable]
    public struct PlayerRoleData
    {
        public Role Role;
        public string Description;
        public RolePlayer Prefab;
    }
    [CreateAssetMenu(menuName = "_GAME/PlayerRoleSO", fileName = "PlayerRoleSO")]
    public class PlayerRoleSO : BaseData<PlayerRoleData, Role>
    {
        protected override void InitDictionary()
        {
           DataDictionary.Clear();
              foreach (var role in data)
              {
                if (!DataDictionary.TryAdd(role.Role, role))
                {
                    Debug.LogWarning($"[PlayerRoleSO] Duplicate role detected: {role.Role}. Skipping addition.");
                }
              }
        }
    }
}
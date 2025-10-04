using _GAME.Scripts.Core.Player;
using _GAME.Scripts.HideAndSeek;
using UnityEngine;

namespace _GAME.Scripts.Core.Services
{
    public class RoleService : IRoleService
    {
        private readonly IPlayerRegistry _playerRegistry;

        public RoleService(IPlayerRegistry playerRegistry)
        {
            _playerRegistry = playerRegistry;
        }

        public Role GetRole(ulong clientId)
        {
            return _playerRegistry.GetPlayer(clientId)?.GetRole() ?? Role.None;
        }

        public void AssignRole(ulong clientId, Role role)
        {
            var player = _playerRegistry.GetPlayer(clientId);
            player?.SetRole(role);
        }

        public bool CanAssignRole(ulong clientId, Role role)
        {
            if (role == Role.Seeker)
            {
                int currentSeekers = GetRoleCount(Role.Seeker);
                int totalPlayers = _playerRegistry.GetPlayerCount();
                int maxSeekers = Mathf.Max(1, totalPlayers / 4);
                return currentSeekers < maxSeekers;
            }
            return true;
        }

        public int GetRoleCount(Role role)
        {
            int count = 0;
            foreach (var player in _playerRegistry.GetAllPlayers())
            {
                if (player.GetRole() == role)
                    count++;
            }
            return count;
        }
    }
}
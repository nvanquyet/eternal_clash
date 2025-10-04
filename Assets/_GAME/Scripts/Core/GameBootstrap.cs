using _GAME.Scripts.Core.Services;
using UnityEngine;

namespace _GAME.Scripts.Core
{
    public class GameBootstrap : MonoBehaviour
    {
        void Awake()
        {
            // Register all services
            var playerRegistry = new PlayerRegistry();
            GameServices.Register<IPlayerRegistry>(playerRegistry);
        
            var roleService = new RoleService(playerRegistry);
            GameServices.Register<IRoleService>(roleService);
        
            var combatService = new CombatService(playerRegistry);
            GameServices.Register<ICombatService>(combatService);
        }
    }
}
using _GAME.Scripts.Core.Components;
using _GAME.Scripts.HideAndSeek;

namespace _GAME.Scripts.Core.Player
{
    /// <summary>
    /// Extension methods for convenient component access
    /// </summary>
    public static class PlayerExtensions
    {
        public static bool IsAlive(this IPlayer player)
        {
            //return player.GetPlayerComponent<HealthComponent>()?.IsAlive ?? false;
            return false;
        }

        public static Role GetRole(this IPlayer player)
        {
            return player.GetPlayerComponent<RoleComponent>()?.CurrentRole ?? Role.None;
        }
    }
}
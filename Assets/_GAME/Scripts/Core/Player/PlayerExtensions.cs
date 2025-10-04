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
            return player.GetComponent<HealthComponent>()?.IsAlive ?? false;
        }

        public static Role GetRole(this IPlayer player)
        {
            return player.GetComponent<RoleComponent>()?.CurrentRole ?? Role.None;
        }

        public static float GetHealth(this IPlayer player)
        {
            return player.GetComponent<HealthComponent>()?.CurrentHealth ?? 0f;
        }

        public static void TakeDamage(this IPlayer player, float amount)
        {
            player.GetComponent<HealthComponent>()?.TakeDamage(amount);
        }

        public static void Heal(this IPlayer player, float amount)
        {
            player.GetComponent<HealthComponent>()?.Heal(amount);
        }

        public static void SetRole(this IPlayer player, Role role)
        {
            player.GetComponent<RoleComponent>()?.SetRole(role);
        }
    }
}
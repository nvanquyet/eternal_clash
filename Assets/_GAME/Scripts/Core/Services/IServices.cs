using System;
using System.Collections.Generic;
using _GAME.Scripts.Core.Player;
using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek;

namespace _GAME.Scripts.Core.Services
{
    
    /// <summary>
    /// Player registry service - replaces GameManager player tracking
    /// </summary>
    public interface IPlayerRegistry
    {
        void RegisterPlayer(ModularPlayer player);
        void UnregisterPlayer(ulong clientId);
        ModularPlayer GetPlayer(ulong clientId);
        IEnumerable<ModularPlayer> GetAllPlayers();
        IEnumerable<ModularPlayer> GetPlayersByRole(Role role);
        int GetPlayerCount();
    }

    /// <summary>
    /// Role management service
    /// </summary>
    public interface IRoleService
    {
        Role GetRole(ulong clientId);
        void AssignRole(ulong clientId, Role role);
        bool CanAssignRole(ulong clientId, Role role);
        int GetRoleCount(Role role);
    }

    /// <summary>
    /// Game state service
    /// </summary>
    public interface IGameStateService
    {
        GameState CurrentState { get; }
        void TransitionTo(GameState newState);
        event Action<GameState, GameState> OnStateChanged;
    }

    /// <summary>
    /// Combat service - handles damage calculations
    /// </summary>
    public interface ICombatService
    {
        float CalculateDamage(float baseDamage, DamageType damageType, float defense);
        void ApplyDamage(ulong attackerId, ulong targetId, float damage);
        void RegisterKill(ulong killerId, ulong victimId);
    }

}
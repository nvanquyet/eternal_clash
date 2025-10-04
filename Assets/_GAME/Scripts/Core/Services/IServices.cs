using System;
using System.Collections.Generic;
using _GAME.Scripts.Core.Player;
using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek;
using _GAME.Scripts.Player;

namespace _GAME.Scripts.Core.Services
{
    
    /// <summary>
    /// Player registry service - replaces GameManager player tracking
    /// </summary>
    public interface IPlayerRegistry
    {
        void RegisterPlayer(PlayerController player);
        void UnregisterPlayer(ulong clientId);
        PlayerController GetPlayer(ulong clientId);
        IEnumerable<PlayerController> GetAllPlayers();
        IEnumerable<PlayerController> GetPlayersByRole(Role role);
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
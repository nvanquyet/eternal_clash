using System.Collections.Generic;
using _GAME.Scripts.Core.Player;
using _GAME.Scripts.HideAndSeek;
using UnityEngine;

namespace _GAME.Scripts.Core.Services
{
    public class PlayerRegistry : IPlayerRegistry
    {
        private readonly Dictionary<ulong, ModularPlayer> _players = new();

        public void RegisterPlayer(ModularPlayer player)
        {
            if (player == null) return;
            _players[player.ClientId] = player;
            Debug.Log($"[PlayerRegistry] Registered player {player.ClientId}");
        }

        public void UnregisterPlayer(ulong clientId)
        {
            if (_players.Remove(clientId))
            {
                Debug.Log($"[PlayerRegistry] Unregistered player {clientId}");
            }
        }

        public ModularPlayer GetPlayer(ulong clientId)
        {
            return _players.TryGetValue(clientId, out var player) ? player : null;
        }

        public IEnumerable<ModularPlayer> GetAllPlayers()
        {
            return _players.Values;
        }

        public IEnumerable<ModularPlayer> GetPlayersByRole(Role role)
        {
            foreach (var player in _players.Values)
            {
                if (player.GetRole() == role)
                    yield return player;
            }
        }

        public int GetPlayerCount()
        {
            return _players.Count;
        }
    }

}
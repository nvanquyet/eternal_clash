using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Core.Components
{
    /// <summary>
    /// Base interface for all player components
    /// </summary>
    public interface IPlayerComponent
    {
        void Initialize(IPlayer owner);
        void OnNetworkSpawn();
        void OnNetworkDespawn();
        bool IsActive { get; }
    }

    /// <summary>
    /// Simplified player interface
    /// </summary>
    public interface IPlayer
    {
        ulong ClientId { get; }
        NetworkObject NetObject { get; }
        Transform Transform { get; }
        T GetComponent<T>() where T : IPlayerComponent;
        void BroadcastEvent<T>(T eventData) where T : struct;
    }

}
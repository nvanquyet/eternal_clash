// _GAME/Scripts/Networking/ClientIdentityRegistry.cs
using System.Collections.Generic;
using GAME.Scripts.DesignPattern;
using UnityEngine;

namespace _GAME.Scripts.Networking
{
    public class ClientIdentityRegistry : SingletonDontDestroy<ClientIdentityRegistry>
    {
        private readonly Dictionary<ulong, string> clientToUgs = new();
        private readonly Dictionary<string, ulong> ugsToClient = new();

        public void Register(ulong clientId, string ugsPlayerId)
        {
            if (string.IsNullOrEmpty(ugsPlayerId)) return;
            clientToUgs[clientId] = ugsPlayerId;
            ugsToClient[ugsPlayerId] = clientId;
        }

        public void UnregisterByClient(ulong clientId)
        {
            if (clientToUgs.Remove(clientId, out var ugs))
            {
                ugsToClient.Remove(ugs);
            }
        }

        public bool TryGetClientId(string ugsPlayerId, out ulong clientId)
            => ugsToClient.TryGetValue(ugsPlayerId, out clientId);

        public bool TryGetUgs(ulong clientId, out string ugs)
            => clientToUgs.TryGetValue(clientId, out ugs);
    }
}
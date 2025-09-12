using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Helper
{
    public static class NetHelpers
    {
        // Clamp "dir" into a cone around "forward" with max degrees
        public static Vector3 ClampDirectionInCone(Vector3 forward, Vector3 dir, float maxAngleDeg)
        {
            if (forward == Vector3.zero) return dir.normalized;
            forward = forward.normalized;
            var d = dir.normalized;

            float ang = Vector3.Angle(forward, d);
            if (ang <= maxAngleDeg) return d;

            float t = maxAngleDeg / Mathf.Max(0.001f, ang);
            return Vector3.Slerp(forward, d, t).normalized;
        }

        public static ClientRpcParams ToTarget(ulong clientId) =>
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };

        public static ClientRpcParams ToAllExcept(params ulong[] clientIds) =>
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = NetworkManager.Singleton.ConnectedClientsIds.Except(clientIds).ToArray() } };
    }
}
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.SkillSystem
{
   public class TeleportSkill : BaseSkill
    {
        [Header("Teleport Settings")]
        [SerializeField] private LayerMask groundLayer = 1;
        [SerializeField] private float teleportRadius = 10f;
        [SerializeField] private ParticleSystem teleportEffect;
        [SerializeField] private int maxTeleportAttempts = 10;
        
        protected override void ExecuteSkillEffect(IGamePlayer caster, Vector3? targetPosition)
        {
            Vector3 teleportPosition;
            
            if (targetPosition.HasValue)
            {
                // Teleport to specific position
                teleportPosition = GetValidTeleportPosition(targetPosition.Value);
            }
            else
            {
                // Random teleport around caster
                teleportPosition = GetRandomTeleportPosition(caster.Position);
            }
            
            // Perform teleport
            TeleportPlayer(caster, teleportPosition);
        }
        
        private Vector3 GetValidTeleportPosition(Vector3 targetPos)
        {
            // Raycast down to find ground
            if (Physics.Raycast(targetPos + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 20f, groundLayer))
            {
                return hit.point + Vector3.up * 0.1f;
            }
            
            return targetPos;
        }
        
        private Vector3 GetRandomTeleportPosition(Vector3 centerPos)
        {
            for (int i = 0; i < maxTeleportAttempts; i++)
            {
                // Generate random position in circle
                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * teleportRadius;
                Vector3 randomPos = centerPos + new Vector3(randomCircle.x, 10f, randomCircle.y);
                
                // Check if valid position
                if (Physics.Raycast(randomPos, Vector3.down, out RaycastHit hit, 20f, groundLayer))
                {
                    Vector3 groundPos = hit.point + Vector3.up * 0.1f;
                    
                    // Check if position is not inside walls
                    if (!Physics.CheckSphere(groundPos, 0.5f, ~groundLayer))
                    {
                        return groundPos;
                    }
                }
            }
            
            // Fallback to original position
            return centerPos;
        }
        
        private void TeleportPlayer(IGamePlayer player, Vector3 position)
        {
            TeleportPlayerClientRpc(player.ClientId, position);
        }
        
        [ClientRpc]
        private void TeleportPlayerClientRpc(ulong playerId, Vector3 position)
        {
            var player = FindObjectsOfType<MonoBehaviour>().OfType<IGamePlayer>().FirstOrDefault(p => p.ClientId == playerId);
            if (player != null)
            {
                var playerMono = player as MonoBehaviour;
                if (playerMono != null)
                {
                    // Create teleport out effect
                    if (teleportEffect != null)
                    {
                        Instantiate(teleportEffect, playerMono.transform.position, Quaternion.identity);
                    }
                    
                    // Move player
                    playerMono.transform.position = position;
                    
                    // Create teleport in effect
                    if (teleportEffect != null)
                    {
                        Instantiate(teleportEffect, position, Quaternion.identity);
                    }
                }
            }
        }
        
        protected override void ExecuteSkillVFX(IGamePlayer caster, Vector3? targetPosition)
        {
            // Teleport effects are handled in ClientRpc
        }
    }
    
}
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.SkillSystem
{
     public class FreezeSkill : BaseSkill
    {
        [Header("Freeze Settings")]
        [SerializeField] private LayerMask targetLayers = -1;
        [SerializeField] private ParticleSystem freezeEffect;
        
        private List<IGamePlayer> frozenPlayers = new List<IGamePlayer>();
        
        protected override void ExecuteSkillEffect(IGamePlayer caster, Vector3? targetPosition)
        {
            List<IGamePlayer> targets = new List<IGamePlayer>();
            
            if (skillType == SkillType.FreezeSeeker)
            {
                // Freeze all seekers in range
                targets = FindTargetsInRange(caster.Position, Role.Seeker);
            }
            else if (skillType == SkillType.FreezeHider)
            {
                // Freeze hiders in range or at target position
                Vector3 center = targetPosition ?? caster.Position;
                targets = FindTargetsInRange(center, Role.Hider);
            }
            
            // Apply freeze effect
            foreach (var target in targets)
            {
                FreezePlayer(target);
            }
            
            // Start unfreeze coroutine
            if (targets.Count > 0)
            {
                StartCoroutine(UnfreezePlayersAfterDuration(targets));
            }
        }
        
        private List<IGamePlayer> FindTargetsInRange(Vector3 center, Role targetRole)
        {
            var allPlayers = FindObjectsOfType<MonoBehaviour>().OfType<IGamePlayer>();
            return allPlayers.Where(p => p.Role == targetRole && 
                                        p.IsAlive && 
                                        Vector3.Distance(p.Position, center) <= range)
                           .ToList();
        }
        
        private void FreezePlayer(IGamePlayer player)
        {
            if (frozenPlayers.Contains(player)) return;
            
            frozenPlayers.Add(player);
            
            // Disable movement and actions
            var playerMono = player as MonoBehaviour;
            if (playerMono != null)
            {
                var characterController = playerMono.GetComponent<CharacterController>();
                if (characterController != null)
                {
                    characterController.enabled = false;
                }
                
                // Add freeze visual effect
                if (freezeEffect != null)
                {
                    var effect = Instantiate(freezeEffect, player.Position, Quaternion.identity);
                    effect.transform.SetParent(playerMono.transform);
                }
            }
            
            FreezePlayerClientRpc(player.ClientId, true);
        }
        
        private void UnfreezePlayer(IGamePlayer player)
        {
            if (!frozenPlayers.Contains(player)) return;
            
            frozenPlayers.Remove(player);
            
            // Re-enable movement
            var playerMono = player as MonoBehaviour;
            if (playerMono != null)
            {
                var characterController = playerMono.GetComponent<CharacterController>();
                if (characterController != null)
                {
                    characterController.enabled = true;
                }
            }
            
            FreezePlayerClientRpc(player.ClientId, false);
        }
        
        private IEnumerator UnfreezePlayersAfterDuration(List<IGamePlayer> players)
        {
            yield return new WaitForSeconds(duration);
            
            foreach (var player in players)
            {
                UnfreezePlayer(player);
            }
        }
        
        [ClientRpc]
        private void FreezePlayerClientRpc(ulong playerId, bool frozen)
        {
            var player = FindObjectsOfType<MonoBehaviour>().OfType<IGamePlayer>().FirstOrDefault(p => p.ClientId == playerId);
            if (player != null)
            {
                var playerMono = player as MonoBehaviour;
                // Add visual freeze effects here
                // Change material color, add ice particles, etc.
            }
        }
        
        protected override void ExecuteSkillVFX(IGamePlayer caster, Vector3? targetPosition)
        {
            // Create freeze visual effects
            Vector3 effectPosition = targetPosition ?? caster.Position;
            
            // Add particle effects, sound, etc.
        }
    }
    
}
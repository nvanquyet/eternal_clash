using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.SkillSystem
{
      public class ShapeShiftSkill : BaseSkill
    {
        [Header("Shape Shift Settings")]
        [SerializeField] private GameObject[] npcPrefabs;
        [SerializeField] private ParticleSystem transformEffect;
        
        private Dictionary<ulong, GameObject> originalModels = new Dictionary<ulong, GameObject>();
        private Dictionary<ulong, GameObject> disguisedModels = new Dictionary<ulong, GameObject>();
        
        protected override void ExecuteSkillEffect(IGamePlayer caster, Vector3? targetPosition)
        {
            if (npcPrefabs.Length == 0) return;
            
            // Choose random NPC appearance
            var randomNPC = npcPrefabs[UnityEngine.Random.Range(0, npcPrefabs.Length)];
            
            ShapeShiftPlayer(caster, randomNPC);
        }
        
        private void ShapeShiftPlayer(IGamePlayer player, GameObject npcPrefab)
        {
            ShapeShiftPlayerClientRpc(player.ClientId, Array.IndexOf(npcPrefabs, npcPrefab));
            
            // Start revert coroutine
            StartCoroutine(RevertShapeShiftAfterDuration(player));
        }
        
        private IEnumerator RevertShapeShiftAfterDuration(IGamePlayer player)
        {
            yield return new WaitForSeconds(duration);
            RevertShapeShift(player);
        }
        
        private void RevertShapeShift(IGamePlayer player)
        {
            RevertShapeShiftClientRpc(player.ClientId);
        }
        
        [ClientRpc]
        private void ShapeShiftPlayerClientRpc(ulong playerId, int npcIndex)
        {
            var player = FindObjectsOfType<MonoBehaviour>().OfType<IGamePlayer>().FirstOrDefault(p => p.ClientId == playerId);
            if (player == null || npcIndex < 0 || npcIndex >= npcPrefabs.Length) return;
            
            var playerMono = player as MonoBehaviour;
            if (playerMono == null) return;
            
            // Store original model
            var originalRenderer = playerMono.GetComponent<Renderer>();
            if (originalRenderer != null)
            {
                originalModels[playerId] = originalRenderer.gameObject;
                originalRenderer.enabled = false;
            }
            
            // Create disguised model
            var npcPrefab = npcPrefabs[npcIndex];
            var disguisedModel = Instantiate(npcPrefab, playerMono.transform.position, playerMono.transform.rotation, playerMono.transform);
            disguisedModels[playerId] = disguisedModel;
            
            // Transform effect
            if (transformEffect != null)
            {
                Instantiate(transformEffect, playerMono.transform.position, Quaternion.identity);
            }
        }
        
        [ClientRpc]
        private void RevertShapeShiftClientRpc(ulong playerId)
        {
            var player = FindObjectsOfType<MonoBehaviour>().OfType<IGamePlayer>().FirstOrDefault(p => p.ClientId == playerId);
            if (player == null) return;
            
            // Restore original model
            if (originalModels.ContainsKey(playerId))
            {
                var originalRenderer = originalModels[playerId].GetComponent<Renderer>();
                if (originalRenderer != null)
                {
                    originalRenderer.enabled = true;
                }
                originalModels.Remove(playerId);
            }
            
            // Remove disguised model
            if (disguisedModels.ContainsKey(playerId))
            {
                Destroy(disguisedModels[playerId]);
                disguisedModels.Remove(playerId);
            }
            
            // Transform effect
            if (transformEffect != null)
            {
                var playerMono = player as MonoBehaviour;
                if (playerMono != null)
                {
                    Instantiate(transformEffect, playerMono.transform.position, Quaternion.identity);
                }
            }
        }
        
        protected override void ExecuteSkillVFX(IGamePlayer caster, Vector3? targetPosition)
        {
            // Shape shift effects are handled in ClientRpc
        }
    }
    
}
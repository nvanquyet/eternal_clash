using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.SkillSystem
{
      public class DetectSkill : BaseSkill
    {
        [Header("Detect Settings")]
        [SerializeField] private Material highlightMaterial;
        [SerializeField] private LayerMask hiderLayers = -1;
        [SerializeField] private ParticleSystem detectEffect;
        
        private Dictionary<ulong, Material[]> originalMaterials = new Dictionary<ulong, Material[]>();
        
        protected override void ExecuteSkillEffect(IGamePlayer caster, Vector3? targetPosition)
        {
            // Find all hiders in range
            var hidersInRange = FindHidersInRange(caster.Position);
            
            // Highlight hiders
            foreach (var hider in hidersInRange)
            {
                HighlightHider(hider);
            }
            
            // Start unhighlight coroutine
            if (hidersInRange.Count > 0)
            {
                StartCoroutine(UnhighlightHidersAfterDuration(hidersInRange));
            }
        }
        
        private List<IGamePlayer> FindHidersInRange(Vector3 center)
        {
            var allPlayers = FindObjectsOfType<MonoBehaviour>().OfType<IGamePlayer>();
            return allPlayers.Where(p => p.Role == PlayerRole.Hider && 
                                        p.IsAlive && 
                                        Vector3.Distance(p.Position, center) <= range)
                           .ToList();
        }
        
        private void HighlightHider(IGamePlayer hider)
        {
            HighlightHiderClientRpc(hider.ClientId, true);
        }
        
        private void UnhighlightHider(IGamePlayer hider)
        {
            HighlightHiderClientRpc(hider.ClientId, false);
        }
        
        private IEnumerator UnhighlightHidersAfterDuration(List<IGamePlayer> hiders)
        {
            yield return new WaitForSeconds(duration);
            
            foreach (var hider in hiders)
            {
                UnhighlightHider(hider);
            }
        }
        
        [ClientRpc]
        private void HighlightHiderClientRpc(ulong hiderId, bool highlight)
        {
            var hider = FindObjectsOfType<MonoBehaviour>().OfType<IGamePlayer>().FirstOrDefault(p => p.ClientId == hiderId);
            if (hider == null) return;
            
            var hiderMono = hider as MonoBehaviour;
            if (hiderMono == null) return;
            
            var renderers = hiderMono.GetComponentsInChildren<Renderer>();
            
            if (highlight)
            {
                // Store original materials
                var allMaterials = new List<Material>();
                foreach (var renderer in renderers)
                {
                    allMaterials.AddRange(renderer.materials);
                    
                    // Apply highlight material
                    var highlightMaterials = new Material[renderer.materials.Length];
                    for (int i = 0; i < highlightMaterials.Length; i++)
                    {
                        highlightMaterials[i] = highlightMaterial;
                    }
                    renderer.materials = highlightMaterials;
                }
                originalMaterials[hiderId] = allMaterials.ToArray();
                
                // Add detect effect
                if (detectEffect != null)
                {
                    var effect = Instantiate(detectEffect, hiderMono.transform.position, Quaternion.identity);
                    effect.transform.SetParent(hiderMono.transform);
                }
            }
            else
            {
                // Restore original materials
                if (originalMaterials.ContainsKey(hiderId))
                {
                    int materialIndex = 0;
                    foreach (var renderer in renderers)
                    {
                        var originalRendererMaterials = new Material[renderer.materials.Length];
                        for (int i = 0; i < originalRendererMaterials.Length; i++)
                        {
                            if (materialIndex < originalMaterials[hiderId].Length)
                            {
                                originalRendererMaterials[i] = originalMaterials[hiderId][materialIndex];
                                materialIndex++;
                            }
                        }
                        renderer.materials = originalRendererMaterials;
                    }
                    originalMaterials.Remove(hiderId);
                }
            }
        }
        
        protected override void ExecuteSkillVFX(IGamePlayer caster, Vector3? targetPosition)
        {
            // Create detection wave effect
            if (detectEffect != null)
            {
                var effect = Instantiate(detectEffect, caster.Position, Quaternion.identity);
                
                // Scale effect to match range
                effect.transform.localScale = Vector3.one * range;
            }
        }
    }
    
}
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using HideAndSeekGame.Core;
using HideAndSeekGame.Players;

namespace HideAndSeekGame.Skills
{
    #region Base Skill Class
    
    public abstract class BaseSkill : NetworkBehaviour, ISkill
    {
        [Header("Skill Base Settings")]
        [SerializeField] protected SkillType skillType;
        [SerializeField] protected float cooldown;
        [SerializeField] protected int usesPerGame;
        [SerializeField] protected float duration;
        [SerializeField] protected float range;
        
        protected int remainingUses;
        protected float nextUseTime;
        protected bool isActive;
        
        // Network variables
        protected NetworkVariable<int> networkRemainingUses = new NetworkVariable<int>();
        protected NetworkVariable<float> networkNextUseTime = new NetworkVariable<float>();
        
        // ISkill implementation
        public SkillType Type => skillType;
        public float Cooldown => cooldown;
        public int UsesPerGame => usesPerGame;
        public int RemainingUses => networkRemainingUses.Value;
        public bool CanUse => networkRemainingUses.Value > 0 && Time.time >= networkNextUseTime.Value && !isActive;
        
        public static event Action<SkillType, IGamePlayer, bool> OnSkillUsed;
        public static event Action<SkillType, float> OnSkillCooldownStarted;
        
        public virtual void Initialize(SkillType type, SkillData data)
        {
            skillType = type;
            cooldown = data.cooldown;
            usesPerGame = data.usesPerGame;
            duration = data.duration;
            range = data.range;
            
            remainingUses = usesPerGame;
            
            if (IsServer)
            {
                networkRemainingUses.Value = usesPerGame;
                networkNextUseTime.Value = 0f;
            }
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            networkRemainingUses.OnValueChanged += OnRemainingUsesChanged;
            networkNextUseTime.OnValueChanged += OnNextUseTimeChanged;
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            networkRemainingUses.OnValueChanged -= OnRemainingUsesChanged;
            networkNextUseTime.OnValueChanged -= OnNextUseTimeChanged;
        }
        
        public virtual void UseSkill(IGamePlayer caster, Vector3? targetPosition = null)
        {
            if (!CanUse) return;
            
            if (IsServer)
            {
                // Consume use
                networkRemainingUses.Value--;
                StartCooldown();
                
                // Execute skill effect
                ExecuteSkillEffect(caster, targetPosition);
                
                // Notify clients
                OnSkillUsedClientRpc(caster.ClientId, targetPosition ?? Vector3.zero, targetPosition.HasValue);
            }
        }
        
        public virtual void StartCooldown()
        {
            if (IsServer)
            {
                networkNextUseTime.Value = Time.time + cooldown;
                OnSkillCooldownStarted?.Invoke(skillType, cooldown);
            }
        }
        
        protected abstract void ExecuteSkillEffect(IGamePlayer caster, Vector3? targetPosition);
        
        [ClientRpc]
        protected virtual void OnSkillUsedClientRpc(ulong casterId, Vector3 targetPosition, bool hasTarget)
        {
            var caster = FindObjectsOfType<MonoBehaviour>().OfType<IGamePlayer>().FirstOrDefault(p => p.ClientId == casterId);
            if (caster != null)
            {
                OnSkillUsed?.Invoke(skillType, caster, true);
                Vector3? target = hasTarget ? targetPosition : null;
                ExecuteSkillVFX(caster, target);
            }
        }
        
        protected virtual void ExecuteSkillVFX(IGamePlayer caster, Vector3? targetPosition)
        {
            // Override in derived classes for visual/audio effects
        }
        
        private void OnRemainingUsesChanged(int previousValue, int newValue)
        {
            remainingUses = newValue;
        }
        
        private void OnNextUseTimeChanged(float previousValue, float newValue)
        {
            nextUseTime = newValue;
        }
    }
    
    #endregion
    
    #region Freeze Skill
    
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
                targets = FindTargetsInRange(caster.Position, PlayerRole.Seeker);
            }
            else if (skillType == SkillType.FreezeHider)
            {
                // Freeze hiders in range or at target position
                Vector3 center = targetPosition ?? caster.Position;
                targets = FindTargetsInRange(center, PlayerRole.Hider);
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
        
        private List<IGamePlayer> FindTargetsInRange(Vector3 center, PlayerRole targetRole)
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
    
    #endregion
    
    #region Teleport Skill
    
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
    
    #endregion
    
    #region Shape Shift Skill
    
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
    
    #endregion
    
    #region Detect Skill
    
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
    
    #endregion
    
    #region Skill Manager
    
    public class SkillManager : NetworkBehaviour
    {
        [Header("Skill Database")]
        [SerializeField] private SkillData[] skillDatabase;
        
        private Dictionary<SkillType, SkillData> skillDataDict = new Dictionary<SkillType, SkillData>();
        
        private void Awake()
        {
            // Build skill database dictionary
            foreach (var skillData in skillDatabase)
            {
                skillDataDict[skillData.type] = skillData;
            }
        }
        
        public SkillData GetSkillData(SkillType skillType)
        {
            return skillDataDict.ContainsKey(skillType) ? skillDataDict[skillType] : default;
        }
        
        public static BaseSkill CreateSkill(SkillType skillType, GameObject parent)
        {
            BaseSkill skill = null;
            
            switch (skillType)
            {
                case SkillType.FreezeSeeker:
                case SkillType.FreezeHider:
                    skill = parent.AddComponent<FreezeSkill>();
                    break;
                case SkillType.Teleport:
                    skill = parent.AddComponent<TeleportSkill>();
                    break;
                case SkillType.ShapeShift:
                    skill = parent.AddComponent<ShapeShiftSkill>();
                    break;
                case SkillType.Detect:
                    skill = parent.AddComponent<DetectSkill>();
                    break;
            }
            
            return skill;
        }
        
        [ServerRpc(RequireOwnership = false)]
        public void UseSkillServerRpc(ulong playerId, SkillType skillType, Vector3 targetPosition, bool hasTarget)
        {
            var player = FindObjectsOfType<MonoBehaviour>().OfType<IGamePlayer>().FirstOrDefault(p => p.ClientId == playerId);
            if (player == null) return;
            
            var skillComponent = (player as MonoBehaviour)?.GetComponent<BaseSkill>();
            if (skillComponent != null && skillComponent.Type == skillType)
            {
                Vector3? target = hasTarget ? targetPosition : null;
                skillComponent.UseSkill(player, target);
            }
        }
    }
    
    #endregion
}
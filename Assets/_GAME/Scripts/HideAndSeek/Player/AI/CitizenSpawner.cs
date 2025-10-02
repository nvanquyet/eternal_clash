
// ============================================================================
// FILE 3: CitizenSpawner.cs - Spawns and manages citizens
// ============================================================================

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace _GAME.Scripts.HideAndSeek.Player.AI
{
    public class CitizenSpawner : NetworkBehaviour
    {
        [Header("=== Spawn Settings ===")]
        [SerializeField] private NetworkBehaviour citizenPrefab;
        [SerializeField] private Transform spawnCenter;
        [SerializeField] private Transform[] allSpawnPoints;
        [SerializeField] private int citizenCount = 10;
        [SerializeField] private bool spawnInCircleShape = false;
        [SerializeField] private float spawnRadius;
        
        [FormerlySerializedAs("poiManager")]
        [Header("=== References ===")]
        [SerializeField] private PointManager pointManager;

        private List<Citizen_AIScript> spawnedCitizens = new List<Citizen_AIScript>();

        private void Start()
        {
            if(!IsServer) return;
            spawnCenter??= transform;
            SpawnCitizens();
        }


        [ContextMenu("Test Spawn Citizens")]
        private void SpawnCitizens()
        {
            Debug.Log($"[CitizenSpawner] Spawning {citizenCount} citizens...");
            if(!IsServer) return;
            for (int i = 0; i < citizenCount; i++)
            {
                Vector3 randomPos;
                if (spawnInCircleShape)
                {
                    randomPos = spawnCenter.position + Random.insideUnitSphere * spawnRadius;
                }
                else
                {
                    var randomPosIndex = Random.Range(0, allSpawnPoints.Length);
                    randomPos = allSpawnPoints[randomPosIndex].position;
                }
               
                randomPos.y = spawnCenter.position.y;

                UnityEngine.AI.NavMeshHit hit;
                if (UnityEngine.AI.NavMesh.SamplePosition(randomPos, out hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    var citizen = Instantiate(citizenPrefab, hit.position, Quaternion.identity, transform);
                    citizen.name = $"Citizen_{i:00}";

                    Citizen_AIScript script = citizen.GetComponent<Citizen_AIScript>();
                    if (script != null)
                    {
                        spawnedCitizens.Add(script);
                    }
                    
                    citizen.NetworkObject.Spawn(true);

                    Debug.Log($"[CitizenSpawner] Spawned {citizen.name} at {hit.position}");
                }
                else
                {
                    Debug.LogWarning($"[CitizenSpawner] Failed to find NavMesh position for citizen {i}");
                }
            }

            Debug.Log($"[CitizenSpawner] Spawned {spawnedCitizens.Count}/{citizenCount} citizens");
        }

        [ContextMenu("Kill Random Citizen")]
        public void KillRandomCitizen()
        {
            var aliveCitizens = spawnedCitizens.FindAll(c => c != null && !c.IsDead);
            if (aliveCitizens.Count > 0)
            {
                var victim = aliveCitizens[Random.Range(0, aliveCitizens.Count)];
                victim.Kill();
                Debug.Log($"[CitizenSpawner] Killed {victim.CitizenName}");
            }
        }

        [ContextMenu("Kill All Citizens")]
        public void KillAllCitizens()
        {
            foreach (var citizen in spawnedCitizens)
            {
                if (citizen != null && !citizen.IsDead)
                {
                    citizen.Kill();
                }
            }
            Debug.Log("[CitizenSpawner] Killed all citizens");
        }

        private void OnDrawGizmos()
        {
            if (spawnCenter == null || !spawnInCircleShape) return;

            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(spawnCenter.position, spawnRadius);
        }
    }
}
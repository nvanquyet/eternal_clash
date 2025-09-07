using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Object
{
      public class DisguiseObjectManager : NetworkBehaviour
    {
        [Header("Object Spawning")]
        [SerializeField] private GameObject[] objectPrefabs;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private int objectsPerType = 3;
        
        [Header("Object Swapping")]
        [SerializeField] private float swapInterval = 30f; // Force swap every 30 seconds
            
        private List<BaseDisguiseObject> spawnedObjects = new List<BaseDisguiseObject>();
        private List<IHider> occupiedHiders = new List<IHider>();
        
        public static event Action OnForceObjectSwap;
        
        private void Start()
        {
            if (IsServer)
            {
                SpawnDisguiseObjects();
                InvokeRepeating(nameof(ForceObjectSwapServerRpc), swapInterval, swapInterval);
            }
        } 
        
        private void SpawnDisguiseObjects()
        {
            var availableSpawnPoints = new List<Transform>(spawnPoints);
            
            foreach (var prefab in objectPrefabs)
            {
                for (int i = 0; i < objectsPerType && availableSpawnPoints.Count > 0; i++)
                {
                    int randomIndex = UnityEngine.Random.Range(0, availableSpawnPoints.Count);
                    var spawnPoint = availableSpawnPoints[randomIndex];
                    availableSpawnPoints.RemoveAt(randomIndex);
                    
                    var obj = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
                    var networkObj = obj.GetComponent<NetworkObject>();
                    if (networkObj != null)
                    {
                        networkObj.Spawn();
                    }
                    
                    var disguiseObj = obj.GetComponent<BaseDisguiseObject>();
                    if (disguiseObj != null)
                    {
                        spawnedObjects.Add(disguiseObj);
                    }
                }
            }
        }
        
        [ServerRpc(RequireOwnership = false)]
        public void ForceObjectSwapServerRpc()
        {
            // Get all occupied hiders
            occupiedHiders.Clear();
            foreach (var obj in spawnedObjects)
            {
                if (obj.IsOccupied && obj.CurrentHider != null)
                {
                    occupiedHiders.Add(obj.CurrentHider);
                    obj.ReleaseObject();
                }
            }
            
            // Notify clients about forced swap
            ForceObjectSwapClientRpc();
            
            // Wait a moment then reassign random objects
            StartCoroutine(ReassignObjects());
        }
        
        private System.Collections.IEnumerator ReassignObjects()
        {
            yield return new WaitForSeconds(1f);
            
            foreach (var hider in occupiedHiders)
            {
                var availableObjects = spawnedObjects.Where(o => !o.IsOccupied && o.IsAlive).ToList();
                if (availableObjects.Count > 0)
                {
                    var randomObject = availableObjects[UnityEngine.Random.Range(0, availableObjects.Count)];
                    randomObject.OccupyObject(hider);
                }
            }
        }
        
        [ClientRpc]
        private void ForceObjectSwapClientRpc()
        {
            OnForceObjectSwap?.Invoke();
        }
    }
    
}
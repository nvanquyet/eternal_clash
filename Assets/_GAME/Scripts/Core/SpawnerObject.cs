using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Core
{
    public class SpawnerObject : NetworkBehaviour
    {
        [SerializeField] private NetworkObject[] prefabToSpawn;
        [SerializeField] private Transform[] spawnPoints;
        
        private Coroutine _spawnRoutine;
        
#if UNITY_EDITOR
        private void OnValidate()
        {
            spawnPoints ??= GetComponentsInChildren<Transform>();
        }
#endif

        [ContextMenu("Refresh Spawn Points")]
        private void RefreshSpawnPoint()
        {
            spawnPoints = GetComponentsInChildren<Transform>();
        }
        
        
        public void SpawnObject(Action callback = null)
        {
            if (!IsServer) return;
            
            if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
            
            if (prefabToSpawn == null || prefabToSpawn.Length == 0)
            {
                Debug.LogWarning("[SpawnerObject] No prefabs to spawn!");
                callback?.Invoke();
                return;
            }
            
            _spawnRoutine = StartCoroutine(IESpawnObject(callback));
        }
        
        private IEnumerator IESpawnObject(Action callback)
        {
            int spawnPointLength = spawnPoints.Length;
            int prefabLength = prefabToSpawn.Length;

            while (spawnPointLength > 0)
            {
                var randomObject = prefabToSpawn[UnityEngine.Random.Range(0, prefabLength)];
                var netObject = Instantiate(randomObject, spawnPoints[spawnPointLength - 1].position, Quaternion.identity);
                netObject.Spawn(true);
                Debug.Log($"[SpawnerObject] Spawned object {netObject.name} at {spawnPoints[spawnPointLength - 1].position}");
                spawnPointLength--;
                yield return null;
            }
            callback?.Invoke();
            _spawnRoutine = null;
            Debug.Log($"[SpawnerObject] Finished spawning objects.");
        }
    }
}

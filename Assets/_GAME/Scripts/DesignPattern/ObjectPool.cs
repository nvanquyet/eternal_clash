using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.DesignPattern
{
    /// <summary>
    /// Interface for poolable objects
    /// </summary>
    public interface IPoolable
    {
        void OnSpawned();
        void OnReturned();
        void ReturnToPool();
    }
    
    /// <summary>
    /// Generic Object Pool base class
    /// </summary>
    public abstract class ObjectPool<T> : NetworkBehaviour where T : NetworkBehaviour
    {
        [Header("Pool Settings")]
        [SerializeField] protected T prefab;
        [SerializeField] protected int initialPoolSize = 10;
        [SerializeField] protected int maxPoolSize = 50;
        [SerializeField] protected bool autoExpand = true;

        private Queue<T> availableObjects = new Queue<T>();
        private HashSet<T> activeObjects = new HashSet<T>();
        
        public int AvailableCount => availableObjects.Count;
        public int ActiveCount => activeObjects.Count;
        
        protected virtual void Awake()
        {
            InitializePool();
        }
        
        protected virtual void InitializePool()
        {
            for (int i = 0; i < initialPoolSize; i++)
            {
                T obj = CreateNewObject();
                obj.gameObject.SetActive(false);
                availableObjects.Enqueue(obj);
            }
        }
        
        protected virtual T CreateNewObject()
        {
            T obj = Instantiate(prefab, transform);
            OnObjectCreated(obj);
            return obj;
        }
        
        public virtual T Get()
        {
            T obj;
            
            if (availableObjects.Count > 0)
            {
                obj = availableObjects.Dequeue();
            }
            else if (autoExpand && activeObjects.Count < maxPoolSize)
            {
                obj = CreateNewObject();
            }
            else
            {
                return null; // Pool exhausted
            }
            
            activeObjects.Add(obj);
            obj.gameObject.SetActive(true);
            if(obj.TryGetComponent<NetworkObject>(out var networkObject)) networkObject.Spawn();
            OnObjectRetrieved(obj);
            return obj;
        }

        public virtual void Return(T obj)
        {
            if (obj == null || !activeObjects.Contains(obj)) return;
            
            activeObjects.Remove(obj);
            availableObjects.Enqueue(obj);
            if(obj.TryGetComponent<NetworkObject>(out var networkObject)) networkObject.Despawn();
            obj.gameObject.SetActive(false);
            OnObjectReturned(obj);
        }
        
        public virtual void ReturnAll()
        {
            var activeList = new List<T>(activeObjects);
            foreach (var obj in activeList)
            {
                Return(obj);
            }
        }
        
        protected abstract void OnObjectCreated(T obj);
        protected abstract void OnObjectRetrieved(T obj);
        protected abstract void OnObjectReturned(T obj);
    }
}

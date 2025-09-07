using System.Collections.Generic;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Config
{
    public abstract class BaseData<T, V> : ScriptableObject 
    {
        [SerializeField] protected T[] data;
        
        protected Dictionary<V, T> dataDictionary;

        protected abstract void InitDictionary();
        
        public T GetData(V key)
        {
            if (dataDictionary == null)
            {
                InitDictionary();
            }
            
            if (dataDictionary != null && dataDictionary.TryGetValue(key, out T value))
            {
                return value;
            }

            Debug.LogWarning($"Key {key} not found in {typeof(T).Name} data.");
            return default;
        }

        public T[] GetAllData()
        {
            return data;
        }
    }
}
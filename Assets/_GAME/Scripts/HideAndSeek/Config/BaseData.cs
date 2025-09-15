using System.Collections.Generic;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Config
{
    public abstract class BaseData<T, TV> : ScriptableObject 
    {
        [SerializeField] protected T[] data;
        
        protected Dictionary<TV, T> DataDictionary = new();

        protected abstract void InitDictionary();
        
        public T GetData(TV key)
        {
            if (DataDictionary == null || DataDictionary.Count == 0)
            {
                InitDictionary();
            }
            
            if (DataDictionary != null && DataDictionary.TryGetValue(key, out T value))
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
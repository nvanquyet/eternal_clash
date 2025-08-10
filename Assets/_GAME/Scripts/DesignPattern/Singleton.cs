using System;
using UnityEngine;

namespace GAME.Scripts.DesignPattern
{
    /// <summary>
    /// Base class for MonoBehaviour-based singletons.
    /// Inherit from this class to make a component a singleton.
    /// </summary>
    /// <typeparam name="T">Type of the singleton</typeparam>
    public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T _instance;
        private static readonly object _lock = new object();
        private static bool _isShuttingDown = false;
        
        protected virtual bool DontDestroyOnLoad => false;
    
        /// <summary>
        /// Singleton instance access.
        /// </summary> 
        public static T Instance
        {
            get
            {
                if (_isShuttingDown)
                { 
                    Debug.LogWarning($"[Singleton] Instance '{typeof(T)}' already destroyed. Returning null.");
                    return null;
                }
    
                lock (_lock)
                {
                    if (_instance != null) 
                        return _instance;
                    
                    // Try to find existing instance in scene
                    _instance = FindAnyObjectByType<T>();

                    if (_instance != null) 
                        return _instance;
                    
                    // Create new instance if none exists
                    var singletonObject = new GameObject();
                    _instance = singletonObject.AddComponent<T>();
                    singletonObject.name = $"[Singleton] {typeof(T)}";

                    return _instance;
                }
            }
        }
        
        /// <summary>
        /// Check if instance exists without creating one
        /// </summary>
        public static bool HasInstance => _instance != null && !_isShuttingDown;
        
        protected virtual void Awake()
        {
            // Handle multiple instances
            if (_instance == null)
            {
                _instance = this as T;
                
                if (DontDestroyOnLoad)
                {
                    DontDestroyOnLoad(gameObject);
                }
                
                OnAwake();
            }
            else if (_instance != this)
            {
                Debug.LogWarning($"[Singleton] Instance of {typeof(T)} already exists. Destroying duplicate.");
                Destroy(gameObject);
            }
        }
        
        /// <summary>
        /// Override this instead of Awake()
        /// </summary>
        protected virtual void OnAwake() { }
        
        protected virtual void OnApplicationQuit()
        {
            _isShuttingDown = true;
        }
        
        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                _isShuttingDown = true;
            }
        }
    }
    
    /// <summary>
    /// Singleton that persists across scenes
    /// </summary>
    public abstract class SingletonDontDestroy<T> : Singleton<T> where T : MonoBehaviour
    {
        protected override bool DontDestroyOnLoad => true;
    }
}
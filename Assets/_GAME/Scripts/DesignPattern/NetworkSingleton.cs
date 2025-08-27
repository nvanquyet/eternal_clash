using System;
using UnityEngine;
using Unity.Netcode;

namespace GAME.Scripts.DesignPattern
{
    /// <summary>
    /// Base class for NetworkBehaviour-based singletons.
    /// Provides singleton access with network spawning capabilities.
    /// </summary>
    /// <typeparam name="T">Type of the network singleton</typeparam>
    public abstract class NetworkSingleton<T> : NetworkBehaviour where T : NetworkBehaviour
    {
        private static T _instance;
        private static readonly object _lock = new object();
        private static bool _isShuttingDown = false;
        
        protected virtual bool DontDestroyOnLoad => false;
        protected virtual bool AutoSpawnOnServer => true;
        
        /// <summary>
        /// Singleton instance access.
        /// Will return null if not spawned on network.
        /// </summary>
        public static T Instance
        {
            get
            {
                if (_isShuttingDown)
                {
                    Debug.LogWarning($"[NetworkSingleton] Instance '{typeof(T)}' already destroyed. Returning null.");
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
                    
                    // For NetworkBehaviour, we cannot auto-create like regular Singleton
                    // because it needs to be properly spawned through NetworkManager
                    Debug.LogWarning($"[NetworkSingleton] No spawned instance of {typeof(T)} found. " +
                                   "Make sure to spawn it through NetworkManager.");
                    return null;
                }
            }
        }
        
        /// <summary>
        /// Check if instance exists without creating one
        /// </summary>
        public static bool HasInstance => _instance != null && !_isShuttingDown;
        
        /// <summary>
        /// Force spawn the singleton on server (if not exists)
        /// </summary>
        public static void SpawnInstance()
        {
            if (HasInstance) return;
            
            if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost)
            {
                Debug.LogError($"[NetworkSingleton] Cannot spawn {typeof(T)} - not running as server/host.");
                return;
            }
            
            // Create GameObject with NetworkObject component
            var singletonObject = new GameObject($"[NetworkSingleton] {typeof(T)}");
            var networkObject = singletonObject.AddComponent<NetworkObject>();
            var singleton = singletonObject.AddComponent<T>();
            
            // Spawn on network
            networkObject.Spawn();
        }
        
        /// <summary>
        /// Spawn from prefab (recommended approach)
        /// </summary>
        /// <param name="prefab">Prefab containing NetworkObject and this component</param>
        public static void SpawnInstance(GameObject prefab)
        {
            if (HasInstance) return;
            
            if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost)
            {
                Debug.LogError($"[NetworkSingleton] Cannot spawn {typeof(T)} - not running as server/host.");
                return;
            }
            
            if (prefab.GetComponent<T>() == null)
            {
                Debug.LogError($"[NetworkSingleton] Prefab does not contain component {typeof(T)}");
                return;
            }
            
            var instance = Instantiate(prefab);
            var networkObject = instance.GetComponent<NetworkObject>();
            networkObject.Spawn();
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            // Handle multiple instances
            if (_instance == null)
            {
                _instance = this as T;
                
                if (DontDestroyOnLoad)
                {
                    DontDestroyOnLoad(gameObject);
                }
                
                OnNetworkAwake();
            }
            else if (_instance != this)
            {
                Debug.LogWarning($"[NetworkSingleton] Instance of {typeof(T)} already exists. Destroying duplicate.");
                
                if (IsServer)
                {
                    NetworkObject.Despawn();
                }
            }
        }
        
        public override void OnNetworkDespawn()
        {
            if (_instance == this)
            {
                OnBeforeDestroy();
                _instance = null;
            }
            
            base.OnNetworkDespawn();
        }
        
        /// <summary>
        /// Override this instead of OnNetworkSpawn()
        /// Called only on the singleton instance
        /// </summary>
        protected virtual void OnNetworkAwake() { }
        
        /// <summary>
        /// Called before the singleton instance is destroyed
        /// </summary>
        protected virtual void OnBeforeDestroy() { }
        
        protected virtual void OnApplicationQuit()
        {
            _isShuttingDown = true;
        }
        
        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                _isShuttingDown = true;
            }
        }
        
        /// <summary>
        /// Safe way to access instance - returns null if not available
        /// </summary>
        /// <param name="action">Action to perform on instance if available</param>
        public static void DoIfExists(System.Action<T> action)
        {
            if (HasInstance && Instance != null)
            {
                action(Instance);
            }
        }
        
        /// <summary>
        /// Check if this singleton is the server authority
        /// </summary>
        protected bool IsServerAuthority => IsServer;
        
        /// <summary>
        /// Check if this singleton is owned by local client
        /// </summary>
        protected bool IsLocalOwner => IsOwner;
    }
    
    /// <summary>
    /// NetworkSingleton that persists across scenes
    /// </summary>
    public abstract class NetworkSingletonDontDestroy<T> : NetworkSingleton<T> where T : NetworkBehaviour
    {
        protected override bool DontDestroyOnLoad => true;
    }
    
    /// <summary>
    /// Helper class for managing NetworkSingleton lifecycle
    /// </summary>
    public static class NetworkSingletonManager
    {
        /// <summary>
        /// Auto-spawn all NetworkSingletons marked with AutoSpawn
        /// Call this when server/host starts
        /// </summary>
        public static void AutoSpawnSingletons()
        {
            if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost)
                return;
            
            // Find all types that inherit from NetworkSingleton
            var singletonTypes = GetAllNetworkSingletonTypes();
            
            foreach (var type in singletonTypes)
            {
                TryAutoSpawn(type);
            }
        }
        
        private static System.Type[] GetAllNetworkSingletonTypes()
        {
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            var singletonTypes = new System.Collections.Generic.List<System.Type>();
            
            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    if (IsNetworkSingletonType(type))
                    {
                        singletonTypes.Add(type);
                    }
                }
            }
            
            return singletonTypes.ToArray();
        }
        
        private static bool IsNetworkSingletonType(System.Type type)
        {
            if (type.IsAbstract || type.IsInterface)
                return false;
            
            var baseType = type.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType && 
                    (baseType.GetGenericTypeDefinition() == typeof(NetworkSingleton<>) ||
                     baseType.GetGenericTypeDefinition() == typeof(NetworkSingletonDontDestroy<>)))
                {
                    return true;
                }
                baseType = baseType.BaseType;
            }
            
            return false;
        }
        
        private static void TryAutoSpawn(System.Type type)
        {
            try
            {
                // Use reflection to check AutoSpawnOnServer property
                var instance = System.Activator.CreateInstance(type);
                var property = type.GetProperty("AutoSpawnOnServer", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (property != null && (bool)property.GetValue(instance))
                {
                    // Try to spawn using reflection
                    var spawnMethod = type.GetMethod("SpawnInstance", System.Type.EmptyTypes);
                    spawnMethod?.Invoke(null, null);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NetworkSingletonManager] Failed to auto-spawn {type.Name}: {ex.Message}");
            }
        }
    }
}
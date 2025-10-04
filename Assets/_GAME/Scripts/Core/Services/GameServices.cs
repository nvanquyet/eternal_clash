using System;
using System.Collections.Generic;
using _GAME.Scripts.Core.Player;
using _GAME.Scripts.HideAndSeek;
using UnityEngine;

namespace _GAME.Scripts.Core.Services
{
    /// <summary>
    /// Service Locator - Dependency injection alternative for Unity
    /// Replaces direct GameManager.Instance calls
    /// </summary>
    public class GameServices : MonoBehaviour
    {
        private static readonly Dictionary<Type, object> _services = new();
        private static GameServices _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public static void Register<T>(T service) where T : class
        {
            var type = typeof(T);
            if (_services.ContainsKey(type))
            {
                Debug.LogWarning($"[GameServices] Service {type.Name} already registered. Overwriting.");
            }
            _services[type] = service;
            Debug.Log($"[GameServices] Registered {type.Name}");
        }

        public static T Get<T>() where T : class
        {
            var type = typeof(T);
            if (_services.TryGetValue(type, out var service))
            {
                return service as T;
            }

            Debug.LogWarning($"[GameServices] Service {type.Name} not found.");
            return null;
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            service = Get<T>();
            return service != null;
        }

        public static void Unregister<T>() where T : class
        {
            var type = typeof(T);
            if (_services.Remove(type))
            {
                Debug.Log($"[GameServices] Unregistered {type.Name}");
            }
        }

        public static void Clear()
        {
            _services.Clear();
            Debug.Log("[GameServices] All services cleared.");
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                Clear();
            }
        }
    }
}
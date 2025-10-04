using System;
using System.Collections.Generic;
using _GAME.Scripts.HideAndSeek;
using UnityEngine;

namespace _GAME.Scripts.Core.Services
{
    /// <summary>
    /// Type-safe event bus - decouples event publishers from subscribers
    /// Replaces static GameEvent class with cleaner architecture
    /// </summary>
    public class GameEventBus : MonoBehaviour
    {
        private static readonly Dictionary<Type, Delegate> _events = new();
        private static readonly Dictionary<Type, List<Delegate>> _eventHistory = new();
        private static GameEventBus _instance;

        [SerializeField] private bool enableLogging = false;
        [SerializeField] private int maxHistoryPerEvent = 10;

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

        #region Core API

        /// <summary>
        /// Subscribe to an event
        /// </summary>
        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            var type = typeof(T);

            if (_events.TryGetValue(type, out var existing))
            {
                _events[type] = Delegate.Combine(existing, handler);
            }
            else
            {
                _events[type] = handler;
            }

            if (_instance?.enableLogging == true)
            {
                Debug.Log($"[EventBus] Subscribed to {type.Name}");
            }
        }

        /// <summary>
        /// Unsubscribe from an event
        /// </summary>
        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            var type = typeof(T);

            if (_events.TryGetValue(type, out var existing))
            {
                _events[type] = Delegate.Remove(existing, handler);

                if (_events[type] == null)
                {
                    _events.Remove(type);
                }
            }

            if (_instance?.enableLogging == true)
            {
                Debug.Log($"[EventBus] Unsubscribed from {type.Name}");
            }
        }

        /// <summary>
        /// Publish an event to all subscribers
        /// </summary>
        public static void Publish<T>(T eventData) where T : struct
        {
            var type = typeof(T);

            if (_instance?.enableLogging == true)
            {
                Debug.Log($"[EventBus] Publishing {type.Name}: {eventData}");
            }

            // Store in history
            StoreInHistory(type, eventData);

            // Invoke subscribers
            if (_events.TryGetValue(type, out var handler))
            {
                try
                {
                    (handler as Action<T>)?.Invoke(eventData);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[EventBus] Error invoking {type.Name}: {ex.Message}");
                }
            }
        }

        #endregion

        #region History & Debug

        private static void StoreInHistory(Type type, object eventData)
        {
            if (_instance == null) return;

            if (!_eventHistory.TryGetValue(type, out var history))
            {
                history = new List<Delegate>();
                _eventHistory[type] = history;
            }

            history.Add(Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(type), eventData, "ToString"));

            // Trim history if needed
            if (history.Count > _instance.maxHistoryPerEvent)
            {
                history.RemoveAt(0);
            }
        }

        public static void ClearHistory()
        {
            _eventHistory.Clear();
        }

        public static void Clear()
        {
            _events.Clear();
            _eventHistory.Clear();
            Debug.Log("[EventBus] Cleared all events and history.");
        }

        #endregion

        #region Utility

        public static int GetSubscriberCount<T>() where T : struct
        {
            var type = typeof(T);
            if (_events.TryGetValue(type, out var handler))
            {
                return handler.GetInvocationList().Length;
            }

            return 0;
        }

        public static bool HasSubscribers<T>() where T : struct
        {
            return _events.ContainsKey(typeof(T));
        }

        #endregion

        private void OnDestroy()
        {
            if (_instance == this)
            {
                Clear();
            }
        }
    }

    // ==================== EVENT DEFINITIONS ====================

    public struct PlayerKilledEvent
    {
        public ulong KillerId;
        public ulong VictimId;
        public float Timestamp;
    }

    public struct PlayerSpawnedEvent
    {
        public ulong ClientId;
        public Vector3 Position;
    }

    public struct RoleAssignedEvent
    {
        public ulong ClientId;
        public Role OldRole;
        public Role NewRole;
    }

    public struct GameStateChangedEvent
    {
        public GameState OldState;
        public GameState NewState;
    }

    public struct HealthChangedEvent
    {
        public ulong ClientId;
        public float OldHealth;
        public float NewHealth;
        public float MaxHealth;
    }

    public struct TaskCompletedEvent
    {
        public ulong ClientId;
        public int TaskId;
        public int CompletedTasks;
        public int TotalTasks;
    }

    public struct SkillUsedEvent
    {
        public ulong ClientId;
        public SkillType SkillType;
        public Vector3 Position;
    }

    public struct BotKilledEvent
    {
        public ulong KillerId;
        public Vector3 Position;
    }
}
using System;
using System.Collections.Generic;
using _GAME.Scripts.Core.Components;
using _GAME.Scripts.Core.Services;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Core.Player
{
    /// <summary>
    /// Lightweight player container using composition instead of inheritance
    /// Components handle all specific concerns
    /// </summary>
    public class ModularPlayer : NetworkBehaviour, IPlayer
    {
        [Header("Component References")] [SerializeField]
        private HealthComponent healthComponent;

        [SerializeField] private RoleComponent roleComponent;
        [SerializeField] private InputComponent inputComponent;

        private readonly Dictionary<Type, IPlayerComponent> _components = new();
        private readonly List<IPlayerComponent> _componentList = new();

        public ulong ClientId => NetObject.OwnerClientId;
        public NetworkObject NetObject => this.NetworkObject;
        
        public Transform Transform => transform;

        // Quick accessors
        public HealthComponent Health => GetComponent<HealthComponent>();
        public RoleComponent Role => GetComponent<RoleComponent>();
        public InputComponent Input => GetComponent<InputComponent>();

        #region Unity Lifecycle

        private void Awake()
        {
            CollectComponents();
            InitializeComponents();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            foreach (var component in _componentList)
            {
                component.OnNetworkSpawn();
            }

            // Register with game service
            if (IsServer)
            {
                GameServices.Get<IPlayerRegistry>()?.RegisterPlayer(this);
            }

            Debug.Log($"[ModularPlayer] Spawned - ClientId: {ClientId}, IsOwner: {IsOwner}");
        }

        public override void OnNetworkDespawn()
        {
            foreach (var component in _componentList)
            {
                component.OnNetworkDespawn();
            }

            if (IsServer)
            {
                GameServices.Get<IPlayerRegistry>()?.UnregisterPlayer(ClientId);
            }

            base.OnNetworkDespawn();
        }

        #endregion

        #region Component Management

        private void CollectComponents()
        {
            // Auto-collect components from GameObject
            var components = GetComponents<IPlayerComponent>();
            foreach (var component in components)
            {
                RegisterComponent(component);
            }

            // Register serialized references
            if (healthComponent != null) RegisterComponent(healthComponent);
            if (roleComponent != null) RegisterComponent(roleComponent);
            if (inputComponent != null) RegisterComponent(inputComponent);
        }

        private void RegisterComponent(IPlayerComponent component)
        {
            var type = component.GetType();
            if (!_components.ContainsKey(type))
            {
                _components[type] = component;
                _componentList.Add(component);
            }
        }

        private void InitializeComponents()
        {
            foreach (var component in _componentList)
            {
                component.Initialize(this);
            }
        }

        public T GetComponent<T>() where T : IPlayerComponent
        {
            return _components.TryGetValue(typeof(T), out var component)
                ? (T)component
                : default;
        }

        #endregion

        #region Event Broadcasting

        public void BroadcastEvent<T>(T eventData) where T : struct
        {
            GameEventBus.Publish(eventData);
        }

        #endregion

        #region Utility Methods

        public bool HasComponent<T>() where T : IPlayerComponent
        {
            return _components.ContainsKey(typeof(T));
        }

        public IEnumerable<IPlayerComponent> GetAllComponents()
        {
            return _componentList;
        }

        #endregion

        #region Debug

        [ContextMenu("Log Components")]
        private void LogComponents()
        {
            Debug.Log($"[ModularPlayer] {name} has {_componentList.Count} components:");
            foreach (var component in _componentList)
            {
                Debug.Log($"  - {component.GetType().Name} (Active: {component.IsActive})");
            }
        }

        #endregion
    }

}
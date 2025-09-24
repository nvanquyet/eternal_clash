using System;
using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.Combat.Gun;
using _GAME.Scripts.HideAndSeek.Player;
using _GAME.Scripts.HideAndSeek.Player.Rig;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace _GAME.Scripts.HideAndSeek.Combat.Base
{
    public enum WeaponType
    {
        Gun,
        Melee,
        Explosive,
        Magic
    }
    
    public enum WeaponState
    {
        Dropped,    // Weapon nằm trên mặt đất, có thể pickup
        Equipped,   // Weapon đang được player cầm
        Hidden      // Weapon bị ẩn (khi player cầm weapon khác)
    }
    
   /// <summary>
    /// Simplified BaseWeapon focused only on pickup/drop/state management.
    /// All specific logic is delegated to components.
    /// </summary>
    public class WeaponInteraction : APassiveInteractable
    {
        [Header("Basic Weapon Identity")]
        [SerializeField] protected string weaponName = "Base Weapon";
        [SerializeField] protected WeaponType weaponType;
        [SerializeField] protected Sprite weaponIcon;
        
        [Header("Visual Configuration")]
        [SerializeField] protected GameObject pickupModel;
        [SerializeField] protected GameObject equippedModel;
        
        [Header("References")]
        [SerializeField] protected InputComponent inputComponent;
        [SerializeField] protected AttackComponent attackComponent;
        
        [SerializeField] private WeaponRig rigSetup;
        
        public WeaponRig RigSetup => rigSetup;
        public AttackComponent AttackComponent => attackComponent;
        
        // Network Variables
        protected NetworkVariable<WeaponState> networkWeaponState = new NetworkVariable<WeaponState>(
            WeaponState.Dropped, writePerm: NetworkVariableWritePermission.Server);
            
        protected PlayerInteraction currentHolder;
        
        // Properties
        public WeaponState CurrentState => networkWeaponState.Value;
        public WeaponType Type => weaponType;
        public string WeaponName => weaponName;
        public Sprite WeaponIcon => weaponIcon;
        public bool IsEquipped => networkWeaponState.Value == WeaponState.Equipped;
        public bool IsDropped => networkWeaponState.Value == WeaponState.Dropped;
        public PlayerInteraction CurrentHolder => currentHolder;
        
        // IInteractable implementation
        public InteractionState State { get; protected set; } = InteractionState.Enable;
        public virtual bool CanInteract => State == InteractionState.Enable && IsDropped;
        
        // Events
        public Action<WeaponState> OnWeaponStateChanged;
        public Action<PlayerInteraction> OnWeaponPickedUp;
        public Action<PlayerInteraction> OnWeaponDropped;

        #region Unity Lifecycle

        
        #if UNITY_EDITOR
        private void OnValidate()
        {
            inputComponent = GetComponentInChildren<InputComponent>();
        }
        #endif

        protected override void Awake()
        {
            base.Awake();
            InitializeComponents();
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            networkWeaponState.OnValueChanged += OnWeaponStateValueChanged;
            UpdateVisualState(networkWeaponState.Value);
            OnWeaponNetworkSpawned();
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            
            if (networkWeaponState != null)
                networkWeaponState.OnValueChanged -= OnWeaponStateValueChanged;
            OnWeaponNetworkDespawned();
        }

        #endregion

        #region Component Management

        protected virtual void InitializeComponents()
        {
            //Show pickup model by default
            SetModelsActive(pickupActive: true, equippedActive: false);
            SetInteractionEnabled(true);
        }
        

        #endregion

        #region Interaction System

        protected override void PerformInteractionLogic(IInteractable initiator)
        {
            if (initiator is PlayerInteraction player)
            {
                RequestPickupServerRpc(player.NetworkObjectId);
            }
        }

        #endregion

        #region State Management
        
        protected virtual void OnWeaponStateValueChanged(WeaponState previousValue, WeaponState newValue)
        {
            UpdateVisualState(newValue);
            UpdateComponentStates(newValue);
            OnWeaponStateChanged?.Invoke(newValue);
        }
        
        protected virtual void UpdateVisualState(WeaponState state)
        {
            switch (state)
            {
                case WeaponState.Dropped:
                    SetModelsActive(pickupActive: true, equippedActive: false); 
                    SetInteractionEnabled(true);
                    OnWeaponDisabled();
                    break;
                    
                case WeaponState.Equipped:
                    SetModelsActive(pickupActive: false, equippedActive: true);
                    SetInteractionEnabled(false);
                    OnWeaponEnabled();
                    break;
                    
                case WeaponState.Hidden:
                    SetModelsActive(pickupActive: false, equippedActive: false);
                    SetInteractionEnabled(false);
                    OnWeaponDisabled();
                    break;
            }
        }

        protected virtual void UpdateComponentStates(WeaponState state)
        {
            // Notify components about weapon state changes
            // Components will handle their own logic
            Debug.Log($"[WeaponInteraction]: {weaponName} state changed to {state}");
            if (inputComponent != null && IsOwner)
            {
                if (state == WeaponState.Equipped)
                    inputComponent.EnableInput();
                else
                    inputComponent.DisableInput();
            }
        }

        private void SetModelsActive(bool pickupActive, bool equippedActive)
        {
            if (pickupModel) pickupModel.SetActive(pickupActive);
            if (equippedModel) equippedModel.SetActive(equippedActive);
        }

        private void SetInteractionEnabled(bool enabled)
        {
            if (InteractionCollider) InteractionCollider.enabled = enabled;
            SetState(enabled ? InteractionState.Enable : InteractionState.Disabled);
        }

        #endregion

        #region Server RPCs
        
        [ServerRpc(RequireOwnership = false)]
        protected virtual void RequestPickupServerRpc(ulong playerNetworkId)
        {
            if (networkWeaponState.Value != WeaponState.Dropped) return;
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkId, out NetworkObject playerNetObj))
            {
                PlayerInteraction player = playerNetObj.GetComponentInChildren<PlayerInteraction>();
                if (player != null)
                {
                    PickupWeapon(player);
                }
            }
        }
        
        [ServerRpc(RequireOwnership = false)]
        protected virtual void RequestDropServerRpc()
        {
            DropWeapon();
        }

        #endregion
        
        #region Public Methods
        // WeaponInteraction.cs
        protected virtual void PickupWeapon(PlayerInteraction player)
        {
            if (!IsServer || networkWeaponState.Value != WeaponState.Dropped) return;

            currentHolder = player;

            // chuyển ownership vũ khí cho người nhặt
            if (NetworkObject && NetworkObject.OwnerClientId != player.OwnerClientId)
                NetworkObject.ChangeOwnership(player.OwnerClientId);
            
            // gắn vào tay (server side → tự replicate)
            var equip = player.PlayerEquipment;
            var hold = equip ? equip.transform : player.transform;
            NetworkObject.TrySetParent(hold);

            // cập nhật state (mọi client sẽ nhận OnValueChanged)
            networkWeaponState.Value = WeaponState.Equipped;
            
            // đồng bộ PlayerEquipment.currentGunRef ngay tại server (1 nguồn sự thật)
            if (equip) equip.SetCurrentWeaponServer(this);

            OnWeaponPickedUp?.Invoke(player);
            OnPickedUp(player);
        }
        
        public virtual void DropWeapon()
        {
            if (!IsServer || networkWeaponState.Value == WeaponState.Dropped) return;
            
            PlayerInteraction previousHolder = currentHolder;
            currentHolder = null;
            networkWeaponState.Value = WeaponState.Dropped;
            
            transform.SetParent(null);
            if (previousHolder != null)
            {
                transform.position = previousHolder.transform.position + Vector3.forward;
                transform.rotation = Quaternion.identity;
            }
            
            //Change parent equipment model
            var model = rigSetup.weaponTransform;
            if (model != null)
            {
                model.SetParent(this.transform);
                model.position = Vector3.zero;
                model.rotation = Quaternion.identity;
            }
            
            //Clear callbacks
            attackComponent.OnPreFire = null;
            
            OnWeaponDropped?.Invoke(previousHolder);
            OnDropped(previousHolder);
        }
        
        protected virtual void HideWeapon()
        {
            if (!IsServer) return;
            networkWeaponState.Value = WeaponState.Hidden;
        }
        
        public virtual void ShowWeapon()
        {
            if (!IsServer) return;
            networkWeaponState.Value = WeaponState.Equipped;
        }
        
        public virtual void OnDrop()
        {
            if (IsOwner || IsServer)
            {
                RequestDropServerRpc();
            }
        }

        #endregion
        
        #region Abstract & Virtual Methods
        // Virtual methods for weapon lifecycle
        protected virtual void OnWeaponNetworkSpawned() { }
        protected virtual void OnWeaponNetworkDespawned() { }
        protected virtual void OnWeaponEnabled() { }
        protected virtual void OnWeaponDisabled() { }

        protected virtual void OnPickedUp(PlayerInteraction player)
        {
            Debug.Log($"[WeaponInteraction]: {weaponName} picked up by {player.name}");
            player?.OnInteracted(this);
        }
        protected virtual void OnDropped(PlayerInteraction player) { }

        #endregion

        public void RefreshEquipModel()
        {
            this.equippedModel.transform.SetParent(this.transform);
        }
    }
}
using System;
using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.Interaction;
using _GAME.Scripts.HideAndSeek.Player;
using _GAME.Scripts.HideAndSeek.Player.Rig;
using Unity.Netcode;
using UnityEngine;

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
        protected NetworkVariable<WeaponState> networkWeaponState =
            new NetworkVariable<WeaponState>(
                WeaponState.Dropped,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );
            
        // Người đang cầm (server-only reference; không sync trực tiếp)
        protected PlayerInteraction currentHolder;
        public PlayerInteraction CurrentHolder => currentHolder;

        // Properties
        public WeaponState CurrentState => networkWeaponState.Value;
        public WeaponType Type => weaponType;
        public string WeaponName => weaponName;
        public Sprite WeaponIcon => weaponIcon;
        public bool IsEquipped => networkWeaponState.Value == WeaponState.Equipped;
        public bool IsDropped => networkWeaponState.Value == WeaponState.Dropped;
        
        public InputComponent Input => inputComponent;

        // Quan trọng: CanInteract bám theo base + trạng thái Dropped
        public override bool CanInteract => base.CanInteract && IsDropped;

        // Events
        public Action<WeaponState> OnWeaponStateChanged;
        public Action<PlayerInteraction> OnWeaponPickedUp;
        public Action<PlayerInteraction> OnWeaponDropped;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (inputComponent == null)
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
            if (networkWeaponState != null)
                networkWeaponState.OnValueChanged -= OnWeaponStateValueChanged;
            OnWeaponNetworkDespawned();
            base.OnNetworkDespawn();
        }

        protected virtual void InitializeComponents()
        {
            // Show pickup model by default
            SetModelsActive(pickupActive: true, equippedActive: false);
            // Bật/tắt collider & state qua base.SetState (networked)
            SetInteractionEnabled(true);
        }

        #region Interaction (server-authoritative)

        // Được gọi bởi APassiveInteractable khi actor (server) xác nhận tương tác
        protected override void PerformInteractionLogic(IInteractable initiator)
        {
            if (initiator is SeekerInteraction player)
            {
                RequestPickupServerRpc(player.NetworkObjectId);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        protected virtual void RequestPickupServerRpc(ulong playerNetworkId)
        {
            if (networkWeaponState.Value != WeaponState.Dropped) return;

            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkId, out NetworkObject playerNetObj))
            {
                PlayerInteraction player = playerNetObj.GetComponentInChildren<PlayerInteraction>();
                if (player != null)
                {
                    // Giao cho PlayerEquipment làm chủ việc equip (ownership/parent/state)
                    var equip = player.PlayerEquipment;
                    if (equip != null)
                    {
                        // Option: lưu holder sớm để DropWeapon có vị trí rơi chính xác nếu cần
                        currentHolder = player;
                        equip.SetCurrentWeaponServer(this);
                        OnPickedUp(player);
                        OnWeaponPickedUp?.Invoke(player);
                    }
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        protected virtual void RequestDropServerRpc()
        {
            DropWeapon();
        }

        #endregion

        #region State & Visual

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
            // Visuals chạy ở mọi máy; input chỉ owner
            if (inputComponent != null && IsOwner)
            {
                if (state == WeaponState.Equipped) inputComponent.EnableInput();
                else inputComponent.DisableInput();
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
            // Sử dụng base.SetState để đồng bộ networkState
            SetState(enabled ? InteractionState.Enable : InteractionState.Disabled);
        }

        #endregion
        
        #region Public Methods (Server authority)

        /// <summary>
        /// Server-only. Bỏ parent (replicate), set state Dropped, đặt lại pose, clear callbacks.
        /// </summary>
        public virtual void DropWeapon()
        {
            if (!IsServer || networkWeaponState.Value == WeaponState.Dropped) return;

            PlayerInteraction previousHolder = currentHolder;
            currentHolder = null;

            // ✅ replicate parenting: parent null qua TrySetParent
            if (NetworkObject != null)
                NetworkObject.TrySetParent((Transform)null);

            networkWeaponState.Value = WeaponState.Dropped;
            
            // Đặt world pose về gần vị trí holder cũ (nếu có)
            if (previousHolder != null)
            {
                transform.position = previousHolder.transform.position + Vector3.forward;
                transform.rotation = Quaternion.identity;
            }
            
            // Gắn lại model con về weapon root
            var model = rigSetup.weaponTransform;
            if (model != null)
            {
                model.SetParent(this.transform);
                model.localPosition = Vector3.zero;
                model.localRotation = Quaternion.identity;
            }
            
            // Clear callbacks
            if (attackComponent != null)
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

        /// <summary>
        /// (Tùy chọn) Gọi từ PlayerEquipment (server) khi đã equip xong để cập nhật holder,
        /// giúp DropWeapon rơi đúng vị trí.
        /// </summary>
        public void ServerAssignHolder(PlayerInteraction holder)
        {
            if (!IsServer) return;
            currentHolder = holder;
        }

        #endregion
        
        #region Virtual Hooks

        protected virtual void OnWeaponNetworkSpawned() { }
        protected virtual void OnWeaponNetworkDespawned() { }
        protected virtual void OnWeaponEnabled() { }
        protected virtual void OnWeaponDisabled() { }

        protected virtual void OnPickedUp(PlayerInteraction player)
        {
            Debug.Log($"[WeaponInteraction]: {weaponName} picked up by {player?.name}");
            player?.OnInteracted(this);
        }

        protected virtual void OnDropped(PlayerInteraction player) { }

        #endregion

        public void RefreshEquipModel()
        {
            if (equippedModel != null)
                equippedModel.transform.SetParent(this.transform);
        }
    }
}

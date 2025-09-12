using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.DesignPattern.Interaction
{
    #region Core Base Class

    public abstract class InteractableBase : NetworkBehaviour, IInteractable
    {
        [Header("Base Interactable Settings")] [SerializeField]
        protected string entityId;

        [SerializeField] protected bool isActive = true;

        // Chỉ server được ghi
        private NetworkVariable<InteractionState> networkState =
            new NetworkVariable<InteractionState>(InteractionState.Enable, NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private NetworkVariable<bool> networkIsActive =
            new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public string EntityId => string.IsNullOrEmpty(entityId) ? $"{gameObject.name}_{NetworkObjectId}" : entityId;

        public virtual bool CanInteract => networkIsActive.Value && networkState.Value != InteractionState.Disabled;
        public Vector3 Position => transform.position;
        public InteractionState CurrentState => networkState.Value;

        #region Unity Lifecycle

        protected virtual void Awake()
        {
        }

        protected virtual void Start()
        {
        }

        #endregion


        public bool IsActive
        {
            get => networkIsActive.Value;
            set
            {
                if (IsServer) networkIsActive.Value = value;
                else if (IsOwner) SetActiveServerRpc(value);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            networkState.OnValueChanged += OnStateNetworkChanged;
            networkIsActive.OnValueChanged += OnActiveNetworkChanged;

            if (IsServer)
            {
                networkIsActive.Value = isActive;
                networkState.Value = InteractionState.Enable;
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (networkState != null) networkState.OnValueChanged -= OnStateNetworkChanged;
            if (networkIsActive != null) networkIsActive.OnValueChanged -= OnActiveNetworkChanged;
        }

        protected virtual void SetState(InteractionState newState)
        {
            if (IsServer) networkState.Value = newState;
            else if (IsOwner) SetStateServerRpc(newState);
        }

        [ServerRpc(RequireOwnership = false)]
        protected void SetStateServerRpc(InteractionState newState)
        {
            // TODO: kiểm tra quyền nếu cần
            networkState.Value = newState;
        }

        [ServerRpc(RequireOwnership = false)]
        protected void SetActiveServerRpc(bool active)
        {
            // TODO: kiểm tra quyền nếu cần
            networkIsActive.Value = active;
        }

        private void OnStateNetworkChanged(InteractionState prev, InteractionState next) => OnStateChanged(prev, next);
        private void OnActiveNetworkChanged(bool prev, bool next) => OnActiveChanged(prev, next);

        // Abstract
        public abstract bool Interact(IInteractable target);
        public abstract void OnInteracted(IInteractable initiator);

        protected virtual void OnStateChanged(InteractionState previousState, InteractionState newState)
        {
        }

        protected virtual void OnActiveChanged(bool previousValue, bool newValue)
        {
        }
    }

    /// <summary>
    /// Base class for objects that CAN BE interacted with (Tables, Doors, Chests, NPCs)
    /// These objects are PASSIVE - they only react when interacted with
    /// </summary>
    public abstract class APassiveInteractable : InteractableBase
    {
        [Header("Trigger Settings")] [SerializeField]
        protected LayerMask interactionLayer = -1;

        [SerializeField] protected string[] interactionTags = { "Player" };
        [SerializeField] protected float interactionCooldown = 0.5f;
        [SerializeField] protected GameObject uiIndicator;

        // Cooldown theo server-time
        private NetworkVariable<double> networkLastInteractionServerTime =
            new NetworkVariable<double>(0d, NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkList<ulong> nearbyInteractors = new NetworkList<ulong>();

        protected override void Start()
        {
            base.Start();
            if (uiIndicator != null) uiIndicator.SetActive(false);
        }

        // ======= Detection (server-authority) =======
        protected virtual void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;
            if (!CanInteract || !IsValidInteractor(other.gameObject)) return;
            var nob = other.gameObject.GetComponent<NetworkObject>();
            if (nob == null)
            {
                // Try get from parent if is child collider
                nob = other.GetComponentInParent<NetworkObject>();
                if (nob == null)
                {
                    Debug.LogWarning($"[PassiveInteractable] NetworkObject not found on {other.gameObject.name} or its parents");
                    return;
                }
            }
            var clientId = nob.OwnerClientId; // chỉ player-controlled mới có ý nghĩa
            if (!nearbyInteractors.Contains(clientId))
            {
                Debug.Log($"[PassiveInteractable] Client {clientId} entered trigger of {EntityId}");
                nearbyInteractors.Add(clientId);
                // chỉ gửi cho chính client đó 
                var p = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                };
                OnInteractorEnteredClientRpc(p);
            }
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;

            var nob = other.gameObject.GetComponent<NetworkObject>();
            if (nob == null)
            {
                // Try get from parent if is child collider
                nob = other.GetComponentInParent<NetworkObject>();
                if (nob == null)
                {
                    Debug.LogWarning($"[PassiveInteractable] NetworkObject not found on {other.gameObject.name} or its parents");
                    return;
                }
            }

            var clientId = nob.OwnerClientId;
            if (nearbyInteractors.Contains(clientId))
            {
                nearbyInteractors.Remove(clientId);
                var p = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                };
                OnInteractorExitedClientRpc(p);
            }
        }

        // Nếu bạn buộc phải phát hiện trên client (host migration / special case),
        // thay vì truyền clientId từ client, dùng ServerRpcParams lấy SenderClientId:
        [ServerRpc(RequireOwnership = false)]
        private void ClientReportEnterServerRpc(ServerRpcParams rpcParams = default)
        {
            var clientId = rpcParams.Receive.SenderClientId;
            if (!nearbyInteractors.Contains(clientId))
            {
                nearbyInteractors.Add(clientId);
                var p = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
                OnInteractorEnteredClientRpc(p);
            }
        }

        [ClientRpc]
        private void OnInteractorEnteredClientRpc(ClientRpcParams p = default)
        {
            Debug.Log($"[PassiveInteractable] Client entered trigger of {EntityId}");
            if (uiIndicator != null)
            {
                uiIndicator.SetActive(true);
                OnInteractionUIShown();
            }
        }

        [ClientRpc]
        private void OnInteractorExitedClientRpc(ClientRpcParams p = default)
        {
            if (uiIndicator != null)
            {
                uiIndicator.SetActive(false);
                OnInteractionUIHidden();
            }
        }

        // ======= Interaction Logic =======
        public override bool Interact(IInteractable target) => false; // passive không chủ động

        public override void OnInteracted(IInteractable initiator)
        {
            if (!IsServer) return;
            if (!CanInteract || !CanPerformInteraction()) return;

            networkLastInteractionServerTime.Value = NetworkManager.Singleton.ServerTime.Time;
            SetState(InteractionState.Disabled);

            PerformInteractionLogic(initiator); // override ở lớp con

            SetState(InteractionState.Enable);
        }

        private bool CanPerformInteraction()
        {
            var now = NetworkManager.Singleton.ServerTime.Time;
            return now > networkLastInteractionServerTime.Value + interactionCooldown;
        }

        protected virtual bool IsValidInteractor(GameObject obj)
        {
            if (((1 << obj.layer) & interactionLayer) == 0) return false;
            if (interactionTags != null && interactionTags.Length > 0)
                return interactionTags.Any(obj.CompareTag);
            return true;
        }

        // Abstracts
        protected abstract void PerformInteractionLogic(IInteractable initiator);

        protected virtual void OnInteractionUIShown()
        {
        }

        protected virtual void OnInteractionUIHidden()
        {
        }

        // Props
        public bool HasNearbyInteractors => nearbyInteractors.Count > 0;
        public int NearbyInteractorCount => nearbyInteractors.Count;
    }


    /// <summary>
    /// Base class for objects that CAN ACTIVELY interact (Player, AI, Robots)
    /// These objects detect and interact with passive objects when input is received
    /// </summary>
    public abstract class AActiveInteractable : InteractableBase
    {
        [Header("Active Interaction Settings")] [SerializeField]
        protected LayerMask detectLayerMask = -1;

        [SerializeField] protected string[] detectTags = { "Interactable" };
        [SerializeField] protected float interactionRange = 3f;

        private APassiveInteractable currentPassiveInteractable;

        // ======= Input =======
        protected virtual void OnInteractInput()
        {
            if (!IsOwner) return;
            if (currentPassiveInteractable == null || !CanInteract)
            {
                OnInteractionFailed("No valid interactable found");
                return;
            }

            var targetNob = currentPassiveInteractable.GetComponent<NetworkObject>();
            if (targetNob == null)
            {
                OnInteractionFailed("Target missing NetworkObject");
                return;
            }

            PerformInteractionServerRpc(targetNob); // gửi NetworkObjectReference
        }

        [ServerRpc]
        private void PerformInteractionServerRpc(NetworkObjectReference targetRef, ServerRpcParams rpcParams = default)
        {
            if (!targetRef.TryGet(out var targetNob))
            {
                return;
            }

            if (!targetNob.TryGetComponent<IInteractable>(out var target))
            {
                return;
            }
            
            // Server-side re-validate
            var senderId = rpcParams.Receive.SenderClientId;
            // Optional: kiểm tra actor gửi RPC có thật sự là owner của "this" (nếu cần)
            // if (OwnerClientId != senderId) return;

            // Kiểm tra tầm
            if (this is { } actor && target is InteractableBase tBase)
            {
                if (Vector3.Distance(actor.Position, tBase.Position) > interactionRange)
                {
                    return;
                }
            }

            if (!Interact(target)) return;
            // chỉ gửi feedback cho client owner
            var p = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { senderId } }
            };
            OnInteractionSuccessClientRpc(targetRef, p);
        }

        [ClientRpc]
        private void OnInteractionSuccessClientRpc(NetworkObjectReference targetRef, ClientRpcParams p = default)
        {
            if (!IsOwner) return; // đã target đúng, nhưng giữ guard
            if (!targetRef.TryGet(out var targNob))
            {
                OnInteractionPerformed(null);
                return;
            }
            
            var passive = targNob.GetComponent<APassiveInteractable>();
            OnInteractionPerformed(passive);
        }

        // ======= Detection =======
        protected virtual void OnTriggerEnter(Collider other)
        {
            if (!IsOwner || !CanInteract) return;

            if (other.TryGetComponent<APassiveInteractable>(out var passive) && IsValidInteractable(other.gameObject))
            {
                SetCurrentInteractable(passive);
            }
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            if (!IsOwner) return;

            if (currentPassiveInteractable != null &&
                other.GetComponent<APassiveInteractable>() == currentPassiveInteractable)
            {
                ClearCurrentInteractable();
            }
        }

        protected virtual bool IsValidInteractable(GameObject obj)
        {
            if (((1 << obj.layer) & detectLayerMask) == 0) return false;
            if (detectTags != null && detectTags.Length > 0) return detectTags.Any(obj.CompareTag);
            return true;
        }

        protected virtual void SetCurrentInteractable(APassiveInteractable passive)
        {
            if (currentPassiveInteractable == passive) return;
            ClearCurrentInteractable();
            currentPassiveInteractable = passive;
            OnNearInteractable(passive);
        }

        protected virtual void ClearCurrentInteractable()
        {
            if (currentPassiveInteractable == null) return;
            var prev = currentPassiveInteractable;
            currentPassiveInteractable = null;
            OnLeftInteractable(prev);
        }

        // ======= Interaction impl =======
        public override bool Interact(IInteractable target)
        {
            Debug.Log($"[ActiveInteractable] Interact called on {EntityId} targeting {target?.EntityId}");
            if (!CanInteract || target == null) return false;

            SetState(InteractionState.Disabled);
            target.OnInteracted(this); // chạy trên server (Passive sẽ validate cooldown server-time)
            SetState(InteractionState.Enable);
            Debug.Log($"[ActiveInteractable] Interact called on {EntityId} targeting {target?.EntityId} succeeded");
            return true;
        }

        public override void OnInteracted(IInteractable initiator)
        {
            /* Active có thể bị người khác tương tác nếu muốn */
        }

        // Abstracts
        protected abstract void OnNearInteractable(APassiveInteractable interactable);
        protected abstract void OnLeftInteractable(APassiveInteractable interactable);

        protected virtual void OnInteractionPerformed(APassiveInteractable interactable)
        {
            interactable.OnInteracted(this);
        }

        protected virtual void OnInteractionFailed(string reason)
        {
            Debug.LogWarning($"[ActiveInteractable] Interaction failed: {reason}");
        }

        // Props
        public bool HasInteractable => currentPassiveInteractable != null;
        public APassiveInteractable CurrentInteractable => currentPassiveInteractable;
    }

    #endregion

    #region Combat System Base Classes

    /// <summary>
    /// Base class for attackable entities with proper network synchronization
    /// </summary>
    public abstract class AAttackable : InteractableBase, IAttackable
    {
        [Header("Attack Settings")] [SerializeField]
        protected float baseDamage = 10f;

        [SerializeField] protected float attackRange = 2f;
        [SerializeField] protected float attackCooldown = 1f;
        [SerializeField] protected DamageType primaryDamageType = DamageType.Physical;

        [Header("Collision Attack Settings")] [SerializeField]
        protected bool enableCollisionAttack = false;

        [SerializeField] protected LayerMask attackableLayers = -1;
        [SerializeField] protected string[] attackableTags = { "Enemy", "Destructible" };
        [SerializeField] public bool destroyAfterCollisionAttack = true;

        protected bool hasCollisionAttacked;

        // Dùng server-time để tránh lệch giờ client
        protected NetworkVariable<double> networkNextAttackServerTime = new NetworkVariable<double>(0d);

        public float BaseDamage => baseDamage;
        public float AttackRange => attackRange;
        public float AttackCooldown => attackCooldown;
        public float NextAttackTime => (float)networkNextAttackServerTime.Value;
        public DamageType PrimaryDamageType => primaryDamageType;

        public virtual bool CanAttack =>
            CanInteract &&
            CurrentState != InteractionState.Disabled &&
            NetworkManager != null &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ServerTime.Time >= networkNextAttackServerTime.Value;

        public event Action<IAttackable, IDefendable, float> OnAttackPerformed;

        // ========================= Active Attack =========================
        public virtual bool Attack(IDefendable target)
        {
            if (target == null) return false;

            if (IsServer)
            {
                return Server_AttemptAttack(target);
            }
            else if (IsOwner)
            {
                var nob = ((MonoBehaviour)target).GetComponent<NetworkObject>();
                if (nob != null)
                {
                    AttackServerRpc(nob);
                }
            }

            return false;
        }

        [ServerRpc(RequireOwnership = false)]
        protected virtual void AttackServerRpc(NetworkObjectReference targetRef)
        {
            if (!targetRef.TryGet(out var targetNob)) return;
            if (!targetNob.TryGetComponent<IDefendable>(out var target)) return;

            Server_AttemptAttack(target);
        }

        private bool Server_AttemptAttack(IDefendable target)
        {
            // VALIDATION server-side
            if (!CanAttack) return false;
            if (target == null || !target.IsAlive) return false;
            if (!IsInAttackRange(target)) return false;

            SetState(InteractionState.Disabled);

            float damage = CalculateDamage(target);
            float actualDamage = target.TakeDamage(this, damage, primaryDamageType);

            networkNextAttackServerTime.Value = NetworkManager.Singleton.ServerTime.Time + attackCooldown;

            OnAttackPerformed?.Invoke(this, target, actualDamage);

            var targetNob = ((MonoBehaviour)target).GetComponent<NetworkObject>();
            if (targetNob != null)
            {
                OnAttackPerformedClientRpc(targetNob, actualDamage);
            }

            SetState(InteractionState.Enable);
            return actualDamage > 0f;
        }

        [ClientRpc]
        protected virtual void OnAttackPerformedClientRpc(NetworkObjectReference targetRef, float actualDamage)
        {
            if (!targetRef.TryGet(out var targetNob))
            {
                OnSuccessfulAttack(null, actualDamage);
                return;
            }

            if (!targetNob.TryGetComponent<IDefendable>(out var target))
            {
                OnSuccessfulAttack(null, actualDamage);
                return;
            }

            OnSuccessfulAttack(target, actualDamage); // FX/UI local
        }

        public virtual bool IsInAttackRange(IDefendable target) =>
            target != null && Vector3.Distance(Position, target.Position) <= attackRange;

        public virtual float CalculateDamage(IDefendable target) => baseDamage;

        // ========================= Collision Attack =========================
        protected virtual void OnTriggerEnter(Collider other)
        {
            if (!enableCollisionAttack || hasCollisionAttacked || !CanAttack) return;
            if (!IsServer) return; // server-authority

            Server_ProcessCollision(other);
        }

        protected virtual void OnCollisionEnter(Collision collision)
        {
            if (!enableCollisionAttack || hasCollisionAttacked || !CanAttack) return;
            if (!IsServer) return;

            Server_ProcessCollision(collision.collider);
        }

        private void Server_ProcessCollision(Collider other)
        {
            if (!IsValidTarget(other.gameObject))
            {
                //OnHitInvalidTarget(other);
                return;
            }

            if (!other.TryGetComponent<IDefendable>(out var target))
            {
                //OnHitNonDefendableTarget(other);
                return;
            }

            if (!target.IsAlive || !IsInAttackRange(target)) return;

            hasCollisionAttacked = true;

            SetState(InteractionState.Disabled);

            float damage = CalculateDamage(target);
            float actualDamage = target.TakeDamage(this, damage, primaryDamageType);

            networkNextAttackServerTime.Value = NetworkManager.Singleton.ServerTime.Time + attackCooldown;

            OnAttackPerformed?.Invoke(this, target, actualDamage);

            var targetNob = ((MonoBehaviour)target).GetComponent<NetworkObject>();
            if (targetNob != null)
            {
                OnAttackPerformedClientRpc(targetNob, actualDamage);
            }

            if (destroyAfterCollisionAttack)
            {
                HandleDestruction();
            }
            else
            {
                SetState(InteractionState.Enable);
            }
        }

        protected virtual bool IsValidTarget(GameObject target)
        {
            // Layer
            if (((1 << target.layer) & attackableLayers) == 0) return false;

            // Tag
            if (attackableTags != null && attackableTags.Length > 0)
            {
                for (int i = 0; i < attackableTags.Length; i++)
                    if (target.CompareTag(attackableTags[i]))
                        return true;
                return false;
            }

            return true;
        }

        // ========================= Abstract hooks =========================
        protected abstract void OnSuccessfulAttack(IDefendable target, float actualDamage);
        protected abstract void OnHitInvalidTarget(Collider other);
        protected abstract void OnHitNonDefendableTarget(Collider other);
        protected abstract void HandleDestruction();
    }


    /// <summary>
    /// Base class for defendable entities with proper network health synchronization
    /// </summary>
    public abstract class ADefendable : InteractableBase, IDefendable
    {
        [Header("Defense Settings")] [SerializeField]
        protected float maxHealth = 100f;

        [SerializeField] protected float defenseValue = 0f;
        [SerializeField] protected bool isInvulnerable = false;

        // NetVars: server-only write
        protected NetworkVariable<float> networkCurrentHealth =
            new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Server);

        protected NetworkVariable<bool> networkIsInvulnerable =
            new NetworkVariable<bool>(writePerm: NetworkVariableWritePermission.Server);

        // Optional: dùng nếu bạn muốn UI ước lượng i-frames / last hit
        protected NetworkVariable<double> networkLastHitServerTime =
            new NetworkVariable<double>(0d, writePerm: NetworkVariableWritePermission.Server);

        // Server-only guard
        protected bool isDead;

        public virtual float CurrentHealth => networkCurrentHealth.Value;
        public float MaxHealth => maxHealth;
        public float DefenseValue => defenseValue;
        public virtual bool IsAlive => networkCurrentHealth.Value > 0f && !isDead;
        public bool IsInvulnerable => networkIsInvulnerable.Value;

        public event Action<float, float> OnHealthChanged; // (current, max)
        public event Action<IDefendable, IAttackable> OnDied; // (self, killer)

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            networkCurrentHealth.OnValueChanged += OnHealthNetworkChanged;
            networkIsInvulnerable.OnValueChanged += OnInvulnerabilityNetworkChanged;

            if (IsServer)
            {
                // Reset every spawn (hữu ích khi pooled)
                isDead = false;
                networkCurrentHealth.Value =
                    Mathf.Clamp(networkCurrentHealth.Value <= 0 ? maxHealth : networkCurrentHealth.Value, 0, maxHealth);
                networkIsInvulnerable.Value = isInvulnerable;
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            if (networkCurrentHealth != null)
                networkCurrentHealth.OnValueChanged -= OnHealthNetworkChanged;
            if (networkIsInvulnerable != null)
                networkIsInvulnerable.OnValueChanged -= OnInvulnerabilityNetworkChanged;
        }

        #endregion

        #region Health & Damage System

        // GỌI TRÊN SERVER (được Attackable gọi sau khi validate)
        public virtual float TakeDamage(IAttackable attacker, float damage, DamageType damageType = DamageType.Physical)
        {
            if (!IsServer) return 0f; // server-authority
            if (!IsAlive) return 0f;
            if (IsInvulnerable) return 0f;

            float finalDamage = CalculateFinalDamage(damage, damageType);
            float newHealth = Mathf.Max(0f, networkCurrentHealth.Value - finalDamage);
            networkCurrentHealth.Value = newHealth;

            networkLastHitServerTime.Value = NetworkManager.Singleton.ServerTime.Time;

            if (newHealth <= 0f && !isDead)
            {
                isDead = true;
                SetState(InteractionState.Disabled);
                // Notify server-side listeners
                OnDied?.Invoke(this, attacker);

                // Broadcast cho client làm FX/âm thanh/HUD
                var attackerRef = attacker is MonoBehaviour mb && mb.TryGetComponent(out NetworkObject aNob)
                    ? new NetworkObjectReference(aNob)
                    : default;

                OnDeathClientRpc(attackerRef);
                OnDeath(attacker); // hook ảo cho lớp con (server-side)
            }

            // Có thể bắn hit clientrpc riêng nếu muốn hiệu ứng trúng đòn
            OnHitClientRpc(finalDamage);

            return finalDamage;
        }

        // GỌI TRÊN SERVER (ví dụ từ item hồi máu)
        public virtual float Heal(float amount)
        {
            if (!IsServer) return 0f;
            if (isDead) return 0f;

            float prev = networkCurrentHealth.Value;
            float next = Mathf.Clamp(prev + Mathf.Abs(amount), 0f, maxHealth);
            networkCurrentHealth.Value = next;

            return next - prev;
        }

        // Owner có thể yêu cầu invuln, server quyết định
        public virtual void SetInvulnerable(bool invulnerable)
        {
            if (IsServer)
            {
                networkIsInvulnerable.Value = invulnerable;
            }
            else if (IsOwner)
            {
                SetInvulnerableServerRpc(invulnerable);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetInvulnerableServerRpc(bool invulnerable)
        {
            // TODO: kiểm tra quyền nếu cần (GM, skill hợp lệ, v.v.)
            networkIsInvulnerable.Value = invulnerable;
        }

        // (Optional) API revive
        [ServerRpc(RequireOwnership = false)]
        public void ReviveServerRpc(float reviveHealth = -1f)
        {
            if (!IsServer) return;
            float hp = reviveHealth > 0 ? reviveHealth : maxHealth;
            isDead = false;
            networkCurrentHealth.Value = Mathf.Clamp(hp, 1f, maxHealth);
            SetState(InteractionState.Enable);
            OnRevivedClientRpc(networkCurrentHealth.Value);
        }

        protected virtual float CalculateFinalDamage(float baseDamage, DamageType damageType)
        {
            if (damageType == DamageType.True) return baseDamage;
            return Mathf.Max(1f, baseDamage - defenseValue);
        }

        #endregion

        #region Client Feedback

        [ClientRpc]
        protected virtual void OnHitClientRpc(float appliedDamage)
        {
            // FX nhẹ: flash, số damage bay… (client-side)
            OnHitLocal(appliedDamage);
        }

        [ClientRpc]
        protected virtual void OnDeathClientRpc(NetworkObjectReference attackerRef)
        {
            // Tải tham chiếu attacker nếu cần cho FX/killfeed
            NetworkObject attackerNob = null;
            attackerRef.TryGet(out attackerNob);

            OnDeathLocal(attackerNob); // FX death, ragdoll, disable UI…
        }

        [ClientRpc]
        protected virtual void OnRevivedClientRpc(float newHealth)
        {
            OnRevivedLocal(newHealth); // FX hồi sinh, bật UI…
        }

        #endregion

        #region Event Handlers

        private void OnHealthNetworkChanged(float previousHealth, float newHealth)
        {
            OnHealthChanged?.Invoke(newHealth, maxHealth);
            OnHealthChangedLocal(previousHealth, newHealth);
        }

        private void OnInvulnerabilityNetworkChanged(bool previousValue, bool newValue)
        {
            OnInvulnerabilityChangedLocal(previousValue, newValue);
        }

        #endregion

        #region Hooks (override tùy lớp con)

        // Server-side hook sau khi chết (ngoài ClientRpc)
        public virtual void OnDeath(IAttackable killer)
        {
        }

        // Local-only hooks cho FX/UI
        protected virtual void OnHealthChangedLocal(float previousHealth, float newHealth)
        {
        }

        protected virtual void OnInvulnerabilityChangedLocal(bool previousValue, bool newValue)
        {
        }

        protected virtual void OnHitLocal(float appliedDamage)
        {
        }

        protected virtual void OnDeathLocal(NetworkObject attackerNob)
        {
        }

        protected virtual void OnRevivedLocal(float newHealth)
        {
        }

        #endregion
    }

    #endregion
}
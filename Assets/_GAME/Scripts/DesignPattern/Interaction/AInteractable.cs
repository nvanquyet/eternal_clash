using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.DesignPattern.Interaction
{
    // =========================
    // Core Base Class
    // =========================
    public abstract class InteractableBase : NetworkBehaviour, IInteractable
    {
        [Header("Base Interactable Settings")] [SerializeField]
        protected string entityId;

        [SerializeField] protected bool isActive = true;
        [SerializeField] private Collider interactionCollider;

        // NetVars: Server write only
        private NetworkVariable<InteractionState> networkState =
            new NetworkVariable<InteractionState>(
                InteractionState.Enable,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private NetworkVariable<bool> networkIsActive =
            new NetworkVariable<bool>(
                true,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public string EntityId => string.IsNullOrEmpty(entityId) ? $"{gameObject.name}_{NetworkObjectId}" : entityId;

        public virtual bool CanInteract => networkIsActive.Value && networkState.Value != InteractionState.Disabled;
        public Vector3 Position => transform.position;

        public Collider InteractionCollider
        {
            get
            {
                if (interactionCollider != null) return interactionCollider;
                interactionCollider = GetComponent<Collider>();
                if (interactionCollider == null)
                    Debug.LogWarning(
                        $"[InteractableBase] InteractionCollider not set and not found on {gameObject.name}");
                return interactionCollider;
            }
        }

        public InteractionState CurrentState => networkState.Value;

        protected virtual void Awake()
        {
        }

        protected virtual void Start()
        {
        }

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
            if (networkState != null) networkState.OnValueChanged -= OnStateNetworkChanged;
            if (networkIsActive != null) networkIsActive.OnValueChanged -= OnActiveNetworkChanged;
            base.OnNetworkDespawn();
        }

        protected virtual void SetState(InteractionState newState)
        {
            if (IsServer) networkState.Value = newState;
            else if (IsOwner) SetStateServerRpc(newState);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetStateServerRpc(InteractionState newState, ServerRpcParams rpc = default)
        {
            if (!ValidateRpcSender(rpc.Receive.SenderClientId)) return;
            networkState.Value = newState;
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetActiveServerRpc(bool active, ServerRpcParams rpc = default)
        {
            if (!ValidateRpcSender(rpc.Receive.SenderClientId)) return;
            networkIsActive.Value = active;
        }

        protected virtual bool ValidateRpcSender(ulong senderId)
        {
            return senderId == OwnerClientId;
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

    // =========================
    // Passive Interactable (trigger-based)
    // =========================
    public abstract class APassiveInteractable : InteractableBase
    {
        [Header("Trigger Settings")] [SerializeField]
        protected LayerMask interactionLayer = ~0;

        [SerializeField] protected string[] interactionTags = { "Player" };
        [SerializeField] protected float interactionCooldown = 0.5f;
        [SerializeField] protected GameObject uiIndicator;

        [Tooltip("Nếu bật, server sẽ gửi RPC bật/tắt prompt. Nếu tắt, prompt hiển thị local-only ở client owner.")]
        [SerializeField]
        private bool useServerDrivenPrompt = false;

        // Cooldown theo server-time
        private NetworkVariable<double> networkLastInteractionServerTime =
            new NetworkVariable<double>(
                0d,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        // HashSet với proper cleanup
        private readonly HashSet<ulong> nearbyInteractors = new HashSet<ulong>();

        protected override void Start()
        {
            base.Start();
            if (uiIndicator != null) uiIndicator.SetActive(false);
        }

        public override void OnNetworkDespawn()
        {
            // Proper cleanup
            nearbyInteractors.Clear();
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Hiển thị prompt LOCAL-ONLY (không network). Gọi ở client owner.
        /// </summary>
        public void ShowPromptLocal(bool visible)
        {
            if (uiIndicator != null)
            {
                uiIndicator.SetActive(visible);
                if (visible) OnInteractionUIShown();
                else OnInteractionUIHidden();
            }
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;
            if (!CanInteract || !IsValidInteractor(other.gameObject)) return;

            if (!TryGetValidNetworkObject(other, out var nob))
            {
                Debug.LogWarning(
                    $"[PassiveInteractable] NetworkObject not found on {other.gameObject.name} or parents.");
                return;
            }

            var clientId = nob.OwnerClientId;
            if (nearbyInteractors.Add(clientId) && useServerDrivenPrompt)
            {
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

            if (!TryGetValidNetworkObject(other, out var nob)) return;

            var clientId = nob.OwnerClientId;
            if (nearbyInteractors.Remove(clientId) && useServerDrivenPrompt)
            {
                var p = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                };
                OnInteractorExitedClientRpc(p);
            }
        }

        /// <summary>
        /// Improved NetworkObject detection with comprehensive fallback
        /// </summary>
        protected virtual bool TryGetValidNetworkObject(Collider collider, out NetworkObject networkObject)
        {
            networkObject = null;

            // Priority 1: Rigidbody root (correct physics root)
            if (collider.attachedRigidbody != null)
            {
                networkObject = collider.attachedRigidbody.GetComponent<NetworkObject>();
                if (networkObject != null) return true;
            }

            // Priority 2: Direct component
            networkObject = collider.GetComponent<NetworkObject>();
            if (networkObject != null) return true;

            // Priority 3: Parent hierarchy
            networkObject = collider.GetComponentInParent<NetworkObject>();
            if (networkObject != null) return true;

            // Priority 4: Child hierarchy (less common but possible)
            networkObject = collider.GetComponentInChildren<NetworkObject>();

            return networkObject != null;
        }

        [ClientRpc]
        private void OnInteractorEnteredClientRpc(ClientRpcParams p = default)
        {
            OnInteractorEntered();
        }

        protected virtual void OnInteractorEntered()
        {
            if (uiIndicator != null)
            {
                uiIndicator.SetActive(true);
                OnInteractionUIShown();
            }
        }

        [ClientRpc]
        private void OnInteractorExitedClientRpc(ClientRpcParams p = default)
        {
            OnInteractorExited();
        }

        protected virtual void OnInteractorExited()
        {
            if (uiIndicator != null)
            {
                uiIndicator.SetActive(false);
                OnInteractionUIHidden();
            }
        }

        public override bool Interact(IInteractable target) => false; // Passive không chủ động

        public override void OnInteracted(IInteractable initiator)
        {
            if (!IsServer) return;
            if (!CanInteract || !CanPerformInteraction()) return;

            // Thread-safe state management
            if (CurrentState == InteractionState.Disabled) return;

            networkLastInteractionServerTime.Value = NetworkManager.Singleton.ServerTime.Time;
            SetState(InteractionState.Disabled);

            try
            {
                PerformInteractionLogic(initiator);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PassiveInteractable] Error in PerformInteractionLogic: {ex.Message}");
            }
            finally
            {
                SetState(InteractionState.Enable);
            }
        }

        private bool CanPerformInteraction()
        {
            var now = NetworkManager.Singleton.ServerTime.Time;
            return now > networkLastInteractionServerTime.Value + interactionCooldown;
        }

        protected virtual bool IsValidInteractor(GameObject obj)
        {
            if (obj == null) return false;
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

    // =========================
    // Active Interactable (actor/player)
    // =========================
    public abstract class AActiveInteractable : InteractableBase
    {
        [Header("Active Interaction Settings")] [SerializeField]
        protected LayerMask detectLayerMask = ~0;

        [SerializeField] protected string[] detectTags = { "Interactable" };
        [SerializeField] protected float interactionRange = 3f;
        [SerializeField] protected float interactionValidationRange = 5f; // Server validation range (slightly larger)

        private APassiveInteractable currentPassiveInteractable;

        // INPUT - Should be called from input system
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

            PerformInteractionServerRpc(targetNob);
        }

        [ServerRpc(RequireOwnership = false)]
        private void PerformInteractionServerRpc(NetworkObjectReference targetRef, ServerRpcParams rpcParams = default)
        {
            if (!ValidateRpcSender(rpcParams.Receive.SenderClientId)) return;

            if (!targetRef.TryGet(out var targetNob))
            {
                SendInteractionFailedClientRpc("Target not found", rpcParams.Receive.SenderClientId);
                return;
            }

            if (!targetNob.TryGetComponent<IInteractable>(out var target))
            {
                SendInteractionFailedClientRpc("Target not interactable", rpcParams.Receive.SenderClientId);
                return;
            }

            // Server-side range validation (with tolerance)
            if (target is InteractableBase tBase)
            {
                float distance = Vector3.Distance(this.Position, tBase.Position);
                if (distance > interactionValidationRange)
                {
                    SendInteractionFailedClientRpc("Out of range", rpcParams.Receive.SenderClientId);
                    return;
                }
            }

            if (!Interact(target))
            {
                SendInteractionFailedClientRpc("Interaction failed", rpcParams.Receive.SenderClientId);
                return;
            }

            var p = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } }
            };
            OnInteractionSuccessClientRpc(targetRef, p);
        }

        [ClientRpc]
        private void SendInteractionFailedClientRpc(string reason, ulong targetClientId)
        {
            if (NetworkManager.Singleton.LocalClientId == targetClientId)
                OnInteractionFailed(reason);
        }

        [ClientRpc]
        private void OnInteractionSuccessClientRpc(NetworkObjectReference targetRef, ClientRpcParams p = default)
        {
            APassiveInteractable passive = null;
            if (targetRef.TryGet(out var nob))
                passive = nob.GetComponent<APassiveInteractable>();
            OnInteractionPerformed(passive);

            // Ẩn prompt local sau khi tương tác thành công
            passive?.ShowPromptLocal(false);
        }

        // Detection (LOCAL-ONLY prompt ở owner)
        protected virtual void OnTriggerEnter(Collider other)
        {
            if (!IsOwner || !CanInteract) return;

            if (other.TryGetComponent<APassiveInteractable>(out var passive) &&
                IsValidInteractable(other.gameObject) &&
                passive.CanInteract)
            {
                SetCurrentInteractable(passive);
                passive.ShowPromptLocal(true);
            }
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            if (!IsOwner) return;

            if (currentPassiveInteractable != null &&
                other.GetComponent<APassiveInteractable>() == currentPassiveInteractable)
            {
                currentPassiveInteractable.ShowPromptLocal(false);
                ClearCurrentInteractable();
            }
        }

        protected virtual bool IsValidInteractable(GameObject obj)
        {
            if (obj == null) return false;
            if (((1 << obj.layer) & detectLayerMask) == 0) return false;
            if (detectTags != null && detectTags.Length > 0)
                return detectTags.Any(obj.CompareTag);
            return true;
        }

        protected virtual void SetCurrentInteractable(APassiveInteractable passive)
        {
            if (currentPassiveInteractable == passive) return;

            // Đảm bảo prompt cũ tắt nếu bị sót
            if (currentPassiveInteractable != null)
                currentPassiveInteractable.ShowPromptLocal(false);

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

        // Interaction implementation
        public override bool Interact(IInteractable target)
        {
            if (!CanInteract || target == null) return false;

            // Thread-safe state check
            if (CurrentState == InteractionState.Disabled) return false;

            SetState(InteractionState.Disabled);

            try
            {
                target.OnInteracted(this);
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ActiveInteractable] Interaction error: {ex.Message}");
                return false;
            }
            finally
            {
                SetState(InteractionState.Enable);
            }
        }

        public override void OnInteracted(IInteractable initiator)
        {
            /* optional */
        }

        // Abstracts
        protected abstract void OnNearInteractable(APassiveInteractable interactable);
        protected abstract void OnLeftInteractable(APassiveInteractable interactable);

        protected virtual void OnInteractionPerformed(APassiveInteractable interactable)
        {
        }

        protected virtual void OnInteractionFailed(string reason)
        {
        }

        // Props
        public bool HasInteractable => currentPassiveInteractable != null;
        public APassiveInteractable CurrentInteractable => currentPassiveInteractable;
    }

    // =========================
    // Combat System: Attackable
    // =========================
    public abstract class AAttackable : InteractableBase, IAttackable
    {
        [Header("Attack Settings")] [SerializeField]
        private float baseDamage = 10f;

        [SerializeField] protected float attackRange = 2f;
        [SerializeField] protected float attackCooldown = 1f;
        [SerializeField] protected DamageType primaryDamageType = DamageType.Physical;

        [Header("Collision Attack Settings")] [SerializeField]
        protected bool enableCollisionAttack = false;

        [SerializeField] protected LayerMask attackableLayers = ~0;
        [SerializeField] protected string[] attackableTags = { "Enemy", "Destructible" };
        [SerializeField] public bool destroyAfterCollisionAttack = true;

        protected bool hasCollisionAttacked;

        // NetVars: server-only write
        protected NetworkVariable<float> networkBaseDamage =
            new NetworkVariable<float>(
                0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        protected NetworkVariable<double> networkNextAttackServerTime =
            new NetworkVariable<double>(
                0d,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        // Cache trước spawn
        private float? pendingDamage = null;
        private bool damageInitialized = false;

        public float BaseDamage => networkBaseDamage.Value;
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

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && !damageInitialized)
            {
                networkBaseDamage.Value = pendingDamage ?? baseDamage;
                damageInitialized = true;
            }
        }

        // Damage management
        public virtual void SetBaseDamage(float damage)
        {
            if (IsSpawned)
            {
                return;
            }

            pendingDamage = damage;
            Debug.Log($"[AAttackable-SetBaseDamage] {name} Pending damage set: {damage}");
        }

        [ServerRpc(RequireOwnership = false)]
        public virtual void UpdateBaseDamageServerRpc(float newDamage, ServerRpcParams rpc = default)
        {
            if (!ValidateRpcSender(rpc.Receive.SenderClientId)) return;
            networkBaseDamage.Value = Mathf.Max(0f, newDamage);
            Debug.Log($"[AAttackable-UpdateBaseDamageServerRpc] {name} Updated damage: {networkBaseDamage.Value}");
        }

        // Active Attack
        public virtual bool Attack(IDefendable target)
        {
            Debug.Log($"[AAttackable-Attack] {name} Attacking target, IsServer: {IsServer}, IsOwner: {IsOwner}");

            if (target == null)
            {
                Debug.Log($"[AAttackable-Attack] {name} Target is NULL");
                return false;
            }

            if (IsServer) return Server_AttemptAttack(target);

            if (IsOwner)
            {
                var nob = (target as MonoBehaviour)?.GetComponent<NetworkObject>();
                if (nob != null) AttackServerRpc(nob);
            }

            return false;
        }

        [ServerRpc(RequireOwnership = false)]
        protected virtual void AttackServerRpc(NetworkObjectReference targetRef, ServerRpcParams rpc = default)
        {
            Debug.Log($"[AAttackable-AttackServerRpc] {name} Received attack RPC");

            if (!ValidateRpcSender(rpc.Receive.SenderClientId)) return;

            if (!targetRef.TryGet(out var targetNob))
            {
                Debug.Log($"[AAttackable-AttackServerRpc] {name} Failed to get target NetworkObject");
                return;
            }

            if (!targetNob.TryGetComponent<IDefendable>(out var target))
            {
                Debug.Log($"[AAttackable-AttackServerRpc] {name} Target has no IDefendable");
                return;
            }

            Server_AttemptAttack(target);
        }

        private bool Server_AttemptAttack(IDefendable target)
        {
            Debug.Log($"[AAttackable-Server_AttemptAttack] {name} START");

            if (!IsServer)
            {
                Debug.Log($"[AAttackable-Server_AttemptAttack] {name} FAILED - Not server");
                return false;
            }

            // Thread-safe attack validation
            if (!CanAttack)
            {
                Debug.Log(
                    $"[AAttackable-Server_AttemptAttack] {name} FAILED - CanAttack: {CanAttack}, IsAttacking");
                return false;
            }

            if (target == null || !target.IsAlive)
            {
                Debug.Log($"[AAttackable-Server_AttemptAttack] {name} FAILED - Target null or dead");
                return false;
            }

            if (!IsInAttackRange(target))
            {
                Debug.Log($"[AAttackable-Server_AttemptAttack] {name} FAILED - Out of range");
                return false;
            }

            var targetMb = target as MonoBehaviour;
            if (!IsValidTargetGO(targetMb ? targetMb.gameObject : null))
            {
                Debug.Log($"[AAttackable-Server_AttemptAttack] {name} FAILED - Invalid target GO");
                return false;
            }

            // Lock attack state
            SetState(InteractionState.Disabled);
            Debug.Log($"[AAttackable-Server_AttemptAttack] {name} Attack locked, processing...");

            try
            {
                float damage = CalculateDamage(target);
                Debug.Log($"[AAttackable-Server_AttemptAttack] {name} Calculated damage: {damage}");

                float appliedDamage = target.TakeDamage(this, damage, primaryDamageType);
                Debug.Log($"[AAttackable-Server_AttemptAttack] {name} Applied damage: {appliedDamage}");

                networkNextAttackServerTime.Value = NetworkManager.Singleton.ServerTime.Time + attackCooldown;

                OnAttackPerformed?.Invoke(this, target, appliedDamage);

                var p = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
                };
                OnAttackFeedbackClientRpc(appliedDamage, p);

                Debug.Log($"[AAttackable-Server_AttemptAttack] {name} SUCCESS - Damage: {appliedDamage}");
                return appliedDamage > 0f;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AAttackable-Server_AttemptAttack] {name} ERROR: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
            finally
            {
                SetState(InteractionState.Enable);
                Debug.Log($"[AAttackable-Server_AttemptAttack] {name} Attack unlocked");
            }
        }

        [ClientRpc]
        protected virtual void OnAttackFeedbackClientRpc(float actualDamage, ClientRpcParams p = default)
        {
            Debug.Log($"[AAttackable-OnAttackFeedbackClientRpc] {name} Received feedback: {actualDamage}");
            OnAttackFeedbackLocal(actualDamage);
        }

        public virtual bool IsInAttackRange(IDefendable target) =>
            target != null && Vector3.Distance(Position, target.Position) <= attackRange;

        public virtual float CalculateDamage(IDefendable target) => BaseDamage;

        // Collision Attack (server-authority)
        protected virtual void OnTriggerEnter(Collider other)
        {
            if (!IsServer)
            {
                Debug.Log($"[AAttackable-OnTriggerEnter] {name} SKIPPED - Not server");
                return;
            }
            
            Debug.Log($"[AAttackable-OnTriggerEnter] {name} triggered by: {other.name}");

            if (!enableCollisionAttack || hasCollisionAttacked || !CanAttack)
            {
                Debug.Log($"[AAttackable-OnTriggerEnter] {name} SKIPPED - Conditions not met");
                return;
            }


            Server_ProcessCollision(other);
        }

        protected virtual void OnCollisionEnter(Collision collision)
        {
            
            if (!IsServer)
            {
                Debug.Log($"[AAttackable-OnCollisionEnter] {name} SKIPPED - Not server");
                return;
            }
            
            if (!enableCollisionAttack || hasCollisionAttacked || !CanAttack)
            {
                Debug.Log($"[AAttackable-OnCollisionEnter] {name} SKIPPED - Conditions not met");
                return;
            }

            Server_ProcessCollision(collision.collider);
        }

        protected virtual void Server_ProcessCollision(Collider other)
        {
            if (other == null)
            {
                Debug.Log(
                    $"[AAttackable-Server_ProcessCollision] {name} FAILED - Other null: {other == null}");
                return;
            }

            var go = other.attachedRigidbody ? other.attachedRigidbody.gameObject : other.gameObject;

            if (go == null || go == this.gameObject)
            {
                Debug.Log($"[AAttackable-Server_ProcessCollision] {name} FAILED - GO is null or self");
                return;
            }

            bool isValid = IsValidTargetGO(go);
            Debug.Log($"[AAttackable-Server_ProcessCollision] {name} IsValidTarget: {isValid}");

            if (!isValid)
            {
                Debug.Log($"[AAttackable-Server_ProcessCollision] {name} Calling OnHitInvalidTarget");
                OnHitInvalidTarget(other);
                return;
            }

            bool hasDefendable = go.TryGetComponent<IDefendable>(out var target);
            Debug.Log($"[AAttackable-Server_ProcessCollision] {name} HasDefendable: {hasDefendable}");

            if (!hasDefendable)
            {
                OnHitNonDefendableTarget(other);
                return;
            }

            Debug.Log(
                $"[AAttackable-Server_ProcessCollision] {name} Target IsAlive: {target.IsAlive}, InRange: {IsInAttackRange(target)}");

            if (!target.IsAlive) // !IsInAttackRange(target))
            {
                Debug.Log($"[AAttackable-Server_ProcessCollision] {name} FAILED - Target dead or out of range");
                return;
            }

            hasCollisionAttacked = true;
            SetState(InteractionState.Disabled);
            Debug.Log($"[AAttackable-Server_ProcessCollision] {name} Collision attack locked, processing damage...");

            try
            {
                float damage = CalculateDamage(target);
                Debug.Log($"[AAttackable-Server_ProcessCollision] {name} Calculated damage: {damage}");

                float actualDamage = target.TakeDamage(this, damage, primaryDamageType);
                Debug.Log($"[AAttackable-Server_ProcessCollision] {name} Actual damage: {actualDamage}");

                networkNextAttackServerTime.Value = NetworkManager.Singleton.ServerTime.Time + attackCooldown;

                OnAttackPerformed?.Invoke(this, target, actualDamage);
                OnAttackFeedbackClientRpc(actualDamage);

                if (destroyAfterCollisionAttack)
                {
                    Debug.Log($"[AAttackable-Server_ProcessCollision] {name} Calling HandleDestruction");
                    HandleDestruction();
                }
                else
                {
                    SetState(InteractionState.Enable);
                    Debug.Log($"[AAttackable-Server_ProcessCollision] {name} Re-enabled for next attack");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AAttackable-Server_ProcessCollision] {name} ERROR: {ex.Message}\n{ex.StackTrace}");
                SetState(InteractionState.Enable);
            }
            finally
            {
                Debug.Log($"[AAttackable-Server_ProcessCollision] {name} Collision attack unlocked");
            }
        }

        protected virtual bool IsValidTargetGO(GameObject target)
        {
            Debug.Log($"[AAttackable-IsValidTargetGO] {name} Checking target: {target?.name}");

            if (target == null)
            {
                Debug.Log($"[AAttackable-IsValidTargetGO] {name} FAILED - Target is null");
                return false;
            }

            // Layer check
            int targetLayerMask = 1 << target.layer;
            bool layerMatch = (targetLayerMask & attackableLayers) != 0;
            Debug.Log(
                $"[AAttackable-IsValidTargetGO] {name} Layer check - Target layer: {LayerMask.LayerToName(target.layer)}, Match: {layerMatch}");

            if (!layerMatch)
            {
                Debug.Log($"[AAttackable-IsValidTargetGO] {name} FAILED - Layer mismatch");
                return false;
            }

            // Tag check
            if (attackableTags != null && attackableTags.Length > 0)
            {
                bool tagOk = false;
                for (int i = 0; i < attackableTags.Length; i++)
                {
                    if (target.CompareTag(attackableTags[i]))
                    {
                        tagOk = true;
                        Debug.Log($"[AAttackable-IsValidTargetGO] {name} Tag matched: {attackableTags[i]}");
                        break;
                    }
                }

                if (!tagOk)
                {
                    Debug.Log(
                        $"[AAttackable-IsValidTargetGO] {name} FAILED - Tag mismatch. Target tag: {target.tag}, Expected: {string.Join(", ", attackableTags)}");
                    return false;
                }
            }

            // Self-attack prevention
            if (target.TryGetComponent<NetworkObject>(out var nob))
            {
                bool isSameOwner = nob.OwnerClientId == OwnerClientId;
                Debug.Log(
                    $"[AAttackable-IsValidTargetGO] {name} Owner check - Target owner: {nob.OwnerClientId}, My owner: {OwnerClientId}, IsSame: {isSameOwner}");

                if (isSameOwner)
                {
                    Debug.Log($"[AAttackable-IsValidTargetGO] {name} FAILED - Same owner (self-attack prevention)");
                    return false;
                }
            }

            Debug.Log($"[AAttackable-IsValidTargetGO] {name} SUCCESS - Target is valid");
            return true;
        }

        // Hooks để override
        protected virtual void OnAttackFeedbackLocal(float actualDamage)
        {
            Debug.Log($"[AAttackable-OnAttackFeedbackLocal] {name} Feedback: {actualDamage}");
        }

        protected abstract void OnHitInvalidTarget(Collider other);
        protected abstract void OnHitNonDefendableTarget(Collider other);
        protected abstract void HandleDestruction();
    }

    // =========================
    // Combat System: Defendable (Health sync)
    // =========================
    public abstract class ADefendable : InteractableBase, IDefendable
    {
        [Header("Defense Settings")] [SerializeField]
        protected float maxHealth = 100f;

        [SerializeField] protected float defenseValue = 0f;
        [SerializeField] protected bool isInvulnerable = false;

        // NetVars: server-only write
        protected NetworkVariable<float> networkCurrentHealth =
            new NetworkVariable<float>(
                0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        protected NetworkVariable<bool> networkIsInvulnerable =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        protected NetworkVariable<double> networkLastHitServerTime =
            new NetworkVariable<double>(
                0d,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        // Sync death state properly
        protected NetworkVariable<bool> networkIsDead =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public virtual float CurrentHealth => networkCurrentHealth.Value;
        public float MaxHealth => maxHealth;
        public float DefenseValue => defenseValue;
        public virtual bool IsAlive => networkCurrentHealth.Value > 0f && !networkIsDead.Value;
        public bool IsInvulnerable => networkIsInvulnerable.Value;

        public event Action<float, float> OnHealthChanged; // (current, max)
        public event Action<IDefendable, IAttackable> OnDied; // (self, killer)

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            networkCurrentHealth.OnValueChanged += OnHealthNetworkChanged;
            networkIsInvulnerable.OnValueChanged += OnInvulnerabilityNetworkChanged;
            networkIsDead.OnValueChanged += OnDeathStateNetworkChanged;

            if (IsServer)
            {
                // Initialize health properly
                var initHealth = networkCurrentHealth.Value <= 0 ? maxHealth : networkCurrentHealth.Value;
                networkCurrentHealth.Value = Mathf.Clamp(initHealth, 0f, maxHealth);
                networkIsInvulnerable.Value = isInvulnerable;
                networkIsDead.Value = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (networkCurrentHealth != null)
                networkCurrentHealth.OnValueChanged -= OnHealthNetworkChanged;
            if (networkIsInvulnerable != null)
                networkIsInvulnerable.OnValueChanged -= OnInvulnerabilityNetworkChanged;
            if (networkIsDead != null)
                networkIsDead.OnValueChanged -= OnDeathStateNetworkChanged;
            base.OnNetworkDespawn();
        }

        // Server-only damage intake
        public virtual float TakeDamage(IAttackable attacker, float damage, DamageType damageType = DamageType.Physical)
        {
            if (!IsServer) return 0f;
            if (!IsAlive) return 0f;
            if (IsInvulnerable) return 0f;

            float finalDamage = CalculateFinalDamage(damage, damageType);
            float newHealth = Mathf.Max(0f, networkCurrentHealth.Value - finalDamage);
            networkCurrentHealth.Value = newHealth;

            networkLastHitServerTime.Value = NetworkManager.Singleton.ServerTime.Time;

            if (newHealth > 0f)
            {
                OnHitClientRpc(finalDamage);
            }
            else if (!networkIsDead.Value)
            {
                networkIsDead.Value = true;
                SetState(InteractionState.Disabled);

                OnDied?.Invoke(this, attacker);

                NetworkObject attackerNob = null;
                if (attacker is MonoBehaviour mb && mb.TryGetComponent(out NetworkObject aNob))
                    attackerNob = aNob;

                OnDeathClientRpc(new NetworkObjectReference(attackerNob));
                OnDeath(attacker); // server-side hook
            }

            return finalDamage;
        }

        // Server-only heal
        public virtual float Heal(float amount)
        {
            if (!IsServer) return 0f;
            if (networkIsDead.Value) return 0f;

            float prev = networkCurrentHealth.Value;
            float next = Mathf.Clamp(prev + Mathf.Abs(amount), 0f, maxHealth);
            networkCurrentHealth.Value = next;
            return next - prev;
        }

        public virtual void SetInvulnerable(bool invulnerable)
        {
            if (IsServer) networkIsInvulnerable.Value = invulnerable;
            else if (IsOwner) SetInvulnerableServerRpc(invulnerable);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetInvulnerableServerRpc(bool invulnerable, ServerRpcParams rpc = default)
        {
            if (!ValidateRpcSender(rpc.Receive.SenderClientId)) return;
            networkIsInvulnerable.Value = invulnerable;
        }

        [ServerRpc(RequireOwnership = false)]
        public void ReviveServerRpc(float reviveHealth = -1f, ServerRpcParams rpc = default)
        {
            if (!ValidateRpcSender(rpc.Receive.SenderClientId)) return;
            if (!IsServer) return;

            float hp = reviveHealth > 0 ? reviveHealth : maxHealth;
            networkIsDead.Value = false;
            networkCurrentHealth.Value = Mathf.Clamp(hp, 1f, maxHealth);
            SetState(InteractionState.Enable);
            OnRevivedClientRpc(networkCurrentHealth.Value);
        }

        protected virtual float CalculateFinalDamage(float baseDamage, DamageType damageType)
        {
            if (damageType == DamageType.True) return baseDamage;
            return Mathf.Max(1f, baseDamage - defenseValue);
        }

        // Client feedback RPCs
        [ClientRpc]
        protected virtual void OnHitClientRpc(float appliedDamage)
        {
            OnHitLocal(appliedDamage);
        }

        [ClientRpc]
        protected virtual void OnDeathClientRpc(NetworkObjectReference attackerRef)
        {
            NetworkObject attackerNob = null;
            attackerRef.TryGet(out attackerNob);
            OnDeathLocal(attackerNob);
        }

        [ClientRpc]
        protected virtual void OnRevivedClientRpc(float newHealth)
        {
            OnRevivedLocal(newHealth);
        }

        // NetVar change handlers
        private void OnHealthNetworkChanged(float previousHealth, float newHealth)
        {
            OnHealthChanged?.Invoke(newHealth, maxHealth);
            OnHealthChangedLocal(previousHealth, newHealth);
        }

        private void OnInvulnerabilityNetworkChanged(bool previousValue, bool newValue)
        {
            OnInvulnerabilityChangedLocal(previousValue, newValue);
        }

        private void OnDeathStateNetworkChanged(bool previousValue, bool newValue)
        {
            OnDeathStateChangedLocal(previousValue, newValue);
        }

        // Server-side hook sau khi chết
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

        protected virtual void OnDeathStateChangedLocal(bool previousValue, bool newValue)
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
    }

    // =========================
    // Event System for Better Error Handling
    // =========================
    public class InteractionEventArgs : EventArgs
    {
        public IInteractable Initiator { get; set; }
        public IInteractable Target { get; set; }
        public Vector3 InteractionPoint { get; set; }
        public float Distance { get; set; }
        public bool Success { get; set; }
        public string ErrorReason { get; set; }
        public double Timestamp { get; set; }
    }

    public class AttackEventArgs : EventArgs
    {
        public IAttackable Attacker { get; set; }
        public IDefendable Target { get; set; }
        public float BaseDamage { get; set; }
        public float AppliedDamage { get; set; }
        public DamageType DamageType { get; set; }
        public Vector3 AttackPoint { get; set; }
        public bool WasKilled { get; set; }
        public double Timestamp { get; set; }
    }

    // =========================
    // Utility Extensions
    // =========================
    public static class InteractionExtensions
    {
        /// <summary>
        /// Safe distance check with null validation
        /// </summary>
        public static float SafeDistanceTo(this IInteractable from, IInteractable to)
        {
            if (from == null || to == null) return float.MaxValue;
            return Vector3.Distance(from.Position, to.Position);
        }

        /// <summary>
        /// Check if interaction is within range with tolerance
        /// </summary>
        public static bool IsInRange(this IInteractable from, IInteractable to, float range, float tolerance = 0.5f)
        {
            return from.SafeDistanceTo(to) <= range + tolerance;
        }

        /// <summary>
        /// Safe cast to MonoBehaviour with NetworkObject check
        /// </summary>
        public static bool TryGetNetworkObject(this IInteractable interactable, out NetworkObject networkObject)
        {
            networkObject = null;
            if (interactable is MonoBehaviour mb)
            {
                networkObject = mb.GetComponent<NetworkObject>();
            }

            return networkObject != null;
        }
    }

    // =========================
    // Performance Monitoring (Optional)
    // =========================
    public static class InteractionMetrics
    {
        private static readonly Dictionary<string, int> interactionCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, float> averageDistances = new Dictionary<string, float>();

        public static void RecordInteraction(string interactionType, float distance)
        {
            if (!interactionCounts.ContainsKey(interactionType))
            {
                interactionCounts[interactionType] = 0;
                averageDistances[interactionType] = 0f;
            }

            var count = ++interactionCounts[interactionType];
            var avgDist = averageDistances[interactionType];
            averageDistances[interactionType] = (avgDist * (count - 1) + distance) / count;
        }

        public static void LogMetrics()
        {
            foreach (var kvp in interactionCounts)
            {
                Debug.Log(
                    $"[InteractionMetrics] {kvp.Key}: {kvp.Value} interactions, avg distance: {averageDistances[kvp.Key]:F2}");
            }
        }

        public static void ClearMetrics()
        {
            interactionCounts.Clear();
            averageDistances.Clear();
        }
    }
}
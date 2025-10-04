// ==================== FIXED PlayerAnimationSync ====================

using System.Collections.Generic;
using _GAME.Scripts.HideAndSeek;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace _GAME.Scripts.Player
{
    public class PlayerAnimationSync : NetworkBehaviour
    {
        [SerializeField] private Animator currentAnimator;
        [SerializeField] private string deathAnimationName = "Death";
        [SerializeField] private string reviveAnimationName = "Revive";

        public Animator CurrentAnimator
        {
            get
            {
                if (currentAnimator == null) currentAnimator = GetComponentInChildren<Animator>();
                return currentAnimator;
            }
            private set
            {
                if (value != null) currentAnimator = value;
            }
        }

        // Server is the authority for network variables
        private readonly NetworkVariable<float> networkXVelocity =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> networkZVelocity =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> networkYVelocity =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> networkIsGrounded =
            new(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> networkIsMoving =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly Dictionary<string, int> _hash = new();
        private double _nextSendTime;
        private const double SEND_INTERVAL = 1.0 / 20.0; // 20 Hz

        private void Awake()
        {
            Cache("xVelocity");
            Cache("zVelocity");
            Cache("yVelocity");
            Cache("isGrounded");
            Cache("isMoving");
        }

        public override void OnNetworkSpawn()
        {
            // Apply current state to animator when spawned
            ApplyAll();

            GameEvent.OnPlayerDeath += OnPlayerDeath;
            GameEvent.OnPlayerRevive += OnPlayerRevive;

            // FIX: Only NON-OWNERS listen to network variable changes
            // Owner uses local prediction and doesn't need to listen to network changes
            if (!IsOwner)
            {
                networkXVelocity.OnValueChanged += (_, v) =>
                {
                    if (CurrentAnimator) CurrentAnimator.SetFloat(_hash["xVelocity"], v);
                };
                networkZVelocity.OnValueChanged += (_, v) =>
                {
                    if (CurrentAnimator) CurrentAnimator.SetFloat(_hash["zVelocity"], v);
                };
                networkYVelocity.OnValueChanged += (_, v) =>
                {
                    if (CurrentAnimator) CurrentAnimator.SetFloat(_hash["yVelocity"], v);
                };
                networkIsGrounded.OnValueChanged += (_, v) =>
                {
                    if (CurrentAnimator) CurrentAnimator.SetBool(_hash["isGrounded"], v);
                };
                //networkIsMoving.OnValueChanged += (_, v) => { if (currentAnimator) currentAnimator.SetBool(_hash["isMoving"], v); };
            }
        }

        public override void OnNetworkDespawn()
        {
            // Unsubscribe from network variable changes
            if (!IsOwner)
            {
                networkXVelocity.OnValueChanged -= OnNetworkVariableChanged;
                networkZVelocity.OnValueChanged -= OnNetworkVariableChanged;
                networkYVelocity.OnValueChanged -= OnNetworkVariableChanged;
                networkIsGrounded.OnValueChanged -= OnNetworkVariableChangedBool;
                networkIsMoving.OnValueChanged -= OnNetworkVariableChangedBool;
            }

            GameEvent.OnPlayerDeath -= OnPlayerDeath;
            GameEvent.OnPlayerRevive -= OnPlayerRevive;
        }

        // Helper methods for unsubscribing
        private void OnNetworkVariableChanged(float prev, float curr)
        {
        }

        private void OnNetworkVariableChangedBool(bool prev, bool curr)
        {
        }

        public void SetAnimator(Animator animator)
        {
            CurrentAnimator = animator;
            ValidateAnimatorParameters();
            ApplyAll();
        }

        private void ApplyAll()
        {
            if (!CurrentAnimator) return;
            CurrentAnimator.SetFloat(_hash["xVelocity"], networkXVelocity.Value);
            CurrentAnimator.SetFloat(_hash["zVelocity"], networkZVelocity.Value);
            CurrentAnimator.SetFloat(_hash["yVelocity"], networkYVelocity.Value);
            CurrentAnimator.SetBool(_hash["isGrounded"], networkIsGrounded.Value);
            //currentAnimator.SetBool(_hash["isMoving"], networkIsMoving.Value);
        }

        private void Cache(string name) => _hash[name] = Animator.StringToHash(name);

        private void ValidateAnimatorParameters()
        {
            if (!CurrentAnimator) return;
            foreach (var kv in _hash)
            {
                bool ok = false;
                foreach (var p in CurrentAnimator.parameters)
                    if (p.nameHash == kv.Value)
                    {
                        ok = true;
                        break;
                    }

                if (!ok) Debug.LogWarning($"[AnimSync] Animator missing param: {kv.Key} on {gameObject.name}");
            }
        }

        /// <summary>
        /// FIX: Owner prediction - update local animator immediately for smooth animation,
        /// then sync to other clients via server (but not back to owner)
        /// </summary>
        public void UpdateMovementAnimation(float xVel, float zVel, float yVel, bool isGrounded)
        {
            if (!IsOwner) return;

            // 1) IMMEDIATE LOCAL UPDATE for owner (client-side prediction)
            //bool isMovingLocal = Mathf.Abs(xVel) > 0.1f || Mathf.Abs(zVel) > 0.1f;

            if (CurrentAnimator)
            {
                CurrentAnimator.SetFloat(_hash["xVelocity"], xVel);
                CurrentAnimator.SetFloat(_hash["zVelocity"], zVel);
                CurrentAnimator.SetFloat(_hash["yVelocity"], yVel);
                CurrentAnimator.SetBool(_hash["isGrounded"], isGrounded);
                //currentAnimator.SetBool(_hash["isMoving"], isMovingLocal);
            }

            // 2) Send to server for other clients (rate limited)
            var now = NetworkManager ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;
            if (now < _nextSendTime) return;
            _nextSendTime = now + SEND_INTERVAL;

            SubmitMovementServerRpc(xVel, zVel, yVel, isGrounded);
        }

        /// <summary>
        /// Server receives owner's animation data and syncs to OTHER clients only
        /// </summary>
        [ServerRpc]
        private void SubmitMovementServerRpc(float xVel, float zVel, float yVel, bool isGrounded)
        {
            // Server updates network variables - this will sync to NON-OWNER clients only
            // because owner doesn't listen to network variable changes (client prediction)
            networkXVelocity.Value = xVel;
            networkZVelocity.Value = zVel;
            networkYVelocity.Value = yVel;
            networkIsGrounded.Value = isGrounded;
            networkIsMoving.Value = Mathf.Abs(xVel) > 0.1f || Mathf.Abs(zVel) > 0.1f;
        }

        // ====== One-shot events (trigger) ======
        public void TriggerAnimation(string triggerName)
        {
            if (!IsOwner) return;

            // Owner triggers immediately for responsiveness
            if (CurrentAnimator)
            {
                int hash = _hash.TryGetValue(triggerName, out var v)
                    ? v
                    : (_hash[triggerName] = Animator.StringToHash(triggerName));
                CurrentAnimator.SetTrigger(hash);
            }

            // Send to server for other clients
            TriggerAnimationServerRpc(triggerName);
        }

        [ServerRpc]
        private void TriggerAnimationServerRpc(string triggerName)
        {
            // Send to all OTHER clients (not back to the owner)
            TriggerAnimationClientRpc(triggerName, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIdsNativeArray = GetNonOwnerClientIds()
                }
            });
        }

        [ClientRpc]
        private void TriggerAnimationClientRpc(string triggerName, ClientRpcParams clientRpcParams = default)
        {
            if (!CurrentAnimator) return;
            int hash = _hash.TryGetValue(triggerName, out var v)
                ? v
                : (_hash[triggerName] = Animator.StringToHash(triggerName));
            CurrentAnimator.SetTrigger(hash);
        }

        /// <summary>
        /// Get all client IDs except the owner for targeted ClientRpc
        /// </summary>
        private Unity.Collections.NativeArray<ulong> GetNonOwnerClientIds()
        {
            var allClients = NetworkManager.Singleton.ConnectedClientsIds;
            var nonOwnerClients = new List<ulong>();

            foreach (var clientId in allClients)
            {
                if (clientId != OwnerClientId)
                {
                    nonOwnerClients.Add(clientId);
                }
            }

            var result =
                new Unity.Collections.NativeArray<ulong>(nonOwnerClients.Count, Unity.Collections.Allocator.Temp);
            for (int i = 0; i < nonOwnerClients.Count; i++)
            {
                result[i] = nonOwnerClients[i];
            }

            return result;
        }


        private void OnPlayerDeath(string namePlayer, ulong idPlayer)
        {
            Debug.Log(
                $"[AnimSync] OnPlayerDeath: {namePlayer} ({idPlayer}), Local: {IsOwner}, Owner: {OwnerClientId}  Animator Valid {CurrentAnimator}");
            if (this.OwnerClientId == idPlayer && CurrentAnimator)
            {
                Debug.Log($"[AnimSync] Play death animation for {namePlayer}");
                //Play animation death
                CurrentAnimator.Play(deathAnimationName);
            }
        }

        private void OnPlayerRevive(string namePlayer, ulong idPlayer)
        {
            if (this.OwnerClientId == idPlayer && CurrentAnimator)
            {
                //Play animation death
                CurrentAnimator.Play(reviveAnimationName);
            }
        }
    }
}
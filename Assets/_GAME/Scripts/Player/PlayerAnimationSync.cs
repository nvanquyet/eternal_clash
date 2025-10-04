// ==================== OPTIMIZED PlayerAnimationSync ====================

using System.Collections.Generic;
using System.Linq;
using _GAME.Scripts.HideAndSeek;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Player
{
    public class PlayerAnimationSync : NetworkBehaviour
    {
        [SerializeField] private Animator currentAnimator;
        [SerializeField] private string deathAnimationName = "Death";
        [SerializeField] private string reviveAnimationName = "Revive";

        public Animator CurrentAnimator
        {
            get => currentAnimator ? currentAnimator : (currentAnimator = GetComponentInChildren<Animator>());
            private set => currentAnimator = value;
        }

        // Network variables
        private readonly NetworkVariable<float> networkXVelocity = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> networkZVelocity = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> networkYVelocity = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> networkIsGrounded = new(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Cached hashes
        private int _xVelocityHash;
        private int _zVelocityHash;
        private int _yVelocityHash;
        private int _isGroundedHash;
        
        // Reusable NativeArray for non-owner clients
        private NativeArray<ulong> _nonOwnerClientIds;
        private bool _nonOwnerArrayInitialized;

        private double _nextSendTime;
        private const double SEND_INTERVAL = 1.0 / 20.0; // 20 Hz

        private void Awake()
        {
            // Cache all hashes once
            _xVelocityHash = Animator.StringToHash("xVelocity");
            _zVelocityHash = Animator.StringToHash("zVelocity");
            _yVelocityHash = Animator.StringToHash("yVelocity");
            _isGroundedHash = Animator.StringToHash("isGrounded");
        }

        public override void OnNetworkSpawn()
        {
            ApplyCurrentState();

            GameEvent.OnPlayerDeath += OnPlayerDeath;
            GameEvent.OnPlayerRevive += OnPlayerRevive;

            // NON-OWNERS listen to network changes using inline lambdas (simpler)
            if (!IsOwner)
            {
                networkXVelocity.OnValueChanged += (_, v) => SetAnimatorFloat(_xVelocityHash, v);
                networkZVelocity.OnValueChanged += (_, v) => SetAnimatorFloat(_zVelocityHash, v);
                networkYVelocity.OnValueChanged += (_, v) => SetAnimatorFloat(_yVelocityHash, v);
                networkIsGrounded.OnValueChanged += (_, v) => SetAnimatorBool(_isGroundedHash, v);
            }
        }

        public override void OnNetworkDespawn()
        {
            // Cleanup
            if (_nonOwnerArrayInitialized && _nonOwnerClientIds.IsCreated)
            {
                _nonOwnerClientIds.Dispose();
                _nonOwnerArrayInitialized = false;
            }

            GameEvent.OnPlayerDeath -= OnPlayerDeath;
            GameEvent.OnPlayerRevive -= OnPlayerRevive;
        }

        public void SetAnimator(Animator animator)
        {
            CurrentAnimator = animator;
            if (animator) ValidateAnimatorParameters();
            ApplyCurrentState();
        }

        private void ApplyCurrentState()
        {
            if (!CurrentAnimator) return;
            
            CurrentAnimator.SetFloat(_xVelocityHash, networkXVelocity.Value);
            CurrentAnimator.SetFloat(_zVelocityHash, networkZVelocity.Value);
            CurrentAnimator.SetFloat(_yVelocityHash, networkYVelocity.Value);
            CurrentAnimator.SetBool(_isGroundedHash, networkIsGrounded.Value);
        }

        private void ValidateAnimatorParameters()
        {
            ValidateParameter(_xVelocityHash, "xVelocity");
            ValidateParameter(_zVelocityHash, "zVelocity");
            ValidateParameter(_yVelocityHash, "yVelocity");
            ValidateParameter(_isGroundedHash, "isGrounded");
        }

        private void ValidateParameter(int hash, string name)
        {
            foreach (var p in CurrentAnimator.parameters)
            {
                if (p.nameHash == hash) return;
            }
            Debug.LogWarning($"[AnimSync] Missing parameter '{name}' on {gameObject.name}");
        }

        // Helper methods to avoid repeated null checks
        private void SetAnimatorFloat(int hash, float value)
        {
            if (CurrentAnimator) CurrentAnimator.SetFloat(hash, value);
        }

        private void SetAnimatorBool(int hash, bool value)
        {
            if (CurrentAnimator) CurrentAnimator.SetBool(hash, value);
        }

        /// <summary>
        /// Owner prediction - immediate local update + server sync
        /// </summary>
        public void UpdateMovementAnimation(float xVel, float zVel, float yVel, bool isGrounded)
        {
            if (!IsOwner) return;

            // 1) Immediate local update
            if (CurrentAnimator)
            {
                CurrentAnimator.SetFloat(_xVelocityHash, xVel);
                CurrentAnimator.SetFloat(_zVelocityHash, zVel);
                CurrentAnimator.SetFloat(_yVelocityHash, yVel);
                CurrentAnimator.SetBool(_isGroundedHash, isGrounded);
            }

            // 2) Rate-limited server sync
            double now = NetworkManager ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;
            if (now < _nextSendTime) return;
            
            _nextSendTime = now + SEND_INTERVAL;
            SubmitMovementServerRpc(xVel, zVel, yVel, isGrounded);
        }

        [ServerRpc]
        private void SubmitMovementServerRpc(float xVel, float zVel, float yVel, bool isGrounded)
        {
            // Update network variables (syncs to non-owners via OnValueChanged)
            networkXVelocity.Value = xVel;
            networkZVelocity.Value = zVel;
            networkYVelocity.Value = yVel;
            networkIsGrounded.Value = isGrounded;
        }

        // ====== Trigger animations ======
        public void TriggerAnimation(string triggerName)
        {
            if (!IsOwner) return;

            int hash = Animator.StringToHash(triggerName);

            // Immediate local trigger
            if (CurrentAnimator) CurrentAnimator.SetTrigger(hash);

            // Sync to others
            TriggerAnimationServerRpc(triggerName);
        }

        [ServerRpc]
        private void TriggerAnimationServerRpc(string triggerName)
        {
            TriggerAnimationClientRpc(triggerName, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = GetNonOwnerClientIds() }
            });
        }

        [ClientRpc]
        private void TriggerAnimationClientRpc(string triggerName, ClientRpcParams clientRpcParams = default)
        {
            if (CurrentAnimator)
            {
                CurrentAnimator.SetTrigger(Animator.StringToHash(triggerName));
            }
        }

        /// <summary>
        /// Reusable array of non-owner client IDs (avoids allocation spam)
        /// </summary>
        private List<ulong> GetNonOwnerClientIds()
        {
            var allClients = NetworkManager.Singleton.ConnectedClientsIds;
            var count = 0;

            // Count non-owners
            foreach (var id in allClients)
            {
                if (id != OwnerClientId) count++;
            }

            // Recreate array only if size changed
            if (!_nonOwnerArrayInitialized || _nonOwnerClientIds.Length != count)
            {
                if (_nonOwnerArrayInitialized && _nonOwnerClientIds.IsCreated)
                {
                    _nonOwnerClientIds.Dispose();
                }
                _nonOwnerClientIds = new NativeArray<ulong>(count, Allocator.Persistent);
                _nonOwnerArrayInitialized = true;
            }

            // Fill array
            int index = 0;
            foreach (var id in allClients)
            {
                if (id != OwnerClientId)
                {
                    _nonOwnerClientIds[index++] = id;
                }
            }

            return _nonOwnerClientIds.ToList();
        }

        private void OnPlayerDeath(string namePlayer, ulong idPlayer)
        {
            if (OwnerClientId == idPlayer && CurrentAnimator)
            {
                CurrentAnimator.Play(deathAnimationName);
            }
        }

        private void OnPlayerRevive(string namePlayer, ulong idPlayer)
        {
            if (OwnerClientId == idPlayer && CurrentAnimator)
            {
                CurrentAnimator.Play(reviveAnimationName);
            }
        }
    }
}
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Player
{
    public class PlayerAnimationSync : NetworkBehaviour
    {
        [SerializeField] private Animator currentAnimator;

        // Network Variables for continuous sync
        private NetworkVariable<float> networkXVelocity = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private NetworkVariable<float> networkZVelocity = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private NetworkVariable<float> networkYVelocity = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private NetworkVariable<bool> networkIsGrounded = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private NetworkVariable<bool> networkIsMoving = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Performance optimization: Cache animation parameter hashes
        private readonly Dictionary<string, int> _parameterHashes = new Dictionary<string, int>();

        // Update rate control
        private float _lastSyncTime;
        private const float SYNC_RATE = 1f / 20f; // 20 updates per second

        private void Awake()
        {
            // Pre-cache common parameter hashes
            CacheParameterHash("xVelocity");
            CacheParameterHash("zVelocity");
            CacheParameterHash("yVelocity");
            CacheParameterHash("isGrounded");
            CacheParameterHash("isMoving");
            CacheParameterHash("speed");
        }

        public override void OnNetworkSpawn()
        {
            // Subscribe to network variable changes for non-owners
            if (!IsOwner)
            {
                networkXVelocity.OnValueChanged += OnXVelocityChanged;
                networkZVelocity.OnValueChanged += OnZVelocityChanged;
                networkYVelocity.OnValueChanged += OnYVelocityChanged;
                networkIsGrounded.OnValueChanged += OnGroundedChanged;
                networkIsMoving.OnValueChanged += OnMovingChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner)
            {
                networkXVelocity.OnValueChanged -= OnXVelocityChanged;
                networkZVelocity.OnValueChanged -= OnZVelocityChanged;
                networkYVelocity.OnValueChanged -= OnYVelocityChanged;
                networkIsGrounded.OnValueChanged -= OnGroundedChanged;
                networkIsMoving.OnValueChanged -= OnMovingChanged;
            }
        }

        private void CacheParameterHash(string paramName)
        {
            _parameterHashes[paramName] = Animator.StringToHash(paramName);
        }

        public void SetAnimator(Animator animator)
        {
            currentAnimator = animator;

            // Validate animator has required parameters
            if (currentAnimator != null)
            {
                ValidateAnimatorParameters();
                
                // Apply current network values to new animator if not owner
                if (!IsOwner)
                {
                    ApplyCurrentNetworkValues();
                }
            }
        }

        private void ValidateAnimatorParameters()
        {
            foreach (var param in _parameterHashes)
            {
                bool hasParameter = false;
                foreach (AnimatorControllerParameter controllerParam in currentAnimator.parameters)
                {
                    if (controllerParam.nameHash == param.Value)
                    {
                        hasParameter = true;
                        break;
                    }
                }

                if (!hasParameter)
                {
                    Debug.LogWarning($"Animator missing parameter: {param.Key} on {gameObject.name}");
                }
            }
        }

        private void ApplyCurrentNetworkValues()
        {
            if (currentAnimator == null) return;

            currentAnimator.SetFloat(_parameterHashes["xVelocity"], networkXVelocity.Value);
            currentAnimator.SetFloat(_parameterHashes["zVelocity"], networkZVelocity.Value);
            currentAnimator.SetFloat(_parameterHashes["yVelocity"], networkYVelocity.Value);
            currentAnimator.SetBool(_parameterHashes["isGrounded"], networkIsGrounded.Value);
            currentAnimator.SetBool(_parameterHashes["isMoving"], networkIsMoving.Value);
        }

        // Network variable change callbacks
        private void OnXVelocityChanged(float previous, float current)
        {
            if (currentAnimator != null)
                currentAnimator.SetFloat(_parameterHashes["xVelocity"], current);
        }

        private void OnZVelocityChanged(float previous, float current)
        {
            if (currentAnimator != null)
                currentAnimator.SetFloat(_parameterHashes["zVelocity"], current);
        }

        private void OnYVelocityChanged(float previous, float current)
        {
            if (currentAnimator != null)
                currentAnimator.SetFloat(_parameterHashes["yVelocity"], current);
        }

        private void OnGroundedChanged(bool previous, bool current)
        {
            if (currentAnimator != null)
                currentAnimator.SetBool(_parameterHashes["isGrounded"], current);
        }

        private void OnMovingChanged(bool previous, bool current)
        {
            if (currentAnimator != null)
                currentAnimator.SetBool(_parameterHashes["isMoving"], current);
        }

        public Animator GetCurrentAnimator() => currentAnimator;

        // Public methods for updating animation parameters (called by PlayerController)
        public void UpdateMovementAnimation(float xVel, float zVel, float yVel, bool isGrounded)
        {
            // Only owner can update network variables
            if (!IsOwner) return;

            // Rate limiting to avoid spam
            if (Time.time - _lastSyncTime < SYNC_RATE) return;
            _lastSyncTime = Time.time;

            // Update network variables (this will automatically sync to all clients)
            networkXVelocity.Value = xVel;
            networkZVelocity.Value = zVel;
            networkYVelocity.Value = yVel;
            networkIsGrounded.Value = isGrounded;
            var isMoving = Mathf.Abs(xVel) > 0.1f || Mathf.Abs(xVel) > 0.1f;
            networkIsMoving.Value = isMoving;

            // Update local animator immediately for owner
            if (currentAnimator != null)
            {
                currentAnimator.SetFloat(_parameterHashes["xVelocity"], xVel);
                currentAnimator.SetFloat(_parameterHashes["zVelocity"], zVel);
                currentAnimator.SetFloat(_parameterHashes["yVelocity"], yVel);
                currentAnimator.SetBool(_parameterHashes["isGrounded"], isGrounded);
                currentAnimator.SetBool(_parameterHashes["isMoving"], isMoving);
            }
        }

        // For one-shot animations like attacks, jumps, etc.
        [Rpc(SendTo.Everyone)]
        private void TriggerAnimationRpc(string triggerName)
        {
            if (currentAnimator == null) return;

            if (!_parameterHashes.ContainsKey(triggerName))
            {
                _parameterHashes[triggerName] = Animator.StringToHash(triggerName);
            }

            currentAnimator.SetTrigger(_parameterHashes[triggerName]);
        }

        [Rpc(SendTo.Everyone)]
        private void SetBoolParameterRpc(string paramName, bool value)
        {
            if (currentAnimator == null) return;

            if (!_parameterHashes.ContainsKey(paramName))
            {
                _parameterHashes[paramName] = Animator.StringToHash(paramName);
            }

            currentAnimator.SetBool(_parameterHashes[paramName], value);
        }

        [Rpc(SendTo.Everyone)]
        private void SetFloatParameterRpc(string paramName, float value)
        {
            if (currentAnimator == null) return;

            if (!_parameterHashes.ContainsKey(paramName))
            {
                _parameterHashes[paramName] = Animator.StringToHash(paramName);
            }

            currentAnimator.SetFloat(_parameterHashes[paramName], value);
        }

        // Public methods for triggering animations
        public void TriggerAnimation(string triggerName)
        {
            if (!IsOwner) return;
            TriggerAnimationRpc(triggerName);
        }

        public void SetBoolParameter(string paramName, bool value)
        {
            if (!IsOwner) return;
            SetBoolParameterRpc(paramName, value);
        }

        public void SetFloatParameter(string paramName, float value)
        {
            if (!IsOwner) return;
            SetFloatParameterRpc(paramName, value);
        }
    }
}
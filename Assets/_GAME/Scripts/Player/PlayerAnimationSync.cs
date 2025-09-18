using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Player
{
    public class PlayerAnimationSync : NetworkBehaviour
    {
        [SerializeField] private Animator _currentAnimator;

        // Performance optimization: Cache animation parameter hashes
        private readonly Dictionary<string, int> _parameterHashes = new Dictionary<string, int>();

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

        private void CacheParameterHash(string paramName)
        {
            _parameterHashes[paramName] = Animator.StringToHash(paramName);
        }

        public void SetAnimator(Animator animator)
        {
            _currentAnimator = animator;

            // Validate animator has required parameters
            if (_currentAnimator != null)
            {
                ValidateAnimatorParameters();
            }
        }

        private void ValidateAnimatorParameters()
        {
            foreach (var param in _parameterHashes)
            {
                bool hasParameter = false;
                foreach (AnimatorControllerParameter controllerParam in _currentAnimator.parameters)
                {
                    if (controllerParam.nameHash == param.Value)
                    {
                        hasParameter = true;
                        break;
                    }
                }

                if (!hasParameter)
                {
                    Debug.LogWarning($"Animator missing parameter: {param.Key}");
                }
            }
        }

        public Animator GetCurrentAnimator() => _currentAnimator;

        // Optimized RPC methods
        [Rpc(SendTo.NotOwner)]
        public void SyncAnimationRpc(float xVel, float zVel, float yVel, bool isGrounded)
        {
            if (_currentAnimator == null) return;

            _currentAnimator.SetFloat(_parameterHashes["xVelocity"], xVel);
            _currentAnimator.SetFloat(_parameterHashes["zVelocity"], zVel);
            _currentAnimator.SetFloat(_parameterHashes["yVelocity"], yVel);
            _currentAnimator.SetBool(_parameterHashes["isGrounded"], isGrounded);
        }

        [Rpc(SendTo.NotOwner)]
        public void SyncTriggerRpc(string triggerName)
        {
            if (_currentAnimator != null)
            {
                if (!_parameterHashes.ContainsKey(triggerName))
                {
                    _parameterHashes[triggerName] = Animator.StringToHash(triggerName);
                }

                _currentAnimator.SetTrigger(_parameterHashes[triggerName]);
            }
        }

        [Rpc(SendTo.NotOwner)]
        public void SyncBoolRpc(string paramName, bool value)
        {
            if (_currentAnimator != null)
            {
                if (!_parameterHashes.ContainsKey(paramName))
                {
                    _parameterHashes[paramName] = Animator.StringToHash(paramName);
                }

                _currentAnimator.SetBool(_parameterHashes[paramName], value);
            }
        }

        [Rpc(SendTo.NotOwner)]
        public void SyncFloatRpc(string paramName, float value)
        {
            if (_currentAnimator != null)
            {
                if (!_parameterHashes.ContainsKey(paramName))
                {
                    _parameterHashes[paramName] = Animator.StringToHash(paramName);
                }

                _currentAnimator.SetFloat(_parameterHashes[paramName], value);
            }
        }
    }
}
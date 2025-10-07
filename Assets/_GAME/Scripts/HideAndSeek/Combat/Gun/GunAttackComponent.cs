using System;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using _GAME.Scripts.HideAndSeek.Player.Rig;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Combat.Gun
{
    [RequireComponent(typeof(GunInputComponent))]
    [RequireComponent(typeof(GunMagazineComponent))]
    [RequireComponent(typeof(EffectsComponent))]
    public class GunAttackComponent : AttackComponent
    {
        [Header("Refs")]
        [SerializeField] private GunInputComponent inputComponent;
        [SerializeField] private GunMagazineComponent magazineComponent;
        [SerializeField] private GunReloadComponent gunReloadComponent;
        [SerializeField] private EffectsComponent effectsComponent;
        [SerializeField] private WeaponInteraction weaponInteraction;

        [Header("Fire Config")] 
        [SerializeField] private bool clientSideFXPrediction = true;
        [SerializeField] private GameObject laserSightObj;

        [Header("Projectile")]
        [SerializeField] private AProjectile bulletPrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float bulletSpeed = 100f;
        
        [Header("Debug")]
        [SerializeField] private bool debugLog = false;
        
        private bool _isAiming = false;
        
        public override bool CanAttack
        {
            get
            {
                if (magazineComponent != null && !magazineComponent.HasAmmo && 
                    gunReloadComponent != null && gunReloadComponent.IsReloading) 
                    return false;
                return LocalCooldownReady;
            }
        }

        public override void Initialize()
        {
            if (firePoint == null) firePoint = transform;
            
            if (debugLog) Debug.Log("[GunAttack] Initialize - Subscribe events");
            
            if (inputComponent != null)
            {
                inputComponent.OnActionAttackStarted += OnAimStarted;
                inputComponent.OnAttackPerformed += OnQuickFire;
                inputComponent.OnHoldFirePerformed += OnAimFirePerformed;
            }
            
            base.Initialize();
        }

        public override void Cleanup()
        {
            base.Cleanup();
            
            if (inputComponent != null)
            {
                inputComponent.OnActionAttackStarted -= OnAimStarted;
                inputComponent.OnAttackPerformed -= OnQuickFire;
                inputComponent.OnHoldFirePerformed -= OnAimFirePerformed;
            }
        }

        private void OnQuickFire()
        {
            if (debugLog) Debug.Log($"[GunAttack] QUICK FIRE (TAP) - No zoom, CanAttack={CanAttack}");
            
            if (!CanAttack)
            {
                if (magazineComponent != null && !magazineComponent.HasAmmo)
                {
                    if (debugLog) Debug.LogWarning("[GunAttack] No ammo for quick fire!");
                    if (clientSideFXPrediction) 
                        effectsComponent?.PlayEmptySound();
                }
                return;
            }
            
            OnPreFire?.Invoke(false);
            Invoke(nameof(FireWeapon), 0.12f);
        }

        private void OnAimStarted()
        {
            if (_isAiming)
            {
                if (debugLog) Debug.LogWarning("[GunAttack] Already aiming!");
                return;
            }
            
            _isAiming = true;
            
            if (debugLog) Debug.Log($"[GunAttack] AIM STARTED (HOLD) - Zoom camera, CanAttack={CanAttack}");
            
            if (laserSightObj != null)
            {
                laserSightObj.SetActive(true);
            }
            
            if (!CanAttack)
            {
                if (magazineComponent != null && !magazineComponent.HasAmmo)
                {
                    if (debugLog) Debug.LogWarning("[GunAttack] No ammo while aiming!");
                    if (clientSideFXPrediction) 
                        effectsComponent?.PlayEmptySound();
                }
                return;
            }
            
            OnPreFire?.Invoke(true);
        }

        private void OnAimFirePerformed()
        {
            if (debugLog) Debug.Log($"[GunAttack] AIM FIRE (HOLD RELEASE) - IsAiming={_isAiming}");
            
            if (_isAiming)
            {
                _isAiming = false;
                if (laserSightObj != null)
                {
                    laserSightObj.SetActive(false);
                }
            }
            
            if (!CanAttack)
            {
                if (magazineComponent != null && !magazineComponent.HasAmmo)
                {
                    if (debugLog) Debug.LogWarning("[GunAttack] No ammo when aim firing!");
                    if (clientSideFXPrediction) 
                        effectsComponent?.PlayEmptySound();
                }
                return;
            }
            FireWeapon();
        }

        private void FireWeapon()
        {
            var origin = firePoint ? firePoint.position : transform.position;
            var dir = firePoint ? firePoint.forward : transform.forward;

            if (clientSideFXPrediction)
            {
                effectsComponent?.PlayAttackEffects();
                effectsComponent?.PlayAttackSound();
            }

            RequestFireServerRpc(origin, dir);
            Invoke(nameof(TriggerFireCallback), 0.12f);
        }

        private void TriggerFireCallback()
        {
            OnFire?.Invoke();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestFireServerRpc(Vector3 origin, Vector3 direction, ServerRpcParams rpcParams = default)
        {
            if (!IsOwnerClient(rpcParams.Receive.SenderClientId))
            {
                if (debugLog) Debug.LogWarning("[GunAttack] Server rejected: Invalid owner");
                return;
            }

            if (!ServerCanFire()) 
            {
                if (debugLog) Debug.LogWarning("[GunAttack] Server rejected: Cooldown not ready");
                if (magazineComponent != null && !magazineComponent.HasAmmo)
                    PlayEmptyClientRpc();
                return;
            }

            if (magazineComponent != null && !magazineComponent.TryConsumeAmmo())
            {
                if (debugLog) Debug.LogWarning("[GunAttack] Server rejected: No ammo");
                PlayEmptyClientRpc();
                return;
            }

            SpawnBullet(origin, direction);
            LastFireServerTime.Value = NetworkManager.ServerTime.Time;
            PlayFireFXClientRpc(origin, direction);
            
            if (debugLog) Debug.Log("[GunAttack] Server: Fire successful");
        }

        private bool ServerCanFire()
        {
            var now = NetworkManager.ServerTime.Time;
            if (now < LastFireServerTime.Value + TimeBetweenShots) 
                return false;
            return true;
        }

        private void SpawnBullet(Vector3 origin, Vector3 direction)
        {
            if (bulletPrefab == null) return;

            var bullet = Instantiate(bulletPrefab, null, false);
            bullet.SetBaseDamage(BaseDamage);
            bullet.NetworkObject.SpawnWithOwnership(this.OwnerClientId, true);
            bullet.Initialize(origin, direction);

            if (bullet.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.linearVelocity = direction.normalized * bulletSpeed;
            }
        }

        [ClientRpc]
        private void PlayFireFXClientRpc(Vector3 origin, Vector3 direction)
        {
            if (clientSideFXPrediction && IsOwner) return;

            effectsComponent?.PlayAttackEffects();
            if (!IsOwner) 
                effectsComponent?.PlayAttackSound();
        }

        [ClientRpc]
        private void PlayEmptyClientRpc()
        {
            if (!IsOwner) 
                effectsComponent?.PlayEmptySound();
        }

        private bool IsOwnerClient(ulong senderClientId)
        {
            var rootNob = GetComponentInParent<NetworkObject>();
            if (rootNob != null) return rootNob.OwnerClientId == senderClientId;

            if (TryGetComponent<NetworkObject>(out var selfNob))
                return selfNob.OwnerClientId == senderClientId;

            return false;
        }

        public override void Attack(Vector3 direction, Vector3 origin)
        {
            // Deprecated
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!inputComponent) inputComponent = GetComponent<GunInputComponent>();
            if (!magazineComponent) magazineComponent = GetComponent<GunMagazineComponent>();
            if (!effectsComponent) effectsComponent = GetComponent<EffectsComponent>();
        }
#endif
    }
}
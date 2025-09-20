using System;
using _GAME.Scripts.HideAndSeek.Combat.Base;
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
        [SerializeField] private EffectsComponent effectsComponent;

        [Header("Fire Config")] 
        [SerializeField] private bool clientSideFXPrediction = true;

        [Header("Projectile")]
        [SerializeField] private AProjectile bulletPrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float bulletSpeed = 100f;
       
        public override bool CanAttack
        {
            get
            {
                if (magazineComponent != null && !magazineComponent.HasAmmo) return false;
                return LocalCooldownReady; // note: this is server time replicated → an toàn cho client check “gần đúng”
            }
        }

        public override void Initialize()
        {
            base.Initialize();

            if (firePoint == null) firePoint = transform;

            if (inputComponent != null)
            {
                inputComponent.OnAttackPerformed += OnLocalAttackInput;
            }
        }

        public override void Cleanup()
        {
            base.Cleanup();
            if (inputComponent != null)
            {
                inputComponent.OnAttackPerformed -= OnLocalAttackInput;
            }
        }

        private void OnLocalAttackInput()
        {
            Debug.Log($"[GunAttack] OnLocalAttackInput CanAttack={CanAttack}, HasAmmo={magazineComponent?.HasAmmo} Current Ammo={magazineComponent?.CurrentAmmo}");
            if (!CanAttack)                     // chặn local-spam
            {
                if (magazineComponent != null && !magazineComponent.HasAmmo)
                {
                    Debug.Log("GunAttack: No ammo!");
                    if(clientSideFXPrediction) effectsComponent?.PlayEmptySound();
                }
                return;
            }

            // Lấy hướng/điểm bắn phía client (máy chủ sẽ vẫn quyết định cuối)
            var origin = firePoint ? firePoint.position : transform.position;
            var dir = firePoint ? firePoint.forward : transform.forward;

            // Optional: prediction FX (mượt cảm giác bấm)
            if (clientSideFXPrediction)
            {
                effectsComponent?.PlayAttackEffects();
                effectsComponent?.PlayAttackSound();
            }

            // Gửi yêu cầu lên server
            RequestFireServerRpc(origin, dir);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestFireServerRpc(Vector3 origin, Vector3 direction, ServerRpcParams rpcParams = default)
        {
            // (1) Xác thực: chỉ cho owner của vũ khí / chủ player hợp lệ bắn
            if (!IsOwnerClient(rpcParams.Receive.SenderClientId))
                return;

            // (2) Cooldown & Ammo (server quyết)
            if (!ServerCanFire()) 
            {
                // có thể gửi ClientRpc báo empty/cooldown nếu muốn
                if (magazineComponent != null && !magazineComponent.HasAmmo)
                    PlayEmptyClientRpc();
                return;
            }

            // (3) Tiêu thụ đạn
            if (magazineComponent != null && !magazineComponent.TryConsumeAmmo())
            {
                PlayEmptyClientRpc();
                return;
            }

            // (4) Spawn đạn (server authoritative)
            SpawnBullet(origin, direction);

            // (5) Cập nhật thời gian bắn cuối
            LastFireServerTime.Value = NetworkManager.ServerTime.Time;

            // (6) Phát hiệu ứng cho tất cả
            PlayFireFXClientRpc(origin, direction);
        }

        private bool ServerCanFire()
        {
            var now = NetworkManager.ServerTime.Time;
            if (now < LastFireServerTime.Value + TimeBetweenShots) return false;
            if (magazineComponent != null && !magazineComponent.HasAmmo) return false;
            return true;
        }

        private void SpawnBullet(Vector3 origin, Vector3 direction)
        {
            if (bulletPrefab == null) return;

            var rot = Quaternion.LookRotation(direction);
            var bullet = Instantiate(bulletPrefab, origin, rot);

            // thiết lập thông số đạn
            bullet.SetBaseDamage(BaseDamage);
            
            if (bullet.TryGetComponent<NetworkObject>(out var nob))
            {
                nob.Spawn(true);
            }

            // nếu AProjectile hỗ trợ vận tốc:
            if (bullet.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.linearVelocity = direction.normalized * bulletSpeed;
            }
        }

        [ClientRpc]
        private void PlayFireFXClientRpc(Vector3 origin, Vector3 direction)
        {
            // Bên owner nếu đã prediction thì có thể bỏ qua (đỡ double SFX).
            if (clientSideFXPrediction && IsOwner) return;

            effectsComponent?.PlayAttackEffects();
            effectsComponent?.PlayAttackSound();
        }

        [ClientRpc]
        private void PlayEmptyClientRpc()
        {
            // Có thể điều kiện hóa: chỉ gửi về owner, nhưng đơn giản phát cho tất cả thấy “click”
            if (IsOwner) effectsComponent?.PlayEmptySound();
        }

        // Xác thực: clientId có phải owner của vũ khí này không?
        private bool IsOwnerClient(ulong senderClientId)
        {
            // Trường hợp vũ khí nằm trong hierarchy Player owner:
            // Nếu súng là NetworkObject riêng → dùng OwnerClientId của chính nó (hoặc của Player gốc)
            var rootNob = GetComponentInParent<NetworkObject>();
            if (rootNob != null) return rootNob.OwnerClientId == senderClientId;

            // fallback: dùng của chính component (nếu là NO)
            if (TryGetComponent<NetworkObject>(out var selfNob))
                return selfNob.OwnerClientId == senderClientId;

            // Nếu không có NetworkObject ở đâu → từ chối
            return false;
        }

        public override void Attack(Vector3 direction, Vector3 origin)
        {
            // KHÔNG gọi trực tiếp từ client nữa.
            // Flow chuẩn: client gọi OnLocalAttackInput -> ServerRpc -> server quyết -> ClientRpc FX
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

using System;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Combat.Gun
{
    public class GunReloadComponent : NetworkBehaviour, IReloadSystem
    {
        [Header("Reload References")]
        [SerializeField] private GunInputComponent inputComponent;
        [SerializeField] private GunMagazineComponent magazineComponent;
        [SerializeField] private GunEffectComponent effectComponent;

        [Header("Reload Configuration")]
        [SerializeField] private float reloadTime = 2f;
        [SerializeField] private bool canInterruptReload = false;
        [SerializeField] private bool clientSideFXPrediction = true;

        private NetworkVariable<bool> networkIsReloading = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private NetworkVariable<double> networkReloadEndTime = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool IsInitialized { get; private set; }
        public bool IsReloading => networkIsReloading.Value;
        public float ReloadTime => reloadTime;
        public float ReloadProgress =>
            IsReloading
                ? Mathf.Clamp01(1f - (float)(networkReloadEndTime.Value - NetworkManager.ServerTime.Time) / reloadTime)
                : 0f;

        public event Action<bool> OnReloadStateChanged;
        public event Action OnReloadStarted;
        public event Action OnReloadCompleted;

        protected void Awake()
        {
            Initialize();
        }


        public void Initialize()
        {
            if (IsInitialized) return;
            if (inputComponent != null) inputComponent.OnReloadPerformed += OnLocalReloadInput;
            IsInitialized = true;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (inputComponent != null) inputComponent.OnReloadPerformed -= OnLocalReloadInput;
        }

        public void Cleanup()
        {
            networkIsReloading.OnValueChanged -= OnReloadStateValueChanged;
            IsInitialized = false;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            networkIsReloading.OnValueChanged += OnReloadStateValueChanged;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            Cleanup();
        }

        private void Update()
        {
            if (!IsServer || !IsReloading) return;

            if (NetworkManager.ServerTime.Time >= networkReloadEndTime.Value)
            {
                CompleteReloadServer();
            }
        }

        private void OnReloadStateValueChanged(bool previousValue, bool newValue)
        {
            OnReloadStateChanged?.Invoke(newValue);
        }

        public bool CanReload()
        {
            if (magazineComponent == null) return false;
            if (magazineComponent.IsUnlimited) return false;
            return !IsReloading && !magazineComponent.IsFull;
        }

        public void StartReload() => RequestStartReloadServerRpc();

        private void OnLocalReloadInput()
        {
            Debug.Log($"[GunReload] Local reload input received. CanReload: {CanReload()}, IsOwner: {IsOwner}");
            if (!IsOwner) return;
            if (!CanReload())
                return;

            if (clientSideFXPrediction)
            {
                OnReloadStarted?.Invoke();
                effectComponent?.PlayReloadSound();
            }
            RequestStartReloadServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestStartReloadServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsOwnerClient(rpcParams.Receive.SenderClientId)) return;
            if (!CanReload()) return;

            networkIsReloading.Value = true;
            networkReloadEndTime.Value = NetworkManager.ServerTime.Time + reloadTime;

            // báo mọi người có thể phát animation/sound
            NotifyReloadStartClientRpc();
            OnReloadStarted?.Invoke(); // local callback trên server (nếu bạn cần)
        }

        public void InterruptReload()
        {
            if (!canInterruptReload) return;
            if (!IsOwner) return; // chỉ chủ súng yêu cầu
            RequestInterruptReloadServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestInterruptReloadServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsOwnerClient(rpcParams.Receive.SenderClientId)) return;
            if (!canInterruptReload || !IsReloading) return;

            networkIsReloading.Value = false;
            NotifyReloadInterruptClientRpc();
        }

        private void CompleteReloadServer()
        {
            if (magazineComponent != null) magazineComponent.RefillAmmo();
            networkIsReloading.Value = false;

            NotifyReloadCompletedClientRpc();
            OnReloadCompleted?.Invoke(); // server local
        }

        [ClientRpc]
        private void NotifyReloadStartClientRpc()
        {
            if (!clientSideFXPrediction || !IsOwner)
                OnReloadStarted?.Invoke();
            if(!IsOwner) effectComponent?.PlayReloadSound();
        }

        [ClientRpc]
        private void NotifyReloadCompletedClientRpc()
        {
            OnReloadCompleted?.Invoke();
        }

        [ClientRpc]
        private void NotifyReloadInterruptClientRpc()
        {
            // Nếu cần hiệu ứng/âm thanh hủy, phát tại đây
        }

        private bool IsOwnerClient(ulong senderClientId)
        {
            var rootNob = GetComponentInParent<NetworkObject>();
            if (rootNob != null) return rootNob.OwnerClientId == senderClientId;
            if (TryGetComponent<NetworkObject>(out var selfNob))
                return selfNob.OwnerClientId == senderClientId;
            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            inputComponent = GetComponent<GunInputComponent>(); 
            magazineComponent = GetComponent<GunMagazineComponent>();
        }
#endif
    }
}

using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Networking.UI
{
    /// <summary>
    /// Script để setup NetworkLoadingManager. Attach vào một GameObject có NetworkObject
    /// và đảm bảo nó survive scene changes.
    /// </summary>
    public class NetworkLoadingSetup : NetworkBehaviour
    {
        [Header("Auto Setup")]
        [SerializeField] private bool setupOnStart = true;
        [SerializeField] private bool dontDestroyOnLoad = true;

        private void Start()
        {
            if (setupOnStart)
            {
                SetupNetworkLoading();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            if (dontDestroyOnLoad)
            {
                // Đảm bảo object này survive scene changes
                if (transform.parent == null)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            
            Debug.Log("[NetworkLoadingSetup] Network spawned and setup completed");
        }

        private void SetupNetworkLoading()
        {
            // Kiểm tra xem đã có NetworkLoadingManager chưa
            NetworkLoadingManager existingManager = FindObjectOfType<NetworkLoadingManager>();
            if (existingManager == null)
            {
                // Thêm NetworkLoadingManager vào object này
                gameObject.AddComponent<NetworkLoadingManager>();
                Debug.Log("[NetworkLoadingSetup] NetworkLoadingManager added to " + gameObject.name);
            }
            else
            {
                Debug.Log("[NetworkLoadingSetup] NetworkLoadingManager already exists");
            }
        }

        // Method để test loading manually
        [ContextMenu("Test Show Loading")]
        public void TestShowLoading()
        {
            if (Application.isPlaying && IsServer)
            {
                NetworkLoadingManager manager = GetComponent<NetworkLoadingManager>();
                if (manager != null)
                {
                    manager.ShowLoadingServerRpc("Testing network loading...");
                }
            }
        }

        [ContextMenu("Test Hide Loading")]
        public void TestHideLoading()
        {
            if (Application.isPlaying && IsServer)
            {
                NetworkLoadingManager manager = GetComponent<NetworkLoadingManager>();
                if (manager != null)
                {
                    manager.HideLoadingServerRpc();
                }
            }
        }
    }
}
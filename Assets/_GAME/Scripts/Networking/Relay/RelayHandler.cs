using System;
using System.Threading;
using System.Threading.Tasks; 
using Unity.Netcode;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking.Relay
{
    /// <summary>
    /// Core Relay operations - chỉ xử lý logic, không quản lý state
    /// </summary>
    public static class RelayHandler
    {
        #region Host Operations

        /// <summary>
        /// Tạo relay allocation và trả về join code
        /// </summary>
        public static async Task<RelayResult<string>> SetupHostRelayAsync(int maxPlayers, CancellationToken cancellationToken = default)
        {
            try
            {
                var maxClients = Math.Max(0, maxPlayers - 1);
                Debug.Log($"[RelayCore] Setting up host relay for {maxClients} clients");

                var result = await RelayConnector.AllocateHostAsync(maxClients, cancellationToken);
                if (!result.IsSuccess)
                {
                    return RelayResult<string>.Failure($"Host allocation failed: {result.ErrorMessage}");
                }

                var (allocation, joinCode) = result.Data;
                Debug.Log($"[RelayCore] Host relay setup complete - Join Code: {joinCode}");
                
                return RelayResult<string>.Success(joinCode);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[RelayCore] Host relay setup cancelled");
                return RelayResult<string>.Failure("Operation cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayCore] Host relay setup failed: {ex.Message}");
                return RelayResult<string>.Failure($"Host setup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Khởi động NetworkManager host
        /// </summary>
        public static NetworkResult StartNetworkHost()
        {
            try
            {
                var networkManager = NetworkManager.Singleton;
                if (networkManager == null)
                {
                    return NetworkResult.Failure("NetworkManager not found");
                }

                if (!networkManager.StartHost())
                {
                    return NetworkResult.Failure("Failed to start NetworkManager host");
                }

                Debug.Log("[RelayCore] Network host started successfully");
                return NetworkResult.Success();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayCore] Network host start failed: {ex.Message}");
                return NetworkResult.Failure($"Network host start failed: {ex.Message}");
            }
        }

        #endregion

        #region Client Operations

        /// <summary>
        /// Kết nối client tới relay
        /// </summary>
        public static async Task<RelayResult<JoinAllocation>> ConnectClientToRelayAsync(string joinCode, CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(joinCode))
                {
                    return RelayResult<JoinAllocation>.Failure("Invalid join code");
                }

                Debug.Log($"[RelayCore] Connecting client to relay: {joinCode}");

                var result = await RelayConnector.JoinAsClientAsync(joinCode, cancellationToken);
                if (!result.IsSuccess)
                {
                    return RelayResult<JoinAllocation>.Failure($"Client relay connection failed: {result.ErrorMessage}");
                }

                Debug.Log("[RelayCore] Client connected to relay successfully");
                return result;
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[RelayCore] Client relay connection cancelled");
                return RelayResult<JoinAllocation>.Failure("Operation cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayCore] Client relay connection failed: {ex.Message}");
                return RelayResult<JoinAllocation>.Failure($"Client connection failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Khởi động NetworkManager client
        /// </summary>
        public static NetworkResult StartNetworkClient()
        {
            try
            {
                var networkManager = NetworkManager.Singleton;
                if (networkManager == null)
                {
                    return NetworkResult.Failure("NetworkManager not found");
                }

                if (!networkManager.StartClient())
                {
                    return NetworkResult.Failure("Failed to start NetworkManager client");
                }

                Debug.Log("[RelayCore] Network client started successfully");
                return NetworkResult.Success();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayCore] Network client start failed: {ex.Message}");
                return NetworkResult.Failure($"Network client start failed: {ex.Message}");
            }
        }

        #endregion

        #region Utility Operations

        /// <summary>
        /// Shutdown network connection
        /// </summary>
        public static async Task<bool> ShutdownNetworkAsync()
        {
            try
            {
                Debug.Log("[RelayCore] Shutting down network...");
                
                // Cancel any ongoing relay operations
                RelayConnector.CancelCurrentOperation();
                
                var networkManager = NetworkManager.Singleton;
                if (networkManager != null && (networkManager.IsHost || networkManager.IsClient))
                {
                    networkManager.Shutdown();
                    await Task.Delay(500); // Wait for cleanup
                }

                // Reset relay state
                RelayConnector.ResetState();
                
                Debug.Log("[RelayCore] Network shutdown complete");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayCore] Network shutdown failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Shutdown network một cách an toàn
        /// </summary>
        public static async Task<bool> SafeShutdownAsync()
        {
            try
            {
                // Cancel any ongoing operations
                RelayConnector.CancelCurrentOperation();
                
                var networkManager = NetworkManager.Singleton;
                if (networkManager != null && (networkManager.IsHost || networkManager.IsClient))
                {
                    Debug.Log("[NetworkStarter] Đang shutdown network...");
                    networkManager.Shutdown();
                    
                    // Đợi một chút để network cleanup
                    await Task.Delay(500);
                }

                // Reset relay state
                RelayConnector.ResetState();

                Debug.Log("[NetworkStarter] Network shutdown hoàn tất");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkStarter] Shutdown thất bại: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Check network status
        /// </summary>
        public static NetworkStatus GetNetworkStatus()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                return NetworkStatus.NotInitialized;

            if (networkManager.IsHost)
                return NetworkStatus.Host;
            
            if (networkManager.IsClient && networkManager.IsConnectedClient)
                return NetworkStatus.ClientConnected;
            
            if (networkManager.IsClient)
                return NetworkStatus.ClientConnecting;

            return NetworkStatus.Idle;
        }

        /// <summary>
        /// Reset all relay and network state
        /// </summary>
        public static void ResetAllState()
        {
            RelayConnector.ResetState();
            Debug.Log("[RelayCore] All state reset");
        }

        #endregion
    }

    #region Result Types

    public class NetworkResult
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }

        private NetworkResult(bool isSuccess, string errorMessage)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
        }

        public static NetworkResult Success() => new(true, null);
        public static NetworkResult Failure(string errorMessage) => new(false, errorMessage);
    }

    public enum NetworkStatus
    {
        NotInitialized,
        Idle,
        Host,
        ClientConnecting,
        ClientConnected,
        Failed
    }
    #endregion
}
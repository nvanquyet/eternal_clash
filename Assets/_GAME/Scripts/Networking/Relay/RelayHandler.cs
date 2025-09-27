// RelayHandler.cs — merged (RelayConnector.cs → RelayHandler.cs)
// Drop-in replacement that removes the extra indirection layer.
// Public API preserved:
//
// - Task<RelayResult<string>>  SetupHostRelayAsync(int maxPlayers, CancellationToken = default)
// - NetworkResult              StartNetworkHost()
// - Task<RelayResult<JoinAllocation>> ConnectClientToRelayAsync(string joinCode, CancellationToken = default)
// - NetworkResult              StartNetworkClient()
// - Task<bool>                 SafeShutdownAsync()
// - void                       ResetAllState()
// - void                       CancelCurrentOperation()
//
// Notes:
// - Uses internal connection state & cancellation (migrated from RelayConnector)
// - Selects DTLS endpoint when available, falls back to UDP
// - Configures UnityTransport with RelayServerData for host/client
// - Exposes RelayResult<T>, NetworkResult, NetworkStatus (unchanged)

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace _GAME.Scripts.Networking.Relay
{
    public static class RelayHandler
    {
        // ===== Internal state (migrated from RelayConnector) =====
        private static readonly object _lock = new object();
        private static CancellationTokenSource _currentOperationCts;
        private static RelayConnectionState _state = RelayConnectionState.Idle;

        private enum RelayConnectionState
        {
            Idle,
            Allocating,
            Joining,
            Connected,
            Failed
        }

        // ====== Public API (unchanged) =======================================

        /// <summary>
        /// Allocate a host Relay allocation and return the Join Code (does not start Netcode host).
        /// </summary>
        public static async Task<RelayResult<string>> SetupHostRelayAsync(
            int maxPlayers,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var maxClients = Math.Max(0, maxPlayers - 1);
                var allocResult = await AllocateHostAsync(maxClients, cancellationToken);

                if (!allocResult.IsSuccess)
                    return RelayResult<string>.Failure($"Host allocation failed: {allocResult.ErrorMessage}");

                var (_, joinCode) = allocResult.Data;
                return RelayResult<string>.Success(joinCode);
            }
            catch (OperationCanceledException)
            {
                return RelayResult<string>.Failure("Operation cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayHandler] SetupHostRelayAsync failed: {ex}");
                return RelayResult<string>.Failure(ex.Message);
            }
        }

        /// <summary>
        /// Starts the Netcode host (NetworkManager.StartHost()) after transport is configured.
        /// </summary>
        public static NetworkResult StartNetworkHost()
        {
            try
            {
                var nm = NetworkManager.Singleton;
                if (nm == null)
                    return NetworkResult.Failure("NetworkManager not found");

                if (!nm.StartHost())
                    return NetworkResult.Failure("Failed to start NetworkManager host");

                Debug.Log("[RelayHandler] Network host started");
                return NetworkResult.Success();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayHandler] StartNetworkHost failed: {ex}");
                return NetworkResult.Failure(ex.Message);
            }
        }

        /// <summary>
        /// Join a Relay allocation by join code and configure transport (does not start Netcode client).
        /// </summary>
        public static async Task<RelayResult<JoinAllocation>> ConnectClientToRelayAsync(
            string joinCode,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await JoinAsClientAsync(joinCode, cancellationToken);
                if (!result.IsSuccess)
                    return RelayResult<JoinAllocation>.Failure(result.ErrorMessage);

                return result;
            }
            catch (OperationCanceledException)
            {
                return RelayResult<JoinAllocation>.Failure("Operation cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayHandler] ConnectClientToRelayAsync failed: {ex}");
                return RelayResult<JoinAllocation>.Failure(ex.Message);
            }
        }

        /// <summary>
        /// Starts the Netcode client (NetworkManager.StartClient()) after transport is configured.
        /// </summary>
        public static NetworkResult StartNetworkClient()
        {
            try
            {
                var nm = NetworkManager.Singleton;
                if (nm == null)
                    return NetworkResult.Failure("NetworkManager not found");

                if (!nm.StartClient())
                    return NetworkResult.Failure("Failed to start NetworkManager client");

                Debug.Log("[RelayHandler] Network client started");
                return NetworkResult.Success();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayHandler] StartNetworkClient failed: {ex}");
                return NetworkResult.Failure(ex.Message);
            }
        }

        /// <summary>
        /// Gracefully shut down Netcode & reset Relay state.
        /// </summary>
        public static async Task<bool> SafeShutdownAsync()
        {
            try
            {
                CancelCurrentOperation();

                var nm = NetworkManager.Singleton;
                if (nm != null && (nm.IsHost || nm.IsClient))
                {
                    Debug.Log("[RelayHandler] Shutting down NetworkManager...");
                    nm.Shutdown();
                    await Task.Delay(500);
                }

                ResetAllState();
                Debug.Log("[RelayHandler] Safe shutdown complete");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayHandler] SafeShutdownAsync failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Cancel current allocation/join operation (if any).
        /// </summary>
        public static void CancelCurrentOperation()
        {
            lock (_lock)
            {
                _currentOperationCts?.Cancel();
            }
            Debug.Log("[RelayHandler] Current operation cancelled");
        }

        /// <summary>
        /// Reset internal Relay state & cancellation.
        /// </summary>
        public static void ResetAllState()
        {
            lock (_lock)
            {
                _currentOperationCts?.Cancel();
                _currentOperationCts?.Dispose();
                _currentOperationCts = null;
                _state = RelayConnectionState.Idle;
            }
            Debug.Log("[RelayHandler] Internal state reset");
        }

        // ====== Internal logic (from RelayConnector) =========================

        private static UnityTransport GetTransport()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
                throw new RelayException("NetworkManager not found");

            if (nm.NetworkConfig?.NetworkTransport is not UnityTransport transport)
                throw new RelayException("UnityTransport is not configured on NetworkManager");

            return transport;
        }

        private static RelayServerEndpoint SelectOptimalEndpoint(System.Collections.Generic.IList<RelayServerEndpoint> endpoints)
        {
            if (endpoints == null || endpoints.Count == 0)
                throw new RelayException("No Relay endpoints available");

            // Prefer DTLS
            var dtls = endpoints.FirstOrDefault(e =>
                string.Equals(e.ConnectionType, "dtls", StringComparison.OrdinalIgnoreCase));

            if (dtls != null)
            {
                Debug.Log("[RelayHandler] Using DTLS endpoint");
                return dtls;
            }

            // Fallback UDP
            var udp = endpoints.FirstOrDefault(e =>
                string.Equals(e.ConnectionType, "udp", StringComparison.OrdinalIgnoreCase));

            if (udp != null)
            {
                Debug.Log("[RelayHandler] Using UDP endpoint");
                return udp;
            }

            Debug.LogWarning("[RelayHandler] Using first available endpoint");
            return endpoints[0];
        }

        private static async Task<RelayResult<(Allocation allocation, string joinCode)>> AllocateHostAsync(
            int maxClientConnections,
            CancellationToken cancellationToken,
            int maxRetries = 3)
        {
            lock (_lock)
            {
                if (_state != RelayConnectionState.Idle)
                    return RelayResult<(Allocation, string)>.Failure($"Invalid state: {_state}");

                _state = RelayConnectionState.Allocating;
                _currentOperationCts?.Cancel();
                _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            try
            {
                Allocation allocation = null;
                string joinCode = null;
                Exception last = null;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        _currentOperationCts.Token.ThrowIfCancellationRequested();

                        allocation = await RelayService.Instance.CreateAllocationAsync(maxClientConnections);
                        joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                        Debug.Log($"[RelayHandler] Host allocation OK (attempt {attempt})");
                        break;
                    }
                    catch (Exception ex) when (attempt < maxRetries)
                    {
                        last = ex;
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                        Debug.LogWarning($"[RelayHandler] Host allocation attempt {attempt} failed: {ex.Message}. Retrying in {delay.TotalSeconds:0}s");
                        await Task.Delay(delay, _currentOperationCts.Token);
                    }
                }

                if (allocation == null || string.IsNullOrEmpty(joinCode))
                    throw last ?? new RelayException("Failed to allocate Relay host");

                await ConfigureTransportForHost(allocation);

                _state = RelayConnectionState.Connected;
                return RelayResult<(Allocation, string)>.Success((allocation, joinCode));
            }
            catch (OperationCanceledException)
            {
                return RelayResult<(Allocation, string)>.Failure("Operation cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayHandler] AllocateHostAsync failed: {ex}");
                return RelayResult<(Allocation, string)>.Failure(ex.Message);
            }
            finally
            {
                if (_state == RelayConnectionState.Allocating)
                    _state = RelayConnectionState.Failed;

                lock (_lock)
                {
                    _currentOperationCts?.Dispose();
                    _currentOperationCts = null;
                }
            }
        }

        private static async Task ConfigureTransportForHost(Allocation allocation)
        {
            await Task.Yield();

            var endpoint = SelectOptimalEndpoint(allocation.ServerEndpoints);
            var serverData = new RelayServerData(
                endpoint.Host,
                (ushort)endpoint.Port,
                allocation.AllocationIdBytes,
                allocation.ConnectionData,
                allocation.ConnectionData, // Host uses its own connection data for both
                allocation.Key,
                endpoint.Secure
            );

            var transport = GetTransport();
            transport.SetRelayServerData(serverData);

            Debug.Log($"[RelayHandler] Host transport configured: {endpoint.Host}:{endpoint.Port} (secure={endpoint.Secure})");
        }

        private static async Task<RelayResult<JoinAllocation>> JoinAsClientAsync(
            string joinCode,
            CancellationToken cancellationToken,
            int maxRetries = 3)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
                return RelayResult<JoinAllocation>.Failure("Invalid join code");

            lock (_lock)
            {
                if (_state != RelayConnectionState.Idle)
                    return RelayResult<JoinAllocation>.Failure($"Invalid state: {_state}");

                _state = RelayConnectionState.Joining;
                _currentOperationCts?.Cancel();
                _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            try
            {
                var normalized = joinCode.Trim().ToUpperInvariant();
                JoinAllocation joinAlloc = null;
                Exception last = null;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        _currentOperationCts.Token.ThrowIfCancellationRequested();

                        joinAlloc = await RelayService.Instance.JoinAllocationAsync(normalized);
                        Debug.Log($"[RelayHandler] Client join OK (attempt {attempt})");
                        break;
                    }
                    catch (Exception ex) when (attempt < maxRetries)
                    {
                        last = ex;
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                        Debug.LogWarning($"[RelayHandler] Client join attempt {attempt} failed: {ex.Message}. Retrying in {delay.TotalSeconds:0}s");
                        await Task.Delay(delay, _currentOperationCts.Token);
                    }
                }

                if (joinAlloc == null)
                    throw last ?? new RelayException("Failed to join Relay");

                await ConfigureTransportForClient(joinAlloc);

                _state = RelayConnectionState.Connected;
                return RelayResult<JoinAllocation>.Success(joinAlloc);
            }
            catch (OperationCanceledException)
            {
                return RelayResult<JoinAllocation>.Failure("Operation cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayHandler] JoinAsClientAsync failed: {ex}");
                return RelayResult<JoinAllocation>.Failure(ex.Message);
            }
            finally
            {
                if (_state == RelayConnectionState.Joining)
                    _state = RelayConnectionState.Failed;

                lock (_lock)
                {
                    _currentOperationCts?.Dispose();
                    _currentOperationCts = null;
                }
            }
        }

        private static async Task ConfigureTransportForClient(JoinAllocation joinAllocation)
        {
            await Task.Yield();

            var endpoint = SelectOptimalEndpoint(joinAllocation.ServerEndpoints);
            var serverData = new RelayServerData(
                endpoint.Host,
                (ushort)endpoint.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData,
                joinAllocation.Key,
                endpoint.Secure
            );

            var transport = GetTransport();
            transport.SetRelayServerData(serverData);

            Debug.Log($"[RelayHandler] Client transport configured: {endpoint.Host}:{endpoint.Port} (secure={endpoint.Secure})");
        }

        // ===== Helper types (were in RelayConnector / RelayHandler) ==========

        private class RelayException : Exception
        {
            public RelayException(string message) : base(message) { }
            public RelayException(string message, Exception inner) : base(message, inner) { }
        }
    }

    // ===== Result Types (unchanged public shapes) ============================

    public class RelayResult<T>
    {
        public bool IsSuccess { get; private set; }
        public T Data { get; private set; }
        public string ErrorMessage { get; private set; }

        private RelayResult(bool ok, T data, string error)
        {
            IsSuccess = ok;
            Data = data;
            ErrorMessage = error;
        }

        public static RelayResult<T> Success(T data) => new(true, data, null);
        public static RelayResult<T> Failure(string error) => new(false, default, error);
    }

    public class NetworkResult
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }

        private NetworkResult(bool ok, string err)
        {
            IsSuccess = ok;
            ErrorMessage = err;
        }

        public static NetworkResult Success() => new(true, null);
        public static NetworkResult Failure(string error) => new(false, error);
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
}

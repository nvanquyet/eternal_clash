// RelayHandler.cs
// Drop-in replacement (full file)

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
        // ===== Internal state =====
        private static readonly object _lock = new();
        private static CancellationTokenSource _currentOperationCts;
        private static RelayConnectionState _state = RelayConnectionState.Idle;

        private enum RelayConnectionState { Idle, Allocating, Joining, Connected, Failed }

        // ===== Public API =====

        /// <summary>
        /// Allocate host Relay allocation and return Join Code. (Does NOT StartHost)
        /// </summary>
        public static async Task<RelayResult<string>> SetupHostRelayAsync(
            int maxPlayers,
            CancellationToken cancellationToken = default,
            int maxRetries = 1) // default: no retry
        {
            try
            {
                var maxClients = Math.Max(0, maxPlayers - 1);
                var allocResult = await AllocateHostAsync(maxClients, cancellationToken, maxRetries);

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

        public static NetworkResult StartNetworkHost()
        {
            try
            {
                var nm = NetworkManager.Singleton;
                if (nm == null) return NetworkResult.Failure("NetworkManager not found");
                if (!nm.StartHost()) return NetworkResult.Failure("Failed to start NetworkManager host");
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
        /// Join by join code and configure transport. (Does NOT StartClient)
        /// </summary>
        public static async Task<RelayResult<JoinAllocation>> ConnectClientToRelayAsync(
            string joinCode,
            CancellationToken cancellationToken = default,
            int maxRetries = 1) // default: no retry
        {
            try
            {
                var result = await JoinAsClientAsync(joinCode, cancellationToken, maxRetries);
                if (!result.IsSuccess) return RelayResult<JoinAllocation>.Failure(result.ErrorMessage);
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

        public static NetworkResult StartNetworkClient()
        {
            try
            {
                var nm = NetworkManager.Singleton;
                if (nm == null) return NetworkResult.Failure("NetworkManager not found");
                if (!nm.StartClient()) return NetworkResult.Failure("Failed to start NetworkManager client");
                Debug.Log("[RelayHandler] Network client started");
                return NetworkResult.Success();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayHandler] StartNetworkClient failed: {ex}");
                return NetworkResult.Failure(ex.Message);
            }
        }

        public static async Task<bool> SafeShutdownAsync()
        {
            try
            {
                CancelCurrentOperation();

                var nm = NetworkManager.Singleton;
                if (nm != null && (nm.IsHost || nm.IsClient || nm.IsServer))
                {
                    Debug.Log("[RelayHandler] Shutting down NetworkManager...");
                    nm.Shutdown();
                    await Task.Delay(300);
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

        public static void CancelCurrentOperation()
        {
            lock (_lock) { _currentOperationCts?.Cancel(); }
            Debug.Log("[RelayHandler] Current operation cancelled");
        }

        public static void ResetAllState()
        {
            lock (_lock)
            {
                _currentOperationCts?.Cancel();
                _currentOperationCts?.Dispose();
                _currentOperationCts = null;
                _state = RelayConnectionState.Idle;
            }
            Debug.Log("[RelayHandler] Internal state reset → Idle");
        }

        // ===== Internals =====

        private static UnityTransport GetTransport()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) throw new RelayException("NetworkManager not found");

            if (nm.NetworkConfig?.NetworkTransport is not UnityTransport transport)
                throw new RelayException("UnityTransport is not configured on NetworkManager");

            return transport;
        }

        private static RelayServerEndpoint SelectOptimalEndpoint(System.Collections.Generic.IList<RelayServerEndpoint> endpoints)
        {
            if (endpoints == null || endpoints.Count == 0)
                throw new RelayException("No Relay endpoints available");

#if UNITY_WEBGL
            // WebGL must use WSS
            var wss = endpoints.FirstOrDefault(e =>
                string.Equals(e.ConnectionType, "wss", StringComparison.OrdinalIgnoreCase));
            if (wss != null) { Debug.Log("[RelayHandler] Using WSS endpoint (WebGL)"); return wss; }
            // fallback (unlikely)
            var any = endpoints[0];
            Debug.LogWarning($"[RelayHandler] WebGL: WSS not found, using {any.ConnectionType}");
            return any;
#else
            // Prefer DTLS, fallback UDP
            var dtls = endpoints.FirstOrDefault(e =>
                string.Equals(e.ConnectionType, "dtls", StringComparison.OrdinalIgnoreCase));
            if (dtls != null) { Debug.Log("[RelayHandler] Using DTLS endpoint"); return dtls; }

            var udp = endpoints.FirstOrDefault(e =>
                string.Equals(e.ConnectionType, "udp", StringComparison.OrdinalIgnoreCase));
            if (udp != null) { Debug.Log("[RelayHandler] Using UDP endpoint"); return udp; }

            Debug.LogWarning("[RelayHandler] Using first available endpoint");
            return endpoints[0];
#endif
        }

        private static async Task<RelayResult<(Allocation allocation, string joinCode)>> AllocateHostAsync(
            int maxClientConnections,
            CancellationToken cancellationToken,
            int maxRetries)
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

                int attempts = Mathf.Max(1, maxRetries);
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    try
                    {
                        _currentOperationCts.Token.ThrowIfCancellationRequested();

                        allocation = await RelayService.Instance.CreateAllocationAsync(maxClientConnections);
                        joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                        Debug.Log($"[RelayHandler] Host allocation OK (attempt {attempt})");
                        break;
                    }
                    catch (Exception ex) when (attempt < attempts)
                    {
                        last = ex;
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                        Debug.LogWarning($"[RelayHandler] Host allocation attempt {attempt} failed: {ex.Message}. Retrying in {delay.TotalSeconds:0}s");
                        await Task.Delay(delay, _currentOperationCts.Token);
                    }
                }

                if (allocation == null || string.IsNullOrEmpty(joinCode))
                    throw last ?? new RelayException("Failed to allocate Relay host");

                await ConfigureTransportForHost(allocation, joinCode);
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
                if (_state == RelayConnectionState.Allocating) _state = RelayConnectionState.Failed;
                lock (_lock)
                {
                    _currentOperationCts?.Dispose();
                    _currentOperationCts = null;
                }
            }
        }

        private static async Task ConfigureTransportForHost(Allocation allocation, string joinCodeForLog)
        {
            await Task.Yield();

            var ep = SelectOptimalEndpoint(allocation.ServerEndpoints);
            var serverData = new RelayServerData(
                ep.Host,
                (ushort)ep.Port,
                allocation.AllocationIdBytes,
                allocation.ConnectionData,
                allocation.ConnectionData, // host uses own connection data twice
                allocation.Key,
                ep.Secure);

            var transport = GetTransport();
            transport.SetRelayServerData(serverData);

            Debug.Log($"[RelayHandler] Host transport: {ep.Host}:{ep.Port} " +
                      $"secure={ep.Secure} type={ep.ConnectionType} " +
                      $"alloc={(allocation.AllocationIdBytes != null ? Convert.ToBase64String(allocation.AllocationIdBytes).Substring(0,8) : "null")} " +
                      $"joinCode={joinCodeForLog}");
        }

        private static async Task<RelayResult<JoinAllocation>> JoinAsClientAsync(
            string joinCode,
            CancellationToken cancellationToken,
            int maxRetries)
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

                int attempts = Mathf.Max(1, maxRetries);
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    try
                    {
                        _currentOperationCts.Token.ThrowIfCancellationRequested();

                        joinAlloc = await RelayService.Instance.JoinAllocationAsync(normalized);
                        Debug.Log($"[RelayHandler] Client join OK (attempt {attempt})");
                        break;
                    }
                    catch (Exception ex) when (attempt < attempts)
                    {
                        last = ex;
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                        Debug.LogWarning($"[RelayHandler] Client join attempt {attempt} failed: {ex.Message}. Retrying in {delay.TotalSeconds:0}s");
                        await Task.Delay(delay, _currentOperationCts.Token);
                    }
                }

                if (joinAlloc == null)
                    throw last ?? new RelayException("Failed to join Relay");

                await ConfigureTransportForClient(joinAlloc, normalized);
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
                if (_state == RelayConnectionState.Joining) _state = RelayConnectionState.Failed;
                lock (_lock)
                {
                    _currentOperationCts?.Dispose();
                    _currentOperationCts = null;
                }
            }
        }

        private static async Task ConfigureTransportForClient(JoinAllocation joinAllocation, string joinCodeForLog)
        {
            await Task.Yield();

            var ep = SelectOptimalEndpoint(joinAllocation.ServerEndpoints);
            var serverData = new RelayServerData(
                ep.Host,
                (ushort)ep.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData,
                joinAllocation.Key,
                ep.Secure);

            var transport = GetTransport();
            transport.SetRelayServerData(serverData);

            Debug.Log($"[RelayHandler] Client transport: {ep.Host}:{ep.Port} " +
                      $"secure={ep.Secure} type={ep.ConnectionType} " +
                      $"alloc={(joinAllocation.AllocationIdBytes != null ? Convert.ToBase64String(joinAllocation.AllocationIdBytes).Substring(0,8) : "null")} " +
                      $"joinCode={joinCodeForLog}");
        }

        // ===== Helper Types =====

        private class RelayException : Exception
        {
            public RelayException(string message) : base(message) { }
            public RelayException(string message, Exception inner) : base(message, inner) { }
        }
    }

    // ===== Result Types =====

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

        private NetworkResult(bool ok, string err) { IsSuccess = ok; ErrorMessage = err; }

        public static NetworkResult Success() => new(true, null);
        public static NetworkResult Failure(string error) => new(false, error);
    }

    public enum NetworkStatus { NotInitialized, Idle, Host, ClientConnecting, ClientConnected, Failed }
}

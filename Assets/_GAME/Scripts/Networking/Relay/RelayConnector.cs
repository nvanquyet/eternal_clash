// RelayConnector.cs - Tối ưu hóa connection và error handling
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
    /// <summary>
    /// RelayConnector được tối ưu với connection pooling và retry logic
    /// </summary>
    public static class RelayConnector
    {
        private static readonly object _lockObject = new object();
        private static RelayConnectionState _currentState = RelayConnectionState.Idle;
        private static CancellationTokenSource _currentOperation;

        public enum RelayConnectionState
        {
            Idle,
            Allocating,
            Joining,
            Connected,
            Failed
        }

        public static RelayConnectionState CurrentState => _currentState;

        #region Transport Management

        private static UnityTransport GetTransport()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                throw new RelayException("NetworkManager không tồn tại");

            if (networkManager.NetworkConfig?.NetworkTransport is not UnityTransport transport)
                throw new RelayException("UnityTransport không được cấu hình");

            return transport;
        }

        private static RelayServerEndpoint SelectOptimalEndpoint(System.Collections.Generic.IList<RelayServerEndpoint> endpoints)
        {
            if (endpoints == null || endpoints.Count == 0)
                throw new RelayException("Không có relay endpoints khả dụng");

            // Ưu tiên DTLS cho bảo mật, fallback về UDP
            var dtlsEndpoint = endpoints.FirstOrDefault(e => 
                string.Equals(e.ConnectionType, "dtls", StringComparison.OrdinalIgnoreCase));
            
            if (dtlsEndpoint != null)
            {
                Debug.Log("[RelayConnector] Sử dụng DTLS endpoint");
                return dtlsEndpoint;
            }

            var udpEndpoint = endpoints.FirstOrDefault(e => 
                string.Equals(e.ConnectionType, "udp", StringComparison.OrdinalIgnoreCase));
                
            if (udpEndpoint != null)
            {
                Debug.Log("[RelayConnector] Sử dụng UDP endpoint");
                return udpEndpoint;
            }

            Debug.LogWarning("[RelayConnector] Sử dụng endpoint mặc định");
            return endpoints[0];
        }

        #endregion

        #region Host Operations

        /// <summary>
        /// Tạo allocation cho host với retry logic
        /// </summary>
        public static async Task<RelayResult<(Allocation allocation, string joinCode)>> AllocateHostAsync(
            int maxClientConnections, 
            CancellationToken cancellationToken = default,
            int maxRetries = 3)
        {
            lock (_lockObject)
            {
                if (_currentState != RelayConnectionState.Idle)
                    return RelayResult<(Allocation, string)>.Failure($"Relay đang ở trạng thái: {_currentState}");
                
                _currentState = RelayConnectionState.Allocating;
                _currentOperation?.Cancel();
                _currentOperation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            try
            {
                var maxClients = Math.Max(0, maxClientConnections);
                Debug.Log($"[RelayConnector] Tạo allocation cho {maxClients} clients");

                Allocation allocation = null;
                string joinCode = null;
                Exception lastException = null;

                // Retry logic
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        _currentOperation.Token.ThrowIfCancellationRequested();

                        allocation = await RelayService.Instance.CreateAllocationAsync(maxClients);
                        joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                        
                        Debug.Log($"[RelayConnector] Allocation thành công (attempt {attempt})");
                        break;
                    }
                    catch (Exception ex) when (attempt < maxRetries)
                    {
                        lastException = ex;
                        Debug.LogWarning($"[RelayConnector] Attempt {attempt} thất bại: {ex.Message}");
                        
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // Exponential backoff
                        await Task.Delay(delay, _currentOperation.Token);
                    }
                }

                if (allocation == null || string.IsNullOrEmpty(joinCode))
                {
                    throw lastException ?? new RelayException("Không thể tạo allocation");
                }

                // Cấu hình transport
                await ConfigureTransportForHost(allocation);
                
                _currentState = RelayConnectionState.Connected;
                Debug.Log($"[RelayConnector] Host setup hoàn tất - Join Code: {joinCode}");
                
                return RelayResult<(Allocation, string)>.Success((allocation, joinCode));
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[RelayConnector] Host allocation đã bị hủy");
                return RelayResult<(Allocation, string)>.Failure("Operation đã bị hủy");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayConnector] Host allocation thất bại: {ex}");
                return RelayResult<(Allocation, string)>.Failure($"Host allocation thất bại: {ex.Message}");
            }
            finally
            {
                if (_currentState == RelayConnectionState.Allocating)
                    _currentState = RelayConnectionState.Failed;
                
                _currentOperation?.Dispose();
                _currentOperation = null;
            }
        }

        private static async Task ConfigureTransportForHost(Allocation allocation)
        {
            await Task.Yield(); // Yield để tránh blocking

            var endpoint = SelectOptimalEndpoint(allocation.ServerEndpoints);
            var serverData = new RelayServerData(
                endpoint.Host,
                (ushort)endpoint.Port,
                allocation.AllocationIdBytes,
                allocation.ConnectionData,
                allocation.ConnectionData, // Host sử dụng chính connectionData của mình
                allocation.Key,
                endpoint.Secure
            );

            var transport = GetTransport();
            transport.SetRelayServerData(serverData);
            
            Debug.Log($"[RelayConnector] Host transport đã được cấu hình: {endpoint.Host}:{endpoint.Port}");
        }

        #endregion

        #region Client Operations

        /// <summary>
        /// Join relay với validation và retry logic
        /// </summary>
        public static async Task<RelayResult<JoinAllocation>> JoinAsClientAsync(
            string joinCode, 
            CancellationToken cancellationToken = default,
            int maxRetries = 3)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
                return RelayResult<JoinAllocation>.Failure("Join code không hợp lệ");

            lock (_lockObject)
            {
                if (_currentState != RelayConnectionState.Idle)
                    return RelayResult<JoinAllocation>.Failure($"Relay đang ở trạng thái: {_currentState}");
                
                _currentState = RelayConnectionState.Joining;
                _currentOperation?.Cancel();
                _currentOperation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            try
            {
                var normalizedCode = joinCode.Trim().ToUpperInvariant();
                Debug.Log($"[RelayConnector] Joining relay với code: {normalizedCode}");

                JoinAllocation joinAllocation = null;
                Exception lastException = null;

                // Retry logic
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        _currentOperation.Token.ThrowIfCancellationRequested();

                        joinAllocation = await RelayService.Instance.JoinAllocationAsync(normalizedCode);
                        Debug.Log($"[RelayConnector] Join thành công (attempt {attempt})");
                        break;
                    }
                    catch (Exception ex) when (attempt < maxRetries)
                    {
                        lastException = ex;
                        Debug.LogWarning($"[RelayConnector] Join attempt {attempt} thất bại: {ex.Message}");
                        
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                        await Task.Delay(delay, _currentOperation.Token);
                    }
                }

                if (joinAllocation == null)
                {
                    throw lastException ?? new RelayException("Không thể join relay");
                }

                // Cấu hình transport
                await ConfigureTransportForClient(joinAllocation);
                
                _currentState = RelayConnectionState.Connected;
                Debug.Log("[RelayConnector] Client setup hoàn tất");
                
                return RelayResult<JoinAllocation>.Success(joinAllocation);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[RelayConnector] Client join đã bị hủy");
                return RelayResult<JoinAllocation>.Failure("Operation đã bị hủy");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayConnector] Client join thất bại: {ex}");
                return RelayResult<JoinAllocation>.Failure($"Client join thất bại: {ex.Message}");
            }
            finally
            {
                if (_currentState == RelayConnectionState.Joining)
                    _currentState = RelayConnectionState.Failed;
                
                _currentOperation?.Dispose();
                _currentOperation = null;
            }
        }

        private static async Task ConfigureTransportForClient(JoinAllocation joinAllocation)
        {
            await Task.Yield(); // Yield để tránh blocking

            var endpoint = SelectOptimalEndpoint(joinAllocation.ServerEndpoints);
            var serverData = new RelayServerData(
                endpoint.Host,
                (ushort)endpoint.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData, // Client cần hostConnectionData
                joinAllocation.Key,
                endpoint.Secure
            );

            var transport = GetTransport();
            transport.SetRelayServerData(serverData);
            
            Debug.Log($"[RelayConnector] Client transport đã được cấu hình: {endpoint.Host}:{endpoint.Port}");
        }

        #endregion

        #region State Management

        /// <summary>
        /// Reset relay state - dùng khi cần cleanup
        /// </summary>
        public static void ResetState()
        {
            lock (_lockObject)
            {
                _currentOperation?.Cancel();
                _currentOperation?.Dispose();
                _currentOperation = null;
                _currentState = RelayConnectionState.Idle;
            }
            
            Debug.Log("[RelayConnector] State đã được reset");
        }

        /// <summary>
        /// Cancel operation hiện tại
        /// </summary>
        public static void CancelCurrentOperation()
        {
            lock (_lockObject)
            {
                _currentOperation?.Cancel();
            }
            
            Debug.Log("[RelayConnector] Operation hiện tại đã bị cancel");
        }

        #endregion
    }

    #region Helper Classes

    /// <summary>
    /// Custom exception cho relay operations
    /// </summary>
    public class RelayException : Exception
    {
        public RelayException(string message) : base(message) { }
        public RelayException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Result wrapper cho relay operations
    /// </summary>
    public class RelayResult<T>
    {
        public bool IsSuccess { get; private set; }
        public T Data { get; private set; }
        public string ErrorMessage { get; private set; }

        private RelayResult(bool isSuccess, T data, string errorMessage)
        {
            IsSuccess = isSuccess;
            Data = data;
            ErrorMessage = errorMessage;
        }

        public static RelayResult<T> Success(T data) => new(true, data, null);
        public static RelayResult<T> Failure(string errorMessage) => new(false, default(T), errorMessage);
    }

    #endregion
}

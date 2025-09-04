using System;
using System.Threading.Tasks;
using _GAME.Scripts.Controller;
using _GAME.Scripts.UI;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Networking.StateMachine
{
    #region Network State Implementations

    /// <summary>
    /// Idle/Disconnected state - starting state
    /// </summary>
    public class DefaultNetworkState : NetworkStateBase
    {
        public override NetworkState State => NetworkState.Default;

        public override async Task OnEnterAsync(NetworkStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);

            // Ensure network is shutdown
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsHost || nm.IsClient)
            {
                nm.Shutdown();      
            }

            // Todo: Reset network data
            Debug.Log("[NetworkState] Entered Default state, network is default.");
            SceneController.Instance.LoadSceneAsync((int)SceneDefinitions.Home);
            LoadingUI.Instance.RunTimed(1f, () => { });
        }

        public override bool CanTransitionTo(NetworkState targetState)
        {
            return targetState == NetworkState.ClientConnecting ||
                   base.CanTransitionTo(targetState);
        }
    }
    

    /// <summary>
    /// Client connecting state - connecting as client
    /// </summary>
    public class ClientConnectingState : NetworkStateBase
    {
        public override NetworkState State => NetworkState.ClientConnecting;

        public override async Task OnEnterAsync(NetworkStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);

            LoadingUI.Instance?.SetProgress(0.2f, 1f, "Connecting to Host...");
        }

        public override bool CanTransitionTo(NetworkState targetState)
        {
            return targetState == NetworkState.Connected ||
                   base.CanTransitionTo(targetState);
        }
    }

    /// <summary>
    /// Connected state - network connected successfully
    /// </summary>
    public class ConnectedState : NetworkStateBase
    {
        public override NetworkState State => NetworkState.Connected;

        public override async Task OnEnterAsync(NetworkStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);

            LoadingUI.Instance?.SetProgress(0.6f, 1f, "Connected");

            // Network is connected, ready for game operations
            Debug.Log("[NetworkState] Network connection established");
        }

        public override bool CanTransitionTo(NetworkState targetState)
        {
            return targetState == NetworkState.Disconnecting ||
                   base.CanTransitionTo(targetState);
        }
    }
    
    /// <summary>
    /// Disconnecting state - gracefully shutting down network
    /// </summary>
    public class DisconnectingState : NetworkStateBase
    {
        public override NetworkState State => NetworkState.Disconnecting;

        public override async Task OnEnterAsync(NetworkStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);
            LoadingUI.Instance?.SetProgress(0.5f, 1f, "Disconnecting...");
            
            //Todo: Shutdown network
        }
    }

    
    
    /// <summary>
    /// Client Disconnected state - disconnected from host
    /// </summary>
    public class DisconnectedState : NetworkStateBase
    {
        public override NetworkState State => NetworkState.Disconnected;

        public override async Task OnEnterAsync(NetworkStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);
            Debug.Log($"[DisconnectedState] Disconnected from host. Reason: {context}");
        }
    }
    /// <summary>
    /// Failed state - error occurred
    /// </summary>
    public class FailedNetworkState : NetworkStateBase
    {
        public override NetworkState State => NetworkState.Failed;

        public override async Task OnEnterAsync(NetworkStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);

            var errorMessage = context as string ?? "Network operation failed";

            // Show error popup
            PopupNotification.Instance?.ShowPopup(false, errorMessage, "Network Error");

            // Auto-transition back to None after delay
            await Task.Delay(2000);
            await manager.TryTransitionAsync(NetworkState.Default);
        }

        public override bool CanTransitionTo(NetworkState targetState)
        {
            return targetState == NetworkState.Default;
        }
    }

    #endregion

    #region State Extensions

    public static class NetworkStateExtensions
    {
        public static string GetDisplayName(this NetworkState state) => state switch
        {
            NetworkState.Default => "Idle",
            NetworkState.ClientConnecting => "Connecting",
            NetworkState.Connected => "Connected",
            NetworkState.Disconnecting => "Disconnecting",
            NetworkState.Disconnected => "Disconnected",
            NetworkState.Failed => "Network Error",
            _ => "Unknown"
        };
    }

    #endregion
}
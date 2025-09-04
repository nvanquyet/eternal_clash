using System;
using System.Threading.Tasks;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Networking.Relay;
using _GAME.Scripts.UI;
using _GAME.Scripts.UI.Base;
using UnityEngine;

namespace _GAME.Scripts.Networking.StateMachine
{
    #region None State

    public class DefaultState : LobbyStateBase
    {
        public override LobbyState State => LobbyState.Default;

        public override async Task OnEnterAsync(LobbyStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);
            // Cleanup
            await RelayHandler.SafeShutdownAsync();
            RelayHandler.ResetAllState();
            
            //Fake loading UI for smooth transition 
            Debug.Log($"[LobbyState] Transitioning to Home Scene.");
            // SceneController.Instance.LoadSceneAsync((int)SceneDefinitions.Home);
            // LoadingUI.Instance.RunTimed(1f, () => { });

        }

        public override bool CanTransitionTo(LobbyState targetState)
        {
            return targetState == LobbyState.CreatingLobby ||
                   targetState == LobbyState.JoiningLobby ||
                   base.CanTransitionTo(targetState);
        }
    }

    #endregion

    #region Create/Join States (UI Only)

    public class CreatingLobbyState : LobbyStateBase
    {
        public override LobbyState State => LobbyState.CreatingLobby;

        public override async Task OnEnterAsync(LobbyStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);
            // UI: Show loading
            LoadingUI.Instance.SetProgress(0.1f,1f,State.GetDisplayName());
        }

        public override bool CanTransitionTo(LobbyState targetState)
        {
            return targetState == LobbyState.LobbyActive ||
                   base.CanTransitionTo(targetState);
        }
    }

    public class JoiningLobbyState : LobbyStateBase
    {
        public override LobbyState State => LobbyState.JoiningLobby;

        public override async Task OnEnterAsync(LobbyStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);

            LoadingUI.Instance.SetProgress(0.1f,1f,State.GetDisplayName());
            // TrackEvent("lobby_join_start", new
            // {
            //     code_length = (context as string)?.Length ?? 0
            // });
        }

        public override bool CanTransitionTo(LobbyState targetState)
        {
            return targetState == LobbyState.LobbyActive ||
                   base.CanTransitionTo(targetState);
        }
    }

    #endregion
    

    #region Active State

    public class LobbyActiveState : LobbyStateBase
    {
        public override LobbyState State => LobbyState.LobbyActive;

        public override async Task OnEnterAsync(LobbyStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);

            var lobbyManager = LobbyManager.Instance;
            var isHost = lobbyManager.IsHost;

            // UI: Hide loading and show lobby screen
            LoadingUI.Instance.SetProgress(1f,1f,State.GetDisplayName());

            // UI: Configure controls based on role
            if (isHost)
            {
                
            }
            else
            {
                
            }

            // Audio & Analytics
            // PlaySound("lobby_joined");
            // TrackEvent("lobby_active", new
            // {
            //     is_host = isHost,
            //     player_count = lobbyManager.CurrentLobby?.Players?.Count ?? 0,
            //     max_players = lobbyManager.CurrentLobby?.MaxPlayers ?? 0
            // });
        }

        public override bool CanTransitionTo(LobbyState targetState)
        {
            return targetState == LobbyState.LeavingLobby ||
                   targetState == LobbyState.RemovingLobby ||
                   base.CanTransitionTo(targetState);
        }
    }

    #endregion

    #region Exit States

    public class LeavingLobbyState : LobbyStateBase
    {
        public override LobbyState State => LobbyState.LeavingLobby;

        public override async Task OnEnterAsync(LobbyStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);
            LoadingUI.Instance.SetProgress(0.5f,1f,State.GetDisplayName());
            //TrackEvent("lobby_leave_start");
        }
    }

    public class RemovingLobbyState : LobbyStateBase
    {
        public override LobbyState State => LobbyState.RemovingLobby;

        public override async Task OnEnterAsync(LobbyStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);
            LoadingUI.Instance.SetProgress(0.5f,1f,State.GetDisplayName());
            //TrackEvent("lobby_remove_start");
        }
    }

    #endregion

    #region Failed State

    public class FailedLobbyState : LobbyStateBase
    {
        public override LobbyState State => LobbyState.Failed;
        public override async Task OnEnterAsync(LobbyStateManager manager, object context = null)
        {
            await base.OnEnterAsync(manager, context);
            
            //Show Popup
            PopupNotification.Instance.ShowPopup(false, "An error occurred with the lobby connection.", "Error");
            await manager.TryTransitionAsync(LobbyState.Default);
        }

        public override bool CanTransitionTo(LobbyState targetState)
        {
            return targetState == LobbyState.Default;
        }
    }

    #endregion

    #region State Extensions (Optimized)

    public static class LobbyStateExtensions
    {
        private static readonly LobbyState[] TransitionalStates =
        {
            LobbyState.CreatingLobby,
            LobbyState.JoiningLobby,
            LobbyState.LeavingLobby,
            LobbyState.RemovingLobby
        };

        private static readonly LobbyState[] UserInputStates =
        {
            LobbyState.Default,
            LobbyState.LobbyActive,
            LobbyState.Failed
        };

        public static bool IsTransitional(this LobbyState state) =>
            Array.IndexOf(TransitionalStates, state) >= 0;

       

        public static bool AllowsUserInput(this LobbyState state) =>
            Array.IndexOf(UserInputStates, state) >= 0;

        public static string GetDisplayName(this LobbyState state) => state switch
        {
            LobbyState.Default => "Home",
            LobbyState.CreatingLobby => "Creating Lobby",
            LobbyState.JoiningLobby => "Joining Lobby",
            LobbyState.LobbyActive => "In Lobby",
            LobbyState.LeavingLobby => "Leaving",
            LobbyState.RemovingLobby => "Closing",
            LobbyState.Failed => "Error",
            _ => "Unknown"
        };
    }

    #endregion

    #region Context Objects (Simplified)

    public static class LobbyStateContext
    {
        public class ErrorContext
        {
            public string ErrorMessage { get; set; }
            public LobbyState PreviousState { get; set; }

            public ErrorContext(string message, LobbyState previousState = LobbyState.Default)
            {
                ErrorMessage = message;
                PreviousState = previousState;
            }
        }

        public class JoinContext
        {
            public string LobbyCode { get; set; }
            public string Password { get; set; }
        }

        public class CreateContext
        {
            public string LobbyName { get; set; }
            public int MaxPlayers { get; set; }
        }
    }

    #endregion
}
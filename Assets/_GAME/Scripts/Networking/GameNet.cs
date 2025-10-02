using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _GAME.Scripts.Authenticator;
using _GAME.Scripts.Config;
using _GAME.Scripts.Controller;
using _GAME.Scripts.Data;
using _GAME.Scripts.Networking.Lobbies;
using _GAME.Scripts.Networking.Relay;
using _GAME.Scripts.UI;
using GAME.Scripts.DesignPattern;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.Serialization;

namespace _GAME.Scripts.Networking
{
    public class GameNet : SingletonDontDestroy<GameNet>
    {
        [FormerlySerializedAs("authManager")] [SerializeField]
        private PlayFabAuthManager auth;

        [FormerlySerializedAs("lobbyManager")] [SerializeField]
        private LobbyManager lobby;

        [FormerlySerializedAs("networkController")] [SerializeField]
        private NetworkController network;

#if UNITY_EDITOR
        protected void OnValidate()
        {
            auth ??= FindAnyObjectByType<PlayFabAuthManager>();
            lobby ??= FindAnyObjectByType<LobbyManager>();
            network ??= FindAnyObjectByType<NetworkController>();
        }
#endif

        #region Getter

        public PlayFabAuthManager Auth => auth;

        public LobbyManager Lobby => lobby;

        public NetworkController Network => network;

        #endregion


        #region Unity Life Circle

        private void Start()
        {
            LobbyEvents.OnLobbyNotFound += OnLobbyNotFound;
        }

        protected override void OnDestroy()
        {
            LobbyEvents.OnLobbyNotFound -= OnLobbyNotFound;
            base.OnDestroy();
        }

        #endregion


        #region PUBLIC API Lobby

        public async Task HostLobby(Action<OperationResult> onCompleted = null)
        {
            try
            {
                LoadingUI.Instance.SetProgress(0.2f, 1, "Creating Lobby...");

                // 1) TẠO LOBBY TRƯỚC - chỉ lobby logic, không network
                var createLobbyOption = new CreateLobbyOptions
                {
                    Password = GameConfig.Instance.defaultPassword,
                    Player = CreateDefaultPlayerData(), // thêm player data
                    Data = new Dictionary<string, DataObject>
                    {
                        {
                            LobbyConstants.LobbyData.PASSWORD,
                            new DataObject(DataObject.VisibilityOptions.Member, GameConfig.Instance.defaultPassword)
                        },
                        {
                            LobbyConstants.LobbyData.PHASE,
                            new DataObject(DataObject.VisibilityOptions.Public, SessionPhase.WAITING)
                        },
                    },
                };

                var op = await Lobby.CreateLobbyAsync(
                    GameConfig.Instance.defaultNameLobby,
                    (int)GameConfig.Instance.defaultMaxPlayer,
                    createLobbyOption);
                if (op.IsSuccess)
                {
                    await Network.LoadSceneAsync(SceneDefinitions.WaitingRoom);
                }
                
                //Show popup
                if (op.IsSuccess && Lobby.CurrentLobby != null)
                {
                    PopupNotification.Instance.ShowPopup(true, "Lobby created successfully", "Success");
                }
                else
                {
                    PopupNotification.Instance.ShowPopup(false, $"Create lobby failed: {op.ErrorMessage}", "Error");
                }
            }
            catch (Exception e)
            {
                PopupNotification.Instance.ShowPopup(false, $"Host error: {e.Message}", "Error");
                // Emergency cleanup
                try
                {
                    await Network.StopAsync();
                    await Lobby.RemoveLobbyAsync();
                }
                catch
                {
                    Debug.LogWarning("[GameNet] Emergency cleanup failed");
                }

                onCompleted?.Invoke(OperationResult.Failure($"Host error: {e.Message}"));
            }
        }

        public async Task JoinLobby(string lobbyCode, string password, Action<OperationResult> onCompleted = null)
        {
            try
            {
                LoadingUI.Instance.SetProgress(0.15f, 1f, "Finding Lobby...");

                // 1) JOIN LOBBY TRƯỚC - chỉ lobby logic, kiểm tra trạng thái
                var joinOp = await Lobby.JoinLobbyAsync(lobbyCode, password);
                //Show popup
                if (joinOp.IsSuccess && Lobby.CurrentLobby != null)
                {
                    PopupNotification.Instance.ShowPopup(true, "Join Lobby successfully", "Success");
                }
                else
                {
                    PopupNotification.Instance.ShowPopup(false, $"Join failed: {joinOp.ErrorMessage}", "Error");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameNet] Exception during joining lobby: {e}");
                PopupNotification.Instance.ShowPopup(false, $"Join error: {e.Message}", "Error");

                // Emergency cleanup
                try
                {
                    await Network.StopAsync();
                    await Lobby.LeaveLobbyAsync();
                }
                catch
                {
                    Debug.LogWarning("[GameNet] Emergency cleanup failed during join");
                }

                onCompleted?.Invoke(OperationResult.Failure($"Join error: {e.Message}"));
            }
        }

        public async Task LeaveLobbyAsync(Action<OperationResult> onCompleted = null)
        {
            try
            {
                LoadingUI.Instance.SetProgress(0.2f, 1f, "Disconnecting...");

                // 1) Disconnect network trước
                await Network.StopAsync();

                LoadingUI.Instance.SetProgress(0.6f, 1f, "Leaving lobby...");

                // 2) Leave/Remove lobby
                OperationResult op;
                if (Network.IsHost)
                {
                    Debug.Log("[GameNet] Host removing lobby...");
                    op = await Lobby.RemoveLobbyAsync();
                }
                else
                {
                    Debug.Log("[GameNet] Client leaving lobby...");
                    op = await Lobby.LeaveLobbyAsync();
                }

                LoadingUI.Instance.SetProgress(0.9f, 1f, "Loading home...");

                if (op.IsSuccess)
                {
                    Debug.Log("[GameNet] Left/removed lobby successfully");
                    SceneController.Instance.LoadSceneAsync(SceneHelper.ToSceneName(SceneDefinitions.Home));
                }
                else
                {
                    PopupNotification.Instance.ShowPopup(false, $"Leave lobby failed: {op.ErrorMessage}", "Error");
                }

                LoadingUI.Instance.SetProgress(1f, 1f, "Complete!");
                onCompleted?.Invoke(op);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameNet] Exception during leaving lobby: {e.Message}");
                PopupNotification.Instance.ShowPopup(false, $"Leave error: {e.Message}", "Error");

                // Force cleanup and go home
                try
                {
                    await Network.StopAsync();
                    SceneController.Instance.LoadSceneAsync(SceneHelper.ToSceneName(SceneDefinitions.Home));
                }
                catch
                {
                    Debug.LogError("[GameNet] Force cleanup failed");
                }

                onCompleted?.Invoke(OperationResult.Failure($"Leave error: {e.Message}"));
            }
        }

        public async Task RemoveLobbyAsync(Action<OperationResult> onCompleted = null)
        {
            try
            {
                if (!Network.IsHost)
                {
                    Debug.LogWarning("[GameNet] Only host can remove lobby");
                    onCompleted?.Invoke(OperationResult.Failure("Only host can remove lobby"));
                    return;
                }

                // Sử dụng logic giống LeaveLobbyAsync cho host
                await LeaveLobbyAsync(onCompleted);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameNet] Exception during removing lobby: {e.Message}");
                onCompleted?.Invoke(OperationResult.Failure($"Remove error: {e.Message}"));
            }
        }

        // HELPER METHODS
        private Unity.Services.Lobbies.Models.Player CreateDefaultPlayerData()
        {
            var playerId = AuthenticationService.Instance?.PlayerId ?? "Unknown";
            var displayName = LocalData.UserName ?? $"Player_{playerId[..Math.Min(6, playerId.Length)]}";

            return new Unity.Services.Lobbies.Models.Player
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    {
                        LobbyConstants.PlayerData.DISPLAY_NAME,
                        new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, displayName)
                    },
                    {
                        LobbyConstants.PlayerData.IS_READY,
                        new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public,
                            LobbyConstants.Defaults.READY_FALSE)
                    }
                }
            };
        }

        public async Task UpdateLobbyPasswordAsync(string newPassword, Action<bool> onCompleted = null)
        {
            try
            {
                var op = await Lobby.UpdateLobbyPasswordAsync(newPassword);
                if (op)
                {
                    PopupNotification.Instance.ShowPopup(true, "Updated lobby password successfully", "Success");
                }
                else
                {
                    PopupNotification.Instance.ShowPopup(false, $"Update lobby password failed, please try again",
                        "Error");
                }

                onCompleted?.Invoke(op);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameNet] Exception during updating lobby password: {e.Message}");
            }
        }

        public async Task UpdateLobbyMaxPlayerAsync(int maxPlayer, Action<bool> onCompleted = null)
        {
            try
            {
                var op = await Lobby.UpdateMaxPlayersAsync(maxPlayer);
                if (op)
                {
                    Debug.Log("[GameNet] Updated lobby password successfully");
                    PopupNotification.Instance.ShowPopup(true, "Update lobby max players successfully", "Success");
                }
                else
                {
                    PopupNotification.Instance.ShowPopup(false, $"Update lobby max players failed, please try again",
                        "Error");
                }

                onCompleted?.Invoke(op);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameNet] Exception during updating lobby max players: {e.Message}");
            }
        }

        public async Task UpdateLobbyNameAsync(string nameLobby, Action<bool> onCompleted = null)
        {
            try
            {
                var op = await Lobby.UpdateLobbyNameAsync(nameLobby);
                if (op)
                {
                    PopupNotification.Instance.ShowPopup(true, "Updated lobby name successfully", "Success");
                }
                else
                {
                    PopupNotification.Instance.ShowPopup(false, $"Update lobby name failed, please try again", "Error");
                }

                onCompleted?.Invoke(op);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameNet] Exception during updating lobby name: {e.Message}");
            }
        }

        public async void KickPlayerAsync(string playerId, Action<bool> onCompleted = null)
        {
            try
            {
                var op = await Lobby.KickPlayerAsync(playerId);
                if (op)
                {
                    PopupNotification.Instance.ShowPopup(true, "Kicked player successfully", "Success");
                }
                else
                {
                    PopupNotification.Instance.ShowPopup(false, $"Kick player failed, please try again", "Error");
                }

                onCompleted?.Invoke(op);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameNet] Exception during kicking player: {e.Message}");
            }
        }

        public async Task SetPlayerReadyAsync(bool isReady, Action<bool> onCompleted = null)
        {
            try
            {
                var op = await Lobby.SetPlayerReadyAsync(isReady);
                if (op)
                {
                    Debug.Log("[GameNet] Set player ready state successfully");
                }
                else
                {
                    PopupNotification.Instance.ShowPopup(false, $"Set player ready state failed, please try again",
                        "Error");
                }

                onCompleted?.Invoke(op);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameNet] Exception during setting player ready state: {e.Message}");
            }
        }

        #endregion
        
        
        public virtual Unity.Services.Lobbies.Models.Player GetPlayerWithClientId(ulong clientId)
        {
            foreach (var player in Lobby.CurrentLobby.Players)
            {
                if (player.Id == clientId.ToString())
                {
                    return player;
                }
            }

            return null;
        }

        public void Clear()
        {
            //Clear all data
            _ = Network.StopAsync();
            Network.ForceAllClientDisconnect();
            Lobby.Runtime.StopRuntime();
        }

        #region Event

        private void OnLobbyNotFound()
        {
            //Player is not in the lobby, return to home
            LoadingUI.Instance.RunTimed(1,
                () =>
                {
                    SceneController.Instance.LoadSceneAsync(SceneHelper.ToSceneName(SceneDefinitions.Home),
                        () => { LoadingUI.Instance.Complete(); });
                }, "Returning to home...", false);
        }

        #endregion
    }
}
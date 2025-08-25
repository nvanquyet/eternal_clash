using System;
using System.Threading;
using System.Threading.Tasks;
using _GAME.Scripts.Networking;
using _GAME.Scripts.Networking.Lobbies;
using GAME.Scripts.DesignPattern;
using Unity.Netcode;

namespace _GAME.Scripts.Core
{
    public class GameSessionHandler : SingletonDontDestroy<GameSessionHandler>
    {
        public event Action<string> OnInfo;
        public event Action<string> OnWarn;
        public event Action<string> OnError;

        protected override void Awake()
        {
            base.Awake();
            RegisterAppExitCleanup_SimpleLobbyExit();
        }

        /// <summary>
        /// Tắt netcode nếu đang chạy (không chờ).
        /// </summary>
        private static void TryShutdownNetcodeFast()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            if (nm.IsClient || nm.IsHost || nm.IsServer) nm.Shutdown();
        }

        /// <summary>
        /// Nếu đang trong lobby: host → remove; client → leave.
        /// Không quan tâm phase, chỉ xử lý rời/giải tán lobby theo vai trò.
        /// </summary>
        private async Task LeaveOrRemoveLobbyNow(CancellationToken ct)
        {
            try
            {
                // 1) tắt netcode trước để không còn traffic/transport mở
                TryShutdownNetcodeFast();

                // 2) kiểm tra đang ở lobby không
                var lobbyId = LobbyExtensions.GetCurrentLobby().Id;
                if (string.IsNullOrWhiteSpace(lobbyId))
                {
                    OnInfo?.Invoke("Không có lobby hiện tại → bỏ qua.");
                    return;
                }

                // 3) host → remove, client → leave
                var isHost = LobbyExtensions.IsHost();
                if (isHost)
                {
                    OnInfo?.Invoke("Host → Remove lobby…");
                    var ok = await LobbyHandler.Instance.RemoveLobbyAsync();
                    if (!ok) OnWarn?.Invoke("RemoveLobbyAsync trả về false.");
                }
                else
                {
                    OnInfo?.Invoke("Client → Leave lobby…");
                    var ok = await LobbyHandler.Instance.LeaveLobbyAsync();
                    if (!ok) OnWarn?.Invoke("LeaveLobbyAsync trả về false.");
                }
            }
            catch (Exception e)
            {
                OnError?.Invoke($"LeaveOrRemoveLobbyNow error: {e.Message}");
            }
        }

        /// <summary>
        /// Đăng ký với AppExitController:
        /// - Best-effort: tắt Netcode nhanh (không chặn)
        /// - Full cleanup: rời/giải tán lobby theo vai trò
        /// </summary>
        private void RegisterAppExitCleanup_SimpleLobbyExit()
        {
            var appExit = _GAME.Scripts.Controller.AppExitController.Instance;

            // tắt netcode nhanh (không block)
            appExit.RegisterBestEffortTask(
                taskId: "netcode_shutdown_fast",
                action: _ => TryShutdownNetcodeFast(),
                order: 0
            );

            // full cleanup: leave/remove lobby
            appExit.RegisterFullCleanupTask(
                taskId: "lobby_leave_or_remove",
                async ct => { await LeaveOrRemoveLobbyNow(ct); },
                order: 10
            );
        }
    }
}

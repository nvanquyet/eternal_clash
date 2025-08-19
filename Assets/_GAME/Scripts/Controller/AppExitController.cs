using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GAME.Scripts.DesignPattern;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace _GAME.Scripts.Controller
{
    /// <summary>
    /// AppExitController:
    /// - Best-effort cleanup: chạy khi Pause/LoseFocus. Không block, không rely vào Task.Delay/CancelAfter.
    /// - Full cleanup: chạy khi muốn thoát app (Application.wantsToQuit). Có await + timeout tổng/ từng task.
    /// </summary>
    public class AppExitController : SingletonDontDestroy<AppExitController>
    {
        [Header("Timeouts (seconds)")]
        [Tooltip("Timeout cho mỗi full-cleanup task (khi Quit)")]
        [SerializeField] private float perTaskTimeoutSeconds = 2f;

        [Tooltip("Tổng timeout cho full cleanup (khi Quit)")]
        [SerializeField] private float totalCleanupTimeoutSeconds = 10f;

        [Tooltip("Ngân sách tổng cho best-effort khi Pause/Focus mất (s)")]
        [SerializeField] private float bestEffortBudgetSeconds = 1f;

        [Header("Stability")]
        [Tooltip("Debounce thời gian (s) để tránh spam focus thay đổi liên tục")]
        [SerializeField] private float focusDebounceSeconds = 0.5f;

        // Events (giữ nguyên để tương thích)
        public event Action OnBeforeCleanup;
        public event Action<string> OnTaskDone;            // gọi khi 1 task (full) xong
        public event Action OnCleanupSuccess;              // kết thúc full cleanup
        public event Action<string> OnCleanupTimeout;      // taskId bị timeout (full)
        public event Action<Exception> OnCleanupError;     // lỗi trong quá trình full cleanup

        // Optional: phát event khi resume để subsystem tự re-init những gì đã đóng lúc best-effort
        public event Action OnAfterResume;

        // Task lists đã tách riêng
        private readonly List<CleanupTask> _bestEffortTasks = new(); // Action nhanh, idempotent
        private readonly List<CleanupTask> _fullTasks = new();       // Func async, có await/timeout

        // State
        private bool _isCleaningUp;            // chỉ dùng cho FULL cleanup
        private bool _hasQuitBeenRequested;    // tránh gọi Quit nhiều lần
        private CancellationTokenSource _bestEffortCts;
        private float _lastFocusOrPauseTs;

        private void OnEnable()
        {
            Application.wantsToQuit += WantsToQuit;
        }

        private void OnDisable()
        {
            Application.wantsToQuit -= WantsToQuit;
        }

        #region Registration API

        /// <summary>Đăng ký best-effort task (chạy nhanh, không await, idempotent).</summary>
        public void RegisterBestEffortTask(string taskId, Action<CancellationToken> action, int order = 0)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                throw new ArgumentException("taskId is required");

            if (action == null)
                throw new ArgumentNullException(nameof(action));

            // Best-effort không phụ thuộc _isCleaningUp (vì _isCleaningUp chỉ của full cleanup)
            _bestEffortTasks.RemoveAll(t => t.TaskId == taskId);
            _bestEffortTasks.Add(new CleanupTask(taskId, ct => { action(ct); return Task.CompletedTask; }, order));
            _bestEffortTasks.Sort((a, b) => a.Order.CompareTo(b.Order));

            Debug.Log($"[AppExitController] Registered BEST-EFFORT task: {taskId} (order:{order})");
        }

        /// <summary>Đăng ký FULL cleanup task (chạy khi quit).</summary>
        public void RegisterFullCleanupTask(string taskId, Func<CancellationToken, Task> asyncTask, int order = 0)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                throw new ArgumentException("taskId is required");

            if (asyncTask == null)
                throw new ArgumentNullException(nameof(asyncTask));

            if (_isCleaningUp)
            {
                Debug.LogWarning($"[AppExitController] Cannot register full task '{taskId}' while full cleanup is in progress");
                return;
            }

            _fullTasks.RemoveAll(t => t.TaskId == taskId);
            _fullTasks.Add(new CleanupTask(taskId, asyncTask, order));
            _fullTasks.Sort((a, b) => a.Order.CompareTo(b.Order));

            Debug.Log($"[AppExitController] Registered FULL task: {taskId} (order:{order})");
        }

        /// <summary>Hủy đăng ký best-effort task theo id.</summary>
        public void UnregisterBestEffortTask(string taskId)
        {
            bool removed = _bestEffortTasks.RemoveAll(t => t.TaskId == taskId) > 0;
            if (removed) Debug.Log($"[AppExitController] Unregistered BEST-EFFORT task: {taskId}");
        }

        /// <summary>Hủy đăng ký full task theo id.</summary>
        public void UnregisterFullCleanupTask(string taskId)
        {
            if (_isCleaningUp)
            {
                Debug.LogWarning($"[AppExitController] Cannot unregister full task '{taskId}' while full cleanup is in progress");
                return;
            }

            bool removed = _fullTasks.RemoveAll(t => t.TaskId == taskId) > 0;
            if (removed) Debug.Log($"[AppExitController] Unregistered FULL task: {taskId}");
        }

        /// <summary>Thông tin các task đã đăng ký (để debug).</summary>
        public (List<(string TaskId, int Order)> bestEffort, List<(string TaskId, int Order)> full) GetRegisteredTasks()
        {
            var be = new List<(string, int)>();
            foreach (var t in _bestEffortTasks) be.Add((t.TaskId, t.Order));

            var fu = new List<(string, int)>();
            foreach (var t in _fullTasks) fu.Add((t.TaskId, t.Order));

            return (be, fu);
        }

        #endregion

        #region Unity lifecycle hooks (Pause/Focus)

        private void OnApplicationPause(bool pauseStatus)
        {
            _lastFocusOrPauseTs = Time.realtimeSinceStartup;

            if (pauseStatus)
            {
                TryRunBestEffortCleanupNonBlocking();
            }
            else
            {
                RecoverAfterResume();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _lastFocusOrPauseTs = Time.realtimeSinceStartup;

            if (!hasFocus)
            {
                TryRunBestEffortCleanupNonBlocking();
            }
            else
            {
                RecoverAfterResume();
            }
        }

        #endregion

        #region Best-effort cleanup (non-blocking)

        // Không dùng _isCleaningUp, không await, không Delay. Chạy rất ngắn và có thể bị cắt ngang.
        private void TryRunBestEffortCleanupNonBlocking()
        {
            if (_bestEffortCts != null) return; // đang chạy rồi
            if (_bestEffortTasks.Count == 0) return;

            Debug.Log($"[AppExitController] Best-effort cleanup START with [{_bestEffortTasks.Count}] tasks");

            _bestEffortCts = new CancellationTokenSource();
            var ct = _bestEffortCts.Token;

            // Chạy nền
            _ = Task.Run(() =>
            {
                try
                {
                    // Phân bổ ngân sách cho từng task (đều nhau)
                    int perTaskMs = Mathf.Max(10,
                        (int)(bestEffortBudgetSeconds * 1000f / Mathf.Max(1, _bestEffortTasks.Count)));

                    foreach (var task in _bestEffortTasks)
                    {
                        if (ct.IsCancellationRequested) break;

                        try
                        {
                            // Gọi task (có thể sync/async). Không chờ dài – chỉ Wait perTaskMs.
                            var t = task.AsyncTask(ct);
                            // Dùng Wait(milliseconds) thay vì Delay/CancelAfter (vì Delay có thể freeze khi nền).
                            t.Wait(perTaskMs);
                            // Nếu chưa xong đúng hạn -> bỏ qua, move next.
                        }
                        catch (Exception ex)
                        {
                            // Nuốt lỗi cho best-effort, chỉ log cảnh báo
                            Debug.LogWarning($"[AppExitController] Best-effort task '{task.TaskId}' ignored error: {ex.Message}");
                        }
                    }
                }
                finally
                {
                    // cleanup CTS trên main thread để an toàn
                    UnityMainThreadDispatch(() =>
                    {
                        _bestEffortCts?.Dispose();
                        _bestEffortCts = null;
                        Debug.Log("[AppExitController] Best-effort cleanup DONE");
                    });
                }
            });
        }

        private void RecoverAfterResume()
        {
            // Debounce focus rung lắc
            if (Time.realtimeSinceStartup - _lastFocusOrPauseTs < focusDebounceSeconds) return;

            // Hủy mọi best-effort đang treo
            if (_bestEffortCts != null)
            {
                _bestEffortCts.Cancel();
                _bestEffortCts.Dispose();
                _bestEffortCts = null;
            }

            // Nếu vì lý do nào đó full cleanup bị treo trong lúc app nền → ép reset
            if (_isCleaningUp)
            {
                Debug.LogWarning("[AppExitController] Forcing FULL cleanup state reset after resume.");
                _isCleaningUp = false;
            }

            // Cho subsystem biết đã quay lại để mở lại tài nguyên (socket/db/…)
            OnAfterResume?.Invoke();
        }

        #endregion

        #region Full cleanup (on quit)

        private bool WantsToQuit()
        {
            if (_hasQuitBeenRequested) return true;   // đã gọi quit xong trước đó
            if (_isCleaningUp) return false;          // đang full cleanup -> chặn quit

            Debug.Log("[AppExitController] Application wants to quit -> FULL cleanup starting...");
            _hasQuitBeenRequested = true;
            _ = RunFullCleanup();
            return false; // chặn quit, sẽ Quit() sau khi cleanup xong (hoặc hết hạn)
        }

        private async Task RunFullCleanup()
        {
            if (_isCleaningUp) return;
            _isCleaningUp = true;

            try
            {
                OnBeforeCleanup?.Invoke();

                using var totalCts = new CancellationTokenSource(TimeSpan.FromSeconds(totalCleanupTimeoutSeconds));
                var totalToken = totalCts.Token;

                int completed = 0, timedout = 0, errors = 0;

                foreach (var task in _fullTasks)
                {
                    if (totalToken.IsCancellationRequested)
                    {
                        Debug.LogWarning("[AppExitController] FULL cleanup: total timeout reached, stop further tasks.");
                        break;
                    }

                    try
                    {
                        Debug.Log($"[AppExitController] FULL task: {task.TaskId} (timeout:{perTaskTimeoutSeconds}s)");

                        using var perTaskCts = CancellationTokenSource.CreateLinkedTokenSource(totalToken);
                        perTaskCts.CancelAfter(TimeSpan.FromSeconds(perTaskTimeoutSeconds));

                        await task.AsyncTask(perTaskCts.Token);

                        completed++;
                        OnTaskDone?.Invoke(task.TaskId);
                    }
                    catch (OperationCanceledException)
                    {
                        timedout++;
                        Debug.LogWarning($"[AppExitController] FULL task TIMEOUT: {task.TaskId}");
                        OnCleanupTimeout?.Invoke(task.TaskId);
                        // continue sang task kế
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Debug.LogError($"[AppExitController] FULL task FAILED: {task.TaskId} - {ex.Message}");
                        OnCleanupError?.Invoke(ex);
                        // continue
                    }
                }

                Debug.Log($"[AppExitController] FULL cleanup finished: Completed={completed}, Timeout={timedout}, Errors={errors}");
                OnCleanupSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AppExitController] FULL cleanup FATAL: {ex.Message}");
                OnCleanupError?.Invoke(ex);
            }
            finally
            {
                _isCleaningUp = false;

                if (_hasQuitBeenRequested)
                {
                    Debug.Log("[AppExitController] FULL cleanup done -> Quitting application...");
                    Application.Quit();

#if UNITY_EDITOR
                    EditorApplication.isPlaying = false;
#endif
                }
            }
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Chạy 1 action trên main thread Unity (dùng cho ContinueWith/Task.Run cleanup phần nhỏ).
        /// Ở đây đơn giản dùng queue qua Update; nếu bạn đã có dispatcher riêng thì thay bằng cái của bạn.
        /// </summary>
        private static readonly Queue<Action> _mainThreadQueue = new();
        private void UnityMainThreadDispatch(Action a)
        {
            if (a == null) return;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(a);
        }

        private void Update()
        {
            // pump queue
            if (_mainThreadQueue.Count == 0) return;
            Action a = null;
            lock (_mainThreadQueue)
            {
                if (_mainThreadQueue.Count > 0) a = _mainThreadQueue.Dequeue();
            }
            a?.Invoke();
        }

        [ContextMenu("Test Best-Effort Now")]
        private void TestBestEffort()
        {
            Debug.Log("[AppExitController] Manual BEST-EFFORT test");
            TryRunBestEffortCleanupNonBlocking();
        }

        [ContextMenu("Test FULL Cleanup (simulate Quit)")]
        private void TestFullCleanup()
        {
            if (_isCleaningUp) return;
            Debug.Log("[AppExitController] Manual FULL cleanup test");
            _hasQuitBeenRequested = true;
            _ = RunFullCleanup();
        }

        #endregion

        #region Types

        private readonly struct CleanupTask
        {
            public readonly string TaskId;
            public readonly Func<CancellationToken, Task> AsyncTask;
            public readonly int Order;

            public CleanupTask(string taskId, Func<CancellationToken, Task> asyncTask, int order)
            {
                TaskId = taskId;
                AsyncTask = asyncTask ?? (_ => Task.CompletedTask);
                Order = order;
            }
        }

        #endregion
    }
}

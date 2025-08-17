using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GAME.Scripts.DesignPattern;
using UnityEngine;

namespace _GAME.Scripts.Controller
{
    public class AppExitController : SingletonDontDestroy<AppExitController>
    {

        [Header("Timeouts (seconds)")] [SerializeField]
        private float perTaskTimeoutSeconds = 2f;

        [SerializeField] private float pauseBestEffortTimeoutSeconds = 1f;
        [SerializeField] private float totalCleanupTimeoutSeconds = 10f; // Timeout tổng

        public event Action OnBeforeCleanup;
        public event Action<string> OnTaskDone;
        public event Action OnCleanupSuccess;
        public event Action<string> OnCleanupTimeout;
        public event Action<Exception> OnCleanupError;

        private readonly List<CleanupTask> _tasks = new();
        private bool _isCleaningUp;
        private bool _hasQuitBeenRequested; // Tránh gọi Application.Quit() nhiều lần
        
        private void OnEnable()
        {
            Application.wantsToQuit += WantsToQuit;
        }

        private void OnDisable()
        {
            Application.wantsToQuit -= WantsToQuit;
        }

        /// <summary>
        /// Đăng ký 1 cleanup task (async). taskId cần duy nhất để có thể hủy đăng ký sau này.
        /// </summary>
        public void RegisterCleanupTask(string taskId, Func<CancellationToken, Task> asyncTask, int order = 0)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                throw new ArgumentException("taskId is required");
            if (_isCleaningUp)
            {
                Debug.LogWarning($"[AppExitManager] Cannot register task '{taskId}' while cleanup is in progress");
                return;
            }

            _tasks.RemoveAll(t => t.TaskId == taskId); // replace nếu trùng
            _tasks.Add(new CleanupTask(taskId, asyncTask, order));
            _tasks.Sort((a, b) => a.Order.CompareTo(b.Order));

            Debug.Log($"[AppExitManager] Registered cleanup task: {taskId} (order: {order})");
        }

        /// <summary>
        /// Đăng ký 1 cleanup task (sync).
        /// </summary>
        public void RegisterCleanupTask(string taskId, Action action, int order = 0)
        {
            RegisterCleanupTask(taskId, _ =>
            {
                action?.Invoke();
                return Task.CompletedTask;
            }, order);
        }

        /// <summary>
        /// Hủy đăng ký task theo id.
        /// </summary>
        public void UnregisterCleanupTask(string taskId)
        {
            if (_isCleaningUp)
            {
                Debug.LogWarning($"[AppExitManager] Cannot unregister task '{taskId}' while cleanup is in progress");
                return;
            }

            bool removed = _tasks.RemoveAll(t => t.TaskId == taskId) > 0;
            if (removed)
                Debug.Log($"[AppExitManager] Unregistered cleanup task: {taskId}");
        }

        /// <summary>
        /// Thử chạy cleanup khi app vào nền (mobile).
        /// </summary>
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && !_isCleaningUp)
            {
                Debug.Log("[AppExitManager] App paused, running best effort cleanup...");
                _ = RunCleanup(bestEffort: true);
            }
        }

        /// <summary>
        /// Thử chạy cleanup khi app mất focus.
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && !_isCleaningUp && !_hasQuitBeenRequested)
            {
                Debug.Log("[AppExitManager] App lost focus, running best effort cleanup...");
                _ = RunCleanup(bestEffort: true);
            }
        }

        /// <summary>
        /// Hook vào vòng đời quit của Unity để chạy cleanup và chỉ quit khi xong (hoặc timeout).
        /// </summary>
        private bool WantsToQuit()
        {
            if (_hasQuitBeenRequested) return true; // Đã cleanup xong rồi
            if (_isCleaningUp) return false; // Vẫn đang cleanup

            Debug.Log("[AppExitManager] Application wants to quit, starting cleanup...");
            _hasQuitBeenRequested = true;
            _ = RunCleanup(bestEffort: false);
            return false; // Chặn quit, sẽ gọi Application.Quit() sau
        }

        private async Task RunCleanup(bool bestEffort)
        {
            if (_isCleaningUp) return;
            _isCleaningUp = true;

            var cleanupType = bestEffort ? "best-effort" : "full";
            Debug.Log($"[AppExitManager] Starting {cleanupType} cleanup with {_tasks.Count} tasks...");

            try
            {
                OnBeforeCleanup?.Invoke();

                // Timeout tổng cho toàn bộ quá trình cleanup
                using var totalCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(bestEffort ? pauseBestEffortTimeoutSeconds * 2 : totalCleanupTimeoutSeconds));

                var completedTasks = 0;
                var timedOutTasks = 0;
                var errorTasks = 0;

                // Chạy tuần tự theo order
                foreach (var task in _tasks)
                {
                    if (totalCts.Token.IsCancellationRequested)
                    {
                        Debug.LogWarning($"[AppExitManager] Total timeout reached, stopping cleanup");
                        break;
                    }

                    try
                    {
                        var taskTimeout = bestEffort ? pauseBestEffortTimeoutSeconds : perTaskTimeoutSeconds;
                        using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(totalCts.Token);
                        taskCts.CancelAfter(TimeSpan.FromSeconds(taskTimeout));

                        Debug.Log($"[AppExitManager] Running task: {task.TaskId} (timeout: {taskTimeout}s)");

                        await task.AsyncTask(taskCts.Token);

                        completedTasks++;
                        Debug.Log($"[AppExitManager] Task completed: {task.TaskId}");
                        OnTaskDone?.Invoke(task.TaskId);
                    }
                    catch (OperationCanceledException)
                    {
                        timedOutTasks++;
                        Debug.LogWarning($"[AppExitManager] Task timed out: {task.TaskId}");
                        OnCleanupTimeout?.Invoke(task.TaskId);

                        if (!bestEffort) continue; // Tiếp tục với task khác
                        else break; // Best effort thì dừng luôn
                    }
                    catch (Exception ex)
                    {
                        errorTasks++;
                        Debug.LogError($"[AppExitManager] Task failed: {task.TaskId} - {ex.Message}");
                        OnCleanupError?.Invoke(ex);

                        if (!bestEffort) continue; // Tiếp tục với task khác
                        else break; // Best effort thì dừng luôn
                    }
                }

                Debug.Log(
                    $"[AppExitManager] Cleanup finished - Completed: {completedTasks}, Timeout: {timedOutTasks}, Errors: {errorTasks}");
                OnCleanupSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AppExitManager] Fatal error during cleanup: {ex.Message}");
                OnCleanupError?.Invoke(ex);
            }
            finally
            {
                _isCleaningUp = false;

                if (!bestEffort && _hasQuitBeenRequested)
                {
                    Debug.Log("[AppExitManager] Cleanup done, quitting application...");
                    // Kết thúc vòng đời app ở đây
                    Application.Quit();

#if UNITY_EDITOR
                    // Trong editor thì quit không hoạt động, dừng play mode
                    UnityEditor.EditorApplication.isPlaying = false;
#endif
                }
            }
        }

        /// <summary>
        /// Manually trigger cleanup (for testing)
        /// </summary>
        [ContextMenu("Test Cleanup")]
        public void TestCleanup()
        {
            if (!_isCleaningUp)
            {
                Debug.Log("[AppExitManager] Manual cleanup test started");
                _ = RunCleanup(bestEffort: true);
            }
        }

        /// <summary>
        /// Get current registered tasks info
        /// </summary>
        public List<(string TaskId, int Order)> GetRegisteredTasks()
        {
            var result = new List<(string, int)>();
            foreach (var task in _tasks)
            {
                result.Add((task.TaskId, task.Order));
            }

            return result;
        }

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
    }
}
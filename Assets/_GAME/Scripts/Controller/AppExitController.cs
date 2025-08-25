using System;
using System.Collections;
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
    /// AppExitController - Mobile Optimized:
    /// - Desktop: Dùng wantsToQuit cho full cleanup
    /// - Mobile: Dùng OnApplicationPause(true) cho cleanup vì mobile không có quit event rõ ràng
    /// - iOS: Cleanup khi vào background vì app có thể bị kill bất cứ lúc nào
    /// - Android: Tương tự iOS nhưng có thêm onLowMemory handling
    /// </summary>
    public class AppExitController : SingletonDontDestroy<AppExitController>
    {
        [Header("Timeouts (seconds)")]
        [Tooltip("Timeout cho mỗi full-cleanup task")]
        [SerializeField] private float perTaskTimeoutSeconds = 2f;

        [Tooltip("Tổng timeout cho full cleanup")]
        [SerializeField] private float totalCleanupTimeoutSeconds = 10f;

        [Tooltip("Ngân sách cho best-effort cleanup (s)")]
        [SerializeField] private float bestEffortBudgetSeconds = 1f;

        [Header("Mobile Settings")]
        [Tooltip("Thời gian chờ trước khi chạy full cleanup trên mobile (s)")]
        [SerializeField] private float mobileFullCleanupDelaySeconds = 0.5f;

        [Tooltip("Có chạy full cleanup khi pause trên mobile không")]
        [SerializeField] private bool runFullCleanupOnMobilePause = true;

        [Tooltip("Có lưu state khi vào background không")]
        [SerializeField] private bool autoSaveOnBackground = true;

        [Header("Stability")]
        [Tooltip("Debounce thời gian để tránh spam focus")]
        [SerializeField] private float focusDebounceSeconds = 0.5f;

        // Events
        public event Action OnBeforeCleanup;
        public event Action<string> OnTaskDone;
        public event Action OnCleanupSuccess;
        public event Action<string> OnCleanupTimeout;
        public event Action<Exception> OnCleanupError;
        public event Action OnAfterResume;

        // Mobile specific events
        public event Action OnEnterBackground;   // Khi app vào background
        public event Action OnEnterForeground;   // Khi app quay lại foreground
        public event Action OnLowMemory;         // Khi hệ thống báo low memory

        // Task lists
        private readonly List<CleanupTask> _bestEffortTasks = new();
        private readonly List<CleanupTask> _fullTasks = new();

        // State
        private bool _isCleaningUp;
        private bool _hasQuitBeenRequested;
        private bool _isInBackground;
        private bool _hasRunBackgroundCleanup;
        private CancellationTokenSource _bestEffortCts;
        private CancellationTokenSource _fullCleanupCts;
        private float _lastFocusOrPauseTs;
        private Coroutine _delayedCleanupCoroutine;

        // Platform detection
        private bool IsMobile => Application.isMobilePlatform;
        private bool IsIOS => Application.platform == RuntimePlatform.IPhonePlayer;
        private bool IsAndroid => Application.platform == RuntimePlatform.Android;

        protected override void Awake()
        {
            base.Awake();
            Debug.Log($"[AppExitController] Platform: {Application.platform}, IsMobile: {IsMobile}");
        }

        private void OnEnable()
        {
            Application.wantsToQuit += WantsToQuit;
            
            // Mobile specific: Listen for low memory warnings
            if (IsAndroid)
            {
                Application.lowMemory += OnApplicationLowMemory;
            }
        }

        private void OnDisable()
        {
            Application.wantsToQuit -= WantsToQuit;
            
            if (IsAndroid)
            {
                Application.lowMemory -= OnApplicationLowMemory;
            }

            // Cancel any running operations
            _bestEffortCts?.Cancel();
            _fullCleanupCts?.Cancel();
            
            if (_delayedCleanupCoroutine != null)
            {
                StopCoroutine(_delayedCleanupCoroutine);
            }
        }

        #region Registration API

        public void RegisterBestEffortTask(string taskId, Action<CancellationToken> action, int order = 0)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                throw new ArgumentException("taskId is required");

            if (action == null)
                throw new ArgumentNullException(nameof(action));

            _bestEffortTasks.RemoveAll(t => t.TaskId == taskId);
            _bestEffortTasks.Add(new CleanupTask(taskId, ct => { action(ct); return Task.CompletedTask; }, order));
            _bestEffortTasks.Sort((a, b) => a.Order.CompareTo(b.Order));

            Debug.Log($"[AppExitController] Registered BEST-EFFORT task: {taskId} (order:{order})");
        }

        public void RegisterFullCleanupTask(string taskId, Func<CancellationToken, Task> asyncTask, int order = 0)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                throw new ArgumentException("taskId is required");

            if (asyncTask == null)
                throw new ArgumentNullException(nameof(asyncTask));

            if (_isCleaningUp)
            {
                Debug.LogWarning($"[AppExitController] Cannot register full task '{taskId}' while cleanup is in progress");
                return;
            }

            _fullTasks.RemoveAll(t => t.TaskId == taskId);
            _fullTasks.Add(new CleanupTask(taskId, asyncTask, order));
            _fullTasks.Sort((a, b) => a.Order.CompareTo(b.Order));

            Debug.Log($"[AppExitController] Registered FULL task: {taskId} (order:{order})");
        }

        public void UnregisterBestEffortTask(string taskId)
        {
            bool removed = _bestEffortTasks.RemoveAll(t => t.TaskId == taskId) > 0;
            if (removed) Debug.Log($"[AppExitController] Unregistered BEST-EFFORT task: {taskId}");
        }

        public void UnregisterFullCleanupTask(string taskId)
        {
            if (_isCleaningUp)
            {
                Debug.LogWarning($"[AppExitController] Cannot unregister full task '{taskId}' while cleanup is in progress");
                return;
            }

            bool removed = _fullTasks.RemoveAll(t => t.TaskId == taskId) > 0;
            if (removed) Debug.Log($"[AppExitController] Unregistered FULL task: {taskId}");
        }

        public (List<(string TaskId, int Order)> bestEffort, List<(string TaskId, int Order)> full) GetRegisteredTasks()
        {
            var be = new List<(string, int)>();
            foreach (var t in _bestEffortTasks) be.Add((t.TaskId, t.Order));

            var fu = new List<(string, int)>();
            foreach (var t in _fullTasks) fu.Add((t.TaskId, t.Order));

            return (be, fu);
        }

        #endregion

        #region Unity lifecycle hooks

        private void OnApplicationPause(bool pauseStatus)
        {
            _lastFocusOrPauseTs = Time.realtimeSinceStartup;

            if (pauseStatus)
            {
                // Vào background
                _isInBackground = true;
                _hasRunBackgroundCleanup = false;
                OnEnterBackground?.Invoke();

                Debug.Log($"[AppExitController] App paused (platform: {Application.platform})");

                if (IsMobile)
                {
                    // Mobile: Chạy best-effort ngay lập tức
                    TryRunBestEffortCleanupNonBlocking();

                    // Mobile: Có thể chạy full cleanup sau một khoảng delay ngắn
                    if (runFullCleanupOnMobilePause && !_hasRunBackgroundCleanup)
                    {
                        _delayedCleanupCoroutine = StartCoroutine(DelayedFullCleanupCoroutine());
                    }
                }
                else
                {
                    // Desktop: Chỉ chạy best-effort
                    TryRunBestEffortCleanupNonBlocking();
                }
            }
            else
            {
                // Quay lại foreground
                _isInBackground = false;
                OnEnterForeground?.Invoke();
                RecoverAfterResume();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _lastFocusOrPauseTs = Time.realtimeSinceStartup;

            if (!hasFocus && !_isInBackground)
            {
                // Mất focus nhưng chưa pause (có thể là popup, dialog...)
                TryRunBestEffortCleanupNonBlocking();
            }
            else if (hasFocus && !_isInBackground)
            {
                // Có focus và không trong background
                RecoverAfterResume();
            }
        }

        private void OnApplicationLowMemory()
        {
            Debug.LogWarning("[AppExitController] Low memory warning received - running emergency cleanup");
            OnLowMemory?.Invoke();
            
            // Chạy best-effort cleanup ngay lập tức
            TryRunBestEffortCleanupNonBlocking();
            
            // Có thể force một số full cleanup tasks quan trọng
            if (!_isCleaningUp)
            {
                _ = RunEmergencyCleanup();
            }
        }

        #endregion

        #region Mobile delayed cleanup

        private IEnumerator DelayedFullCleanupCoroutine()
        {
            yield return new WaitForSeconds(mobileFullCleanupDelaySeconds);
            
            if (_isInBackground && !_hasRunBackgroundCleanup && !_isCleaningUp)
            {
                Debug.Log("[AppExitController] Running delayed full cleanup for mobile background");
                _hasRunBackgroundCleanup = true;
                _ = RunFullCleanup(isMobileBackground: true);
            }
        }

        #endregion

        #region Best-effort cleanup (non-blocking)

        private void TryRunBestEffortCleanupNonBlocking()
        {
            if (_bestEffortCts != null) return; // đã chạy rồi
            if (_bestEffortTasks.Count == 0) return;

            Debug.Log($"[AppExitController] Best-effort cleanup START with [{_bestEffortTasks.Count}] tasks");

            _bestEffortCts = new CancellationTokenSource();
            var ct = _bestEffortCts.Token;

            _ = Task.Run(() =>
            {
                try
                {
                    int perTaskMs = Mathf.Max(10,
                        (int)(bestEffortBudgetSeconds * 1000f / Mathf.Max(1, _bestEffortTasks.Count)));

                    foreach (var task in _bestEffortTasks)
                    {
                        if (ct.IsCancellationRequested) break;

                        try
                        {
                            var t = task.AsyncTask(ct);
                            t.Wait(perTaskMs);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[AppExitController] Best-effort task '{task.TaskId}' error: {ex.Message}");
                        }
                    }
                }
                finally
                {
                    UnityMainThreadDispatch(() =>
                    {
                        _bestEffortCts?.Dispose();
                        _bestEffortCts = null;
                        Debug.Log("[AppExitController] Best-effort cleanup DONE");
                    });
                }
            });
        }

        #endregion

        #region Full cleanup

        private bool WantsToQuit()
        {
            if (IsMobile)
            {
                // Mobile thường không gọi wantsToQuit, xử lý qua OnApplicationPause
                Debug.Log("[AppExitController] wantsToQuit called on mobile - allowing quit");
                return true;
            }

            if (_hasQuitBeenRequested) return true;
            if (_isCleaningUp) return false;

            Debug.Log("[AppExitController] Application wants to quit -> FULL cleanup starting...");
            _hasQuitBeenRequested = true;
            _ = RunFullCleanup(isMobileBackground: false);
            return false;
        }

        private async Task RunFullCleanup(bool isMobileBackground = false)
        {
            if (_isCleaningUp) return;
            _isCleaningUp = true;

            try
            {
                OnBeforeCleanup?.Invoke();

                // Mobile background cleanup có timeout ngắn hơn
                float timeoutSeconds = isMobileBackground ? 
                    Mathf.Min(totalCleanupTimeoutSeconds, 5f) : 
                    totalCleanupTimeoutSeconds;

                _fullCleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var totalToken = _fullCleanupCts.Token;

                int completed = 0, timedout = 0, errors = 0;

                foreach (var task in _fullTasks)
                {
                    if (totalToken.IsCancellationRequested)
                    {
                        Debug.LogWarning("[AppExitController] FULL cleanup: timeout reached, stopping.");
                        break;
                    }

                    try
                    {
                        // Mobile background có timeout per-task ngắn hơn
                        float taskTimeout = isMobileBackground ? 
                            Mathf.Min(perTaskTimeoutSeconds, 1f) : 
                            perTaskTimeoutSeconds;

                        Debug.Log($"[AppExitController] FULL task: {task.TaskId} (timeout:{taskTimeout}s, mobile:{isMobileBackground})");

                        using var perTaskCts = CancellationTokenSource.CreateLinkedTokenSource(totalToken);
                        perTaskCts.CancelAfter(TimeSpan.FromSeconds(taskTimeout));

                        await task.AsyncTask(perTaskCts.Token);

                        completed++;
                        OnTaskDone?.Invoke(task.TaskId);
                    }
                    catch (OperationCanceledException)
                    {
                        timedout++;
                        Debug.LogWarning($"[AppExitController] FULL task TIMEOUT: {task.TaskId}");
                        OnCleanupTimeout?.Invoke(task.TaskId);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Debug.LogError($"[AppExitController] FULL task FAILED: {task.TaskId} - {ex.Message}");
                        OnCleanupError?.Invoke(ex);
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
                _fullCleanupCts?.Dispose();
                _fullCleanupCts = null;
                _isCleaningUp = false;

                if (_hasQuitBeenRequested && !isMobileBackground)
                {
                    Debug.Log("[AppExitController] FULL cleanup done -> Quitting application...");
                    Application.Quit();

#if UNITY_EDITOR
                    EditorApplication.isPlaying = false;
#endif
                }
            }
        }

        private async Task RunEmergencyCleanup()
        {
            if (_isCleaningUp) return;

            Debug.Log("[AppExitController] Running EMERGENCY cleanup due to low memory");
            
            // Chạy một số tasks quan trọng nhất với timeout rất ngắn
            var emergencyTasks = _fullTasks.FindAll(t => t.Order <= 10); // Chỉ tasks có priority cao
            if (emergencyTasks.Count == 0) return;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2f));
                
                foreach (var task in emergencyTasks)
                {
                    if (cts.Token.IsCancellationRequested) break;
                    
                    try
                    {
                        using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        taskCts.CancelAfter(TimeSpan.FromMilliseconds(500));
                        
                        await task.AsyncTask(taskCts.Token);
                        Debug.Log($"[AppExitController] Emergency cleanup: {task.TaskId} completed");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[AppExitController] Emergency cleanup: {task.TaskId} failed - {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AppExitController] Emergency cleanup failed: {ex.Message}");
            }
        }

        #endregion

        #region Recovery

        private void RecoverAfterResume()
        {
            if (Time.realtimeSinceStartup - _lastFocusOrPauseTs < focusDebounceSeconds) return;

            // Cancel các operations đang chạy
            if (_bestEffortCts != null)
            {
                _bestEffortCts.Cancel();
                _bestEffortCts.Dispose();
                _bestEffortCts = null;
            }

            if (_delayedCleanupCoroutine != null)
            {
                StopCoroutine(_delayedCleanupCoroutine);
                _delayedCleanupCoroutine = null;
            }

            // Reset cleanup state nếu cần
            if (_isCleaningUp && IsMobile)
            {
                Debug.LogWarning("[AppExitController] Forcing cleanup state reset after mobile resume.");
                _fullCleanupCts?.Cancel();
                _isCleaningUp = false;
            }

            OnAfterResume?.Invoke();
        }

        #endregion

        #region Utilities

        private static readonly Queue<Action> _mainThreadQueue = new();
        
        private void UnityMainThreadDispatch(Action a)
        {
            if (a == null) return;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(a);
        }

        private void Update()
        {
            if (_mainThreadQueue.Count == 0) return;
            Action a = null;
            lock (_mainThreadQueue)
            {
                if (_mainThreadQueue.Count > 0) a = _mainThreadQueue.Dequeue();
            }
            a?.Invoke();
        }

        #endregion

        #region Debug Methods

        [ContextMenu("Test Best-Effort Now")]
        private void TestBestEffort()
        {
            Debug.Log("[AppExitController] Manual BEST-EFFORT test");
            TryRunBestEffortCleanupNonBlocking();
        }

        [ContextMenu("Test FULL Cleanup")]
        private void TestFullCleanup()
        {
            if (_isCleaningUp) return;
            Debug.Log("[AppExitController] Manual FULL cleanup test");
            _ = RunFullCleanup(false);
        }

        [ContextMenu("Test Mobile Background Cleanup")]
        private void TestMobileBackgroundCleanup()
        {
            if (_isCleaningUp) return;
            Debug.Log("[AppExitController] Manual MOBILE background cleanup test");
            _ = RunFullCleanup(true);
        }

        [ContextMenu("Simulate Low Memory")]
        private void SimulateLowMemory()
        {
            OnApplicationLowMemory();
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
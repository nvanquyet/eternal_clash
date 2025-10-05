// PlayFabAuthManager.cs — optimized & aligned with CloudScript (unified parsing, rate-limit safe, resilient session)

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GAME.Scripts.DesignPattern;
using Newtonsoft.Json.Linq;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace _GAME.Scripts.Authenticator
{
    public class SecurityAlertInfo
    {
        public string AttemptDeviceInfo;
        public string AttemptLocation;
        public DateTime AttemptTime;
        public string AttemptIP;
    }

    public class PlayFabAuthManager : MonoBehaviour, IAuthManager
    {
        [Header("Session Settings")] [SerializeField]
        private bool enableSessionProtection = true;

        [SerializeField] private float loginProcessTimeoutSeconds = 30f;
        [SerializeField] private float sessionHeartbeatIntervalSeconds = 45f;
        [SerializeField] private float sessionTimeoutSeconds = 300f; // 5 phút
        [SerializeField] private float securityAlertCheckIntervalSeconds = 30f; // mỗi 30s

        // limits
        private const float MIN_HEARTBEAT_GAP_SECONDS = 30f;
        private const float MIN_LOGIN_RETRY_INTERVAL = 3f;
        private const int MAX_SESSION_RETRY = 2;

        // CloudScript function names (avoid typo)
        private static class CS
        {
            public const string CheckExistingOnlineSessions = "CheckExistingOnlineSessions";
            public const string EstablishNewSession = "EstablishNewSession";
            public const string UpdateHeartbeat = "UpdateHeartbeat"; // dùng bản thường (server đã chuẩn hoá)
            public const string SecureLogout = "SecureLogout";
            public const string GetPendingSecurityAlerts = "GetPendingSecurityAlerts";
            public const string MarkSecurityAlertRead = "MarkSecurityAlertRead";
            public const string NotifySecurityAlert = "NotifySecurityAlert";
            public const string CheckUserOnlineStatus = "CheckUserOnlineStatus";
        }

        private bool isLoggedIn;
        private string userId = string.Empty;
        private string currentSessionId = string.Empty;
        private bool isCurrentlyLoggingIn = false;
        private DateTime loginStartTime;
        private DateTime lastHeartbeat;
        private DateTime lastLoginAttempt = DateTime.MinValue;
        private int loginAttemptCount = 0;
        private int sessionRetryCount = 0;
        private bool maintenanceStarted = false;

        private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        // Events
        public event Action<SecurityAlertInfo> OnSecurityAlert;
        public event Action OnSessionExpired;
        public event Action<string> OnLoginSuccess;
        public event Action<string> OnLoginFailed;

        public bool IsLoggedIn => isLoggedIn;
        public string UserId => userId;
        public string CurrentSessionId => currentSessionId;
        public bool IsCurrentlyLoggingIn => isCurrentlyLoggingIn;

        protected void Awake()
        {
            currentSessionId = GenerateSessionId();
        }

        // private void Start()
        // {
        //     if (enableSessionProtection && isLoggedIn)
        //         StartSessionMaintenance();
        // }
        //
        // private void OnDestroy()
        // {
        //     StopSessionMaintenance();
        // }

        #region Login Flow

        public async Task<(bool success, string message)> LoginAsync(string userOrEmail, string password)
        {
            if (!ValidateLoginInput(userOrEmail, password, out var validationError))
            {
                OnLoginFailed?.Invoke(validationError);
                return (false, validationError);
            }

            var now = DateTime.UtcNow;
            var sinceLast = (now - lastLoginAttempt).TotalSeconds;

            if (sinceLast < MIN_LOGIN_RETRY_INTERVAL)
            {
                var wait = Mathf.CeilToInt(MIN_LOGIN_RETRY_INTERVAL - (float)sinceLast);
                var msg = $"Please wait {wait} seconds before retrying.";
                OnLoginFailed?.Invoke(msg);
                return (false, msg);
            }

            if (loginAttemptCount > 0)
            {
                var backoff = Math.Min(loginAttemptCount * 2, 10);
                if (sinceLast < backoff)
                {
                    var remain = Mathf.CeilToInt(backoff - (float)sinceLast);
                    var msg = $"Too many attempts. Please wait {remain} seconds.";
                    OnLoginFailed?.Invoke(msg);
                    return (false, msg);
                }
            }

            if (isCurrentlyLoggingIn)
            {
                const string msg = "Already logging in, please wait...";
                OnLoginFailed?.Invoke(msg);
                return (false, msg);
            }

            try
            {
                isCurrentlyLoggingIn = true;
                lastLoginAttempt = DateTime.UtcNow;

                // Chỉ cần PlayFab authentication
                var loginResult = await AuthenticateUser(userOrEmail, password);
                if (!loginResult.success)
                {
                    loginAttemptCount++;
                    OnLoginFailed?.Invoke(loginResult.message);
                    return (false, loginResult.message);
                }

                // Set basic state
                userId = loginResult.playFabId;
                isLoggedIn = true;
                loginAttemptCount = 0;

                OnLoginSuccess?.Invoke("Login successful!");
                return (true, "Login successful!");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayFabAuth] Login exception: {ex.Message}");
                loginAttemptCount++;
                const string msg = "Has occurred an error during login. Please try again.";
                OnLoginFailed?.Invoke(msg);
                return (false, msg);
            }
            finally
            {
                isCurrentlyLoggingIn = false;
            }
        }

        // public async Task<(bool success, string message)> LoginAsync(string userOrEmail, string password)
        // {
        //     if (!ValidateLoginInput(userOrEmail, password, out var validationError))
        //     {
        //         OnLoginFailed?.Invoke(validationError);
        //         return (false, validationError);
        //     }
        //
        //     var now = DateTime.UtcNow;
        //     var sinceLast = (now - lastLoginAttempt).TotalSeconds;
        //
        //     if (sinceLast < MIN_LOGIN_RETRY_INTERVAL)
        //     {
        //         var wait = Mathf.CeilToInt(MIN_LOGIN_RETRY_INTERVAL - (float)sinceLast);
        //         var msg = $"Vui lòng chờ {wait} giây trước khi thử lại.";
        //         OnLoginFailed?.Invoke(msg);
        //         return (false, msg);
        //     }
        //
        //     if (loginAttemptCount > 0)
        //     {
        //         var backoff = Math.Min(loginAttemptCount * 2, 10);
        //         if (sinceLast < backoff)
        //         {
        //             var remain = Mathf.CeilToInt(backoff - (float)sinceLast);
        //             var msg = $"Quá nhiều lần thử. Vui lòng chờ {remain} giây.";
        //             OnLoginFailed?.Invoke(msg);
        //             return (false, msg);
        //         }
        //     }
        //
        //     if (isCurrentlyLoggingIn)
        //     {
        //         const string msg = "Đang trong quá trình đăng nhập, vui lòng chờ...";
        //         OnLoginFailed?.Invoke(msg);
        //         return (false, msg);
        //     }
        //
        //     try
        //     {
        //         isCurrentlyLoggingIn = true;
        //         loginStartTime = DateTime.UtcNow;
        //         lastLoginAttempt = DateTime.UtcNow;
        //
        //         // 1) PlayFab Authentication
        //         var loginResult = await AuthenticateUser(userOrEmail, password);
        //         if (!loginResult.success)
        //         {
        //             loginAttemptCount++;
        //             OnLoginFailed?.Invoke(loginResult.message);
        //             return (false, loginResult.message);
        //         }
        //
        //         userId = loginResult.playFabId;
        //
        //         // 2) Thiết lập session mới (optional - chỉ nếu cần session management)
        //         if (enableSessionProtection)
        //         {
        //             var sessionNew = await EstablishNewSession(userId);
        //             if (!sessionNew.success)
        //             {
        //                 Debug.LogWarning($"[PlayFabAuth] Session establishment failed: {sessionNew.message}");
        //                 // Không fail login, chỉ disable session protection
        //                 enableSessionProtection = false;
        //             }
        //             else
        //             {
        //                 currentSessionId = sessionNew.sessionId;
        //             }
        //         }
        //
        //         // 3) Set local state
        //         isLoggedIn = true;
        //         lastHeartbeat = DateTime.UtcNow;
        //         loginAttemptCount = 0;
        //         sessionRetryCount = 0;
        //
        //         // 4) Start session maintenance (nếu enabled)
        //         if (enableSessionProtection && isLoggedIn)
        //         {
        //             StartSessionMaintenance();
        //         }
        //
        //         OnLoginSuccess?.Invoke("Đăng nhập thành công!");
        //         return (true, "Đăng nhập thành công!");
        //     }
        //     catch (Exception ex)
        //     {
        //         Debug.LogError($"[PlayFabAuth] Login exception: {ex.Message}");
        //         await ForceLogoutQuiet();
        //         loginAttemptCount++;
        //         const string msg = "Đã xảy ra lỗi trong quá trình đăng nhập. Vui lòng thử lại.";
        //         OnLoginFailed?.Invoke(msg);
        //         return (false, msg);
        //     }
        //     finally
        //     {
        //         isCurrentlyLoggingIn = false;
        //     }
        // }


        // Simplified session establishment - không gửi security alert
        // private async Task<SessionResult> EstablishNewSession(string playFabId)
        // {
        //     var newSessionId = GenerateSessionId();
        //
        //     var (ok, payload, err) = await ExecuteCSAsync(CS.EstablishNewSession, new Dictionary<string, object>
        //     {
        //         ["playerId"] = playFabId,
        //         ["sessionId"] = newSessionId,
        //         ["deviceId"] = SystemInfo.deviceUniqueIdentifier,
        //         ["deviceInfo"] = GetDeviceInfo(),
        //         ["sessionTimeoutSeconds"] = sessionTimeoutSeconds
        //     });
        //
        //     if (!ok || payload == null)
        //     {
        //         Debug.LogWarning($"[PlayFabAuth] EstablishNewSession failed: {err ?? "No response"}");
        //         return new SessionResult { success = false, message = err ?? "No response" };
        //     }
        //
        //     var success = TryGet(payload, "success", out bool s) && s;
        //     var message = TryGet(payload, "message", out string m) ? m : "ok";
        //     return new SessionResult { success = success, sessionId = newSessionId, message = message };
        // }

        // private void StartSessionMaintenance()
        // {
        //     if (!enableSessionProtection || !isLoggedIn) return;
        //     if (maintenanceStarted) return;
        //
        //     maintenanceStarted = true;
        //
        //     CancelInvoke(nameof(SendHeartbeat));
        //     InvokeRepeating(nameof(SendHeartbeat), sessionHeartbeatIntervalSeconds, sessionHeartbeatIntervalSeconds);
        //
        //     // Bỏ security alert check nếu không cần
        //     // CancelInvoke(nameof(CheckSecurityAlerts));
        //     // InvokeRepeating(nameof(CheckSecurityAlerts), 10f, securityAlertCheckIntervalSeconds);
        // }


// Optional: Method để enable/disable session protection
        // public void SetSessionProtection(bool enable)
        // {
        //     enableSessionProtection = enable;
        //     if (!enable && maintenanceStarted)
        //     {
        //         StopSessionMaintenance();
        //     }
        //     else if (enable && isLoggedIn && !maintenanceStarted)
        //     {
        //         StartSessionMaintenance();
        //     }
        // }

// Helper methods với retry logic
        // private async Task<OnlineCheckResult> CheckExistingOnlineSessionWithRetry(int maxRetries = 3)
        // {
        //     for (int i = 0; i < maxRetries; i++)
        //     {
        //         try
        //         {
        //             var result = await CheckExistingOnlineSession();
        //             if (result.success) return result;
        //
        //             if (i < maxRetries - 1) // không phải lần cuối
        //             {
        //                 await Task.Delay(UnityEngine.Random.Range(800, 1500)); // exponential backoff
        //             }
        //         }
        //         catch (Exception ex)
        //         {
        //             Debug.LogWarning($"[PlayFabAuth] CheckExistingOnlineSession attempt {i + 1} failed: {ex.Message}");
        //             if (i < maxRetries - 1)
        //             {
        //                 await Task.Delay(UnityEngine.Random.Range(1000, 2000));
        //             }
        //         }
        //     }
        //
        //     return new OnlineCheckResult
        //         { success = false, message = "Không thể kiểm tra phiên đăng nhập sau nhiều lần thử" };
        // }


        private async Task<BasicLoginResult> AuthenticateUser(string userOrEmail, string password)
        {
            var tcs = new TaskCompletionSource<BasicLoginResult>();

            if (userOrEmail.Contains("@"))
            {
                var req = new LoginWithEmailAddressRequest { Email = userOrEmail, Password = password };
                PlayFabClientAPI.LoginWithEmailAddress(req,
                    r => tcs.TrySetResult(new BasicLoginResult
                        { success = true, playFabId = r.PlayFabId, sessionToken = r.SessionTicket, message = "ok" }),
                    e => tcs.TrySetResult(new BasicLoginResult { success = false, message = GetLoginErrorMessage(e) })
                );
            }
            else
            {
                var req = new LoginWithPlayFabRequest { Username = userOrEmail, Password = password };
                PlayFabClientAPI.LoginWithPlayFab(req,
                    r => tcs.TrySetResult(new BasicLoginResult
                        { success = true, playFabId = r.PlayFabId, sessionToken = r.SessionTicket, message = "ok" }),
                    e => tcs.TrySetResult(new BasicLoginResult { success = false, message = GetLoginErrorMessage(e) })
                );
            }

            return await tcs.Task;
        }

        #endregion

        #region CloudScript helpers

        private async Task<(bool success, JObject payload, string error)> ExecuteCSAsync(string functionName,
            object param)
        {
            var tcs = new TaskCompletionSource<(bool, JObject, string)>();

            var req = new ExecuteCloudScriptRequest
            {
                FunctionName = functionName,
                FunctionParameter = param,
                GeneratePlayStreamEvent = false
            };

            PlayFabClientAPI.ExecuteCloudScript(req,
                result =>
                {
                    try
                    {
                        if (result.FunctionResult == null)
                        {
                            tcs.TrySetResult((false, null, "No response"));
                            return;
                        }

                        JObject jo = result.FunctionResult as JObject;
                        if (jo == null)
                        {
                            if (result.FunctionResult is string s) jo = JObject.Parse(s);
                            else jo = JObject.FromObject(result.FunctionResult);
                        }

                        tcs.TrySetResult((true, jo, null));
                    }
                    catch (Exception e)
                    {
                        tcs.TrySetResult((false, null, $"Parse error: {e.Message}"));
                    }
                },
                error => tcs.TrySetResult((false, null, error.ErrorMessage))
            );

            return await tcs.Task;
        }

        private static bool TryGet<T>(JObject obj, string key, out T value)
        {
            value = default;
            if (obj == null) return false;
            if (!obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var token)) return false;
            try
            {
                value = token.ToObject<T>();
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Server Calls

        private async Task<OnlineCheckResult> CheckExistingOnlineSession()
        {
            var (ok, payload, err) = await ExecuteCSAsync(CS.CheckExistingOnlineSessions, new Dictionary<string, object>
            {
                ["playerId"] = userId,
                ["deviceId"] = SystemInfo.deviceUniqueIdentifier,
                ["deviceInfo"] = GetDeviceInfo()
            });

            if (!ok || payload == null)
                return new OnlineCheckResult { success = false, message = err ?? "No response" };

            return new OnlineCheckResult
            {
                success = TryGet(payload, "success", out bool succ) && succ,
                hasOtherOnlineSession = TryGet(payload, "hasOtherOnlineSession", out bool other) && other,
                message = TryGet(payload, "message", out string msg) ? msg : ""
            };
        }

        // private async Task SendSecurityAlert(string targetPlayFabId, string attemptAccount)
        // {
        //     try
        //     {
        //         _ = await ExecuteCSAsync(CS.NotifySecurityAlert, new Dictionary<string, object>
        //         {
        //             ["playerId"] = targetPlayFabId,
        //             ["targetUserId"] = targetPlayFabId,
        //             ["attemptAccount"] = attemptAccount,
        //             ["alertInfo"] = new Dictionary<string, object>
        //             {
        //                 ["AttemptDeviceInfo"] = GetDeviceInfo(),
        //                 ["AttemptLocation"] = GetClientLocation(),
        //                 ["AttemptTime"] = DateTime.UtcNow.ToString("O"),
        //                 ["AttemptIP"] = GetClientIP()
        //             }
        //         });
        //     }
        //     catch (Exception e)
        //     {
        //         Debug.LogWarning($"[PlayFabAuth] SendSecurityAlert error: {e.Message}");
        //     }
        // }


        private async Task<bool> SecureLogoutViaCloudScript()
        {
            var (ok, _, __) =
                await ExecuteCSAsync(CS.SecureLogout, new Dictionary<string, object> { ["playerId"] = userId });
            return ok;
        }

        #endregion

        #region Maintenance

        // private void StopSessionMaintenance()
        // {
        //     if (!maintenanceStarted) return;
        //     maintenanceStarted = false;
        //
        //     CancelInvoke(nameof(SendHeartbeat));
        //     CancelInvoke(nameof(CheckSecurityAlerts));
        // }

        // Improved heartbeat với better validation
        // private void SendHeartbeat()
        // {
        //     if (!isLoggedIn || !enableSessionProtection) return;
        //     if (string.IsNullOrEmpty(currentSessionId) || string.IsNullOrEmpty(userId)) return;
        //
        //     var sinceLogin = (DateTime.UtcNow - loginStartTime).TotalSeconds;
        //     if (sinceLogin < 15) return; // tăng từ 10s lên 15s
        //
        //     var sinceBeat = (DateTime.UtcNow - lastHeartbeat).TotalSeconds;
        //     if (sinceBeat < MIN_HEARTBEAT_GAP_SECONDS) return;
        //
        //     _ = SendHeartbeatAsync();
        // }


        // private async Task SendHeartbeatAsync()
        // {
        //     try
        //     {
        //         var (ok, payload, err) = await ExecuteCSAsync(CS.UpdateHeartbeat, new Dictionary<string, object>
        //         {
        //             ["playerId"] = userId,
        //             ["sessionId"] = currentSessionId
        //         });
        //
        //         if (!ok || payload == null)
        //         {
        //             Debug.LogWarning($"[PlayFabAuth] Heartbeat failed: {err ?? "no payload"}");
        //             // Không gọi CheckSessionExpiration() ngay lập tức, cho thêm cơ hội retry
        //             return;
        //         }
        //
        //         if (TryGet(payload, "success", out bool succ) && succ)
        //         {
        //             lastHeartbeat = DateTime.UtcNow;
        //             Debug.Log($"[PlayFabAuth] Heartbeat successful at {lastHeartbeat:HH:mm:ss}");
        //             return;
        //         }
        //
        //         var errorCode = TryGet(payload, "errorCode", out string ec) ? ec : "UNKNOWN";
        //         Debug.LogWarning($"[PlayFabAuth] Heartbeat failed with error: {errorCode}");
        //
        //         if (errorCode == "INVALID_SESSION")
        //         {
        //             _ = HandleInvalidSessionWithRetryAsync();
        //         }
        //         else if (errorCode == "NO_SESSION" || errorCode == "CORRUPTED_SESSION")
        //         {
        //             // Thử establish session mới
        //             _ = HandleInvalidSessionWithRetryAsync();
        //         }
        //         else
        //         {
        //             // Các lỗi khác - có thể là network, chờ lần sau
        //             Debug.LogWarning($"[PlayFabAuth] Heartbeat error {errorCode}, will retry next cycle");
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         Debug.LogError($"[PlayFabAuth] SendHeartbeatAsync exception: {ex.Message}");
        //     }
        // }

        // private async Task HandleInvalidSessionWithRetryAsync()
        // {
        //     sessionRetryCount++;
        //     if (sessionRetryCount <= MAX_SESSION_RETRY)
        //     {
        //         var re = await EstablishNewSession(userId);
        //         if (re.success)
        //         {
        //             currentSessionId = re.sessionId;
        //             lastHeartbeat = DateTime.UtcNow;
        //             sessionRetryCount = 0;
        //             return;
        //         }
        //     }
        //
        //     sessionRetryCount = 0;
        //     HandleInvalidSession();
        // }

        // private void HandleInvalidSession()
        // {
        //     Debug.LogWarning("[PlayFabAuth] Invalid session detected - forcing logout");
        //     ForceLogout("Phiên đăng nhập không hợp lệ");
        // }

        private void CheckSessionExpiration()
        {
            if (!isLoggedIn) return;
            var gap = (DateTime.UtcNow - lastHeartbeat).TotalSeconds;
            if (gap > sessionTimeoutSeconds)
            {
                Debug.LogWarning("[PlayFabAuth] Session expired due to heartbeat timeout");
                ForceLogout("Session đã hết hạn");
            }
        }

        // private void CheckSecurityAlerts()
        // {
        //     if (!isLoggedIn) return;
        //     _ = CheckSecurityAlertsAsync();
        // }

        private async Task CheckSecurityAlertsAsync()
        {
            var (ok, payload, _) = await ExecuteCSAsync(CS.GetPendingSecurityAlerts, new Dictionary<string, object>
            {
                ["playerId"] = userId
            });

            if (!ok || payload == null) return;
            if (!TryGet(payload, "success", out bool succ) || !succ) return;
            if (!TryGet(payload, "alerts", out JArray alerts) || alerts == null || alerts.Count == 0) return;

            var first = alerts.First as JObject;
            if (first == null) return;

            var alertInfoObj = first["alertInfo"] as JObject;
            if (alertInfoObj == null) return;

            var info = new SecurityAlertInfo
            {
                AttemptDeviceInfo = alertInfoObj.Value<string>("AttemptDeviceInfo") ?? "Unknown",
                AttemptLocation = alertInfoObj.Value<string>("AttemptLocation") ?? "Unknown",
                AttemptIP = alertInfoObj.Value<string>("AttemptIP") ?? "Unknown",
                AttemptTime = DateTime.TryParse(alertInfoObj.Value<string>("AttemptTime"), out var t)
                    ? t
                    : DateTime.UtcNow
            };

            OnSecurityAlert?.Invoke(info);
            MarkSecurityAlertAsRead();
        }

        private void MarkSecurityAlertAsRead()
        {
            _ = ExecuteCSAsync(CS.MarkSecurityAlertRead, new Dictionary<string, object> { ["playerId"] = userId });
        }

        #endregion

        #region Register / Forgot / Logout

        public async Task<(bool success, string message)> RegisterAsync(string username, string email, string password,
            string confirmPassword)
        {
            if (!ValidateRegisterInput(username, email, password, confirmPassword, out var error))
                return (false, error);

            var tcs = new TaskCompletionSource<(bool, string)>();
            var req = new RegisterPlayFabUserRequest
            {
                Email = email,
                Password = password,
                Username = username,
                RequireBothUsernameAndEmail = true
            };

            PlayFabClientAPI.RegisterPlayFabUser(req,
                _ => tcs.TrySetResult((true, "Registration successful! You can now log in.")),
                e => tcs.TrySetResult((false, GetRegisterErrorMessage(e)))
            );

            return await tcs.Task;
        }

        public async Task<(bool success, string message)> ForgotPasswordAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
                return (false, "Invalid email address.");

            var tcs = new TaskCompletionSource<(bool, string)>();
            var req = new SendAccountRecoveryEmailRequest { Email = email, TitleId = PlayFabSettings.TitleId };

            PlayFabClientAPI.SendAccountRecoveryEmail(req,
                _ => tcs.TrySetResult((true, "Password recovery email sent! Please check your inbox.")),
                e => tcs.TrySetResult((false, e.ErrorMessage))
            );

            return await tcs.Task;
        }

        public void Logout(Action onSuccess = null) => _ = LogoutAsync(onSuccess);

        public async Task LogoutAsync(Action onSuccess = null)
        {
            try
            {
                PlayFabClientAPI.ForgetAllCredentials();
                isLoggedIn = false;
                userId = string.Empty;
                onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayFabAuth] Logout error: {ex.Message}");
                onSuccess?.Invoke();
            }
        }
        // public async Task LogoutAsync(Action onSuccess = null)
        // {
        //     try
        //     {
        //         if (enableSessionProtection && isLoggedIn)
        //             await SecureLogoutViaCloudScript();
        //
        //         PlayFabClientAPI.ForgetAllCredentials();
        //         isLoggedIn = false;
        //         userId = string.Empty;
        //         currentSessionId = string.Empty;
        //
        //         StopSessionMaintenance();
        //         onSuccess?.Invoke();
        //     }
        //     catch (Exception ex)
        //     {
        //         Debug.LogError($"[PlayFabAuth] Logout error: {ex.Message}");
        //         onSuccess?.Invoke();
        //     }
        // }

        // private async Task ForceLogoutQuiet()
        // {
        //     try
        //     {
        //         StopSessionMaintenance();
        //         PlayFabClientAPI.ForgetAllCredentials();
        //         await Task.Delay(80);
        //     }
        //     catch (Exception ex)
        //     {
        //         Debug.LogError($"[PlayFabAuth] Force logout quiet error: {ex.Message}");
        //     }
        //     finally
        //     {
        //         isLoggedIn = false;
        //         userId = string.Empty;
        //         currentSessionId = string.Empty;
        //     }
        // }

        private void ForceLogout(string reason)
        {
            Debug.LogWarning($"[PlayFabAuth] Force logout: {reason}");
            //StopSessionMaintenance();
            PlayFabClientAPI.ForgetAllCredentials();
            isLoggedIn = false;
            userId = string.Empty;
            currentSessionId = string.Empty;
            OnSessionExpired?.Invoke();
        }

        #endregion

        #region Helpers / Validation / Errors

        private string GenerateSessionId() =>
            $"{SystemInfo.deviceUniqueIdentifier}_{DateTime.UtcNow.Ticks}_{UnityEngine.Random.Range(1000, 9999)}";

        private string GetDeviceInfo() => $"{SystemInfo.deviceModel} ({SystemInfo.operatingSystem})";
        private string GetClientIP() => "Unknown IP"; // nếu cần, bạn có thể tích hợp service IP
        private string GetClientLocation() => "Unknown Location";

        public async Task<bool> IsAccountOnlineElsewhereAsync(string playerId)
        {
            if (!enableSessionProtection) return false;
            var (ok, payload, _) = await ExecuteCSAsync(CS.CheckUserOnlineStatus, new Dictionary<string, object>
            {
                ["playerId"] = playerId,
                ["userId"] = playerId
            });

            if (!ok || payload == null) return false;
            return TryGet(payload, "success", out bool succ) && succ &&
                   TryGet(payload, "isOnline", out bool online) && online;
        }

        private bool ValidateLoginInput(string userOrEmail, string password, out string error)
        {
            if (string.IsNullOrWhiteSpace(userOrEmail) || string.IsNullOrWhiteSpace(password))
            {
                error = "Please enter both username/email and password.";
                return false;
            }

            error = null;
            return true;
        }

        private bool ValidateRegisterInput(string username, string email, string password, string confirm,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirm))
            {
                error = "Please fill in all fields.";
                return false;
            }

            if (!EmailRegex.IsMatch(email))
            {
                error = "Invalid email address.";
                return false;
            }

            if (password != confirm)
            {
                error = "Passwords do not match.";
                return false;
            }

            if (password.Length < 6)
            {
                error = "Password must be at least 6 characters long.";
                return false;
            }

            error = null;
            return true;
        }

        private string GetLoginErrorMessage(PlayFabError error) => error.Error switch
        {
            PlayFabErrorCode.InvalidUsernameOrPassword => "Username/email or password is incorrect.",
            PlayFabErrorCode.AccountNotFound => "Account does not exist.",
            PlayFabErrorCode.AccountBanned => "Account has been banned.",
            PlayFabErrorCode.InvalidParams => "Invalid login parameters.",
            PlayFabErrorCode.ServiceUnavailable => "Service is temporarily unavailable. Please try again later.",
            PlayFabErrorCode.ConnectionError => "Unable to connect to server. Please check your network connection.",
            _ => $"Login error: {error.ErrorMessage}"
        };

        private string GetRegisterErrorMessage(PlayFabError error) => error.Error switch
        {
            PlayFabErrorCode.UsernameNotAvailable => "Username is already taken.",
            PlayFabErrorCode.EmailAddressNotAvailable => "Email is already registered.",
            PlayFabErrorCode.InvalidParams => "Invalid registration parameters.",
            PlayFabErrorCode.ProfaneDisplayName => "Username contains inappropriate language.",
            PlayFabErrorCode.ServiceUnavailable => "Service is temporarily unavailable. Please try again later.",
            PlayFabErrorCode.ConnectionError => "Unable to connect to server. Please check your network connection.",
            _ => $"Login error: {error.ErrorMessage}"
        };

        #endregion

        #region DTOs

        [Serializable]
        public class BasicLoginResult
        {
            public bool success;
            public string message;
            public string playFabId;
            public string sessionToken;
        }

        [Serializable]
        public class OnlineCheckResult
        {
            public bool success;
            public bool hasOtherOnlineSession;
            public string message;
        }

        [Serializable]
        public class SessionResult
        {
            public bool success;
            public string sessionId;
            public string message;
        }

        #endregion
    }
}
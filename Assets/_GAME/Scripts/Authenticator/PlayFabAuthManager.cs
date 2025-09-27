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

    public class PlayFabAuthManager : SingletonDontDestroy<PlayFabAuthManager>, IAuthManager<PlayFabAuthManager>
    {
        [Header("Session Settings")]
        [SerializeField] private bool  enableSessionProtection            = true;
        [SerializeField] private float loginProcessTimeoutSeconds         = 30f;
        [SerializeField] private float sessionHeartbeatIntervalSeconds    = 45f;
        [SerializeField] private float sessionTimeoutSeconds              = 300f; // 5 phút
        [SerializeField] private float securityAlertCheckIntervalSeconds  = 30f;  // mỗi 30s

        // limits
        private const float MIN_HEARTBEAT_GAP_SECONDS = 30f;
        private const float MIN_LOGIN_RETRY_INTERVAL  = 3f;
        private const int   MAX_SESSION_RETRY         = 2;

        // CloudScript function names (avoid typo)
        private static class CS
        {
            public const string CheckExistingOnlineSessions = "CheckExistingOnlineSessions";
            public const string EstablishNewSession         = "EstablishNewSession";
            public const string UpdateHeartbeat             = "UpdateHeartbeat";          // dùng bản thường (server đã chuẩn hoá)
            public const string SecureLogout                = "SecureLogout";
            public const string GetPendingSecurityAlerts    = "GetPendingSecurityAlerts";
            public const string MarkSecurityAlertRead       = "MarkSecurityAlertRead";
            public const string NotifySecurityAlert         = "NotifySecurityAlert";
            public const string CheckUserOnlineStatus       = "CheckUserOnlineStatus";
        }

        private bool     isLoggedIn;
        private string   userId = string.Empty;
        private string   currentSessionId = string.Empty;
        private bool     isCurrentlyLoggingIn = false;
        private DateTime loginStartTime;
        private DateTime lastHeartbeat;
        private DateTime lastLoginAttempt = DateTime.MinValue;
        private int      loginAttemptCount = 0;
        private int      sessionRetryCount = 0;
        private bool     maintenanceStarted = false;

        private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        // Events
        public event Action<SecurityAlertInfo> OnSecurityAlert;
        public event Action OnSessionExpired;
        public event Action<string> OnLoginSuccess;
        public event Action<string> OnLoginFailed;

        public PlayFabAuthManager Singleton => Instance;
        public bool   IsLoggedIn        => isLoggedIn;
        public string UserId            => userId;
        public string CurrentSessionId  => currentSessionId;
        public bool   IsCurrentlyLoggingIn => isCurrentlyLoggingIn;

        protected override void Awake()
        {
            base.Awake();
            currentSessionId = GenerateSessionId();
        }

        private void Start()
        {
            if (enableSessionProtection && isLoggedIn)
                StartSessionMaintenance();
        }

        private void OnDestroy()
        {
            StopSessionMaintenance();
        }

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
                var msg = $"Vui lòng chờ {wait} giây trước khi thử lại.";
                OnLoginFailed?.Invoke(msg);
                return (false, msg);
            }

            if (loginAttemptCount > 0)
            {
                var backoff = Math.Min(loginAttemptCount * 2, 10);
                if (sinceLast < backoff)
                {
                    var remain = Mathf.CeilToInt(backoff - (float)sinceLast);
                    var msg = $"Quá nhiều lần thử. Vui lòng chờ {remain} giây.";
                    OnLoginFailed?.Invoke(msg);
                    return (false, msg);
                }
            }

            if (isCurrentlyLoggingIn)
            {
                const string msg = "Đang trong quá trình đăng nhập, vui lòng chờ...";
                OnLoginFailed?.Invoke(msg);
                return (false, msg);
            }

            var deadline = DateTime.UtcNow.AddSeconds(loginProcessTimeoutSeconds);

            try
            {
                isCurrentlyLoggingIn = true;
                loginStartTime = DateTime.UtcNow;
                lastLoginAttempt = DateTime.UtcNow;

                // 1) Auth
                var loginResult = await AuthenticateUser(userOrEmail, password);
                if (!loginResult.success)
                {
                    loginAttemptCount++;
                    OnLoginFailed?.Invoke(loginResult.message);
                    return (false, loginResult.message);
                }

                userId = loginResult.playFabId;

                if (DateTime.UtcNow > deadline)
                {
                    await ForceLogoutQuiet();
                    const string msg = "Quy trình đăng nhập quá thời gian cho phép.";
                    OnLoginFailed?.Invoke(msg);
                    return (false, msg);
                }

                await Task.Delay(UnityEngine.Random.Range(250, 600)); // tránh rate-limit

                // 2) Check session khác
                var onlineCheck = await CheckExistingOnlineSession();
                if (!onlineCheck.success)
                {
                    await ForceLogoutQuiet();
                    loginAttemptCount++;
                    const string msg = "Lỗi kiểm tra phiên đăng nhập";
                    OnLoginFailed?.Invoke(msg);
                    return (false, msg);
                }

                if (onlineCheck.hasOtherOnlineSession)
                {
                    _ = SendSecurityAlert(userId, userOrEmail); // fire & forget
                    await ForceLogoutQuiet();
                    loginAttemptCount++;
                    const string msg = "Tài khoản đang được sử dụng từ thiết bị khác. Không thể đăng nhập.";
                    OnLoginFailed?.Invoke(msg);
                    return (false, msg);
                }

                if (DateTime.UtcNow > deadline)
                {
                    await ForceLogoutQuiet();
                    const string msg = "Quy trình đăng nhập quá thời gian cho phép.";
                    OnLoginFailed?.Invoke(msg);
                    return (false, msg);
                }

                await Task.Delay(UnityEngine.Random.Range(200, 450));

                // 3) Thiết lập session mới
                var sessionNew = await EstablishNewSession(userId);
                if (!sessionNew.success)
                {
                    await ForceLogoutQuiet();
                    loginAttemptCount++;
                    const string msg = "Không thể thiết lập phiên đăng nhập";
                    OnLoginFailed?.Invoke(msg);
                    return (false, msg);
                }

                // 4) Local state
                isLoggedIn = true;
                currentSessionId = sessionNew.sessionId;
                lastHeartbeat = DateTime.UtcNow;

                loginAttemptCount = 0;
                sessionRetryCount = 0;

                await Task.Delay(UnityEngine.Random.Range(500, 900)); // cho server sync xong

                if (enableSessionProtection && isLoggedIn)
                    StartSessionMaintenance();

                OnLoginSuccess?.Invoke("Đăng nhập thành công!");
                return (true, "Đăng nhập thành công!");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayFabAuth] Login exception: {ex.Message}");
                await ForceLogoutQuiet();
                loginAttemptCount++;
                const string msg = "Đã xảy ra lỗi trong quá trình đăng nhập. Vui lòng thử lại.";
                OnLoginFailed?.Invoke(msg);
                return (false, msg);
            }
            finally
            {
                isCurrentlyLoggingIn = false;
            }
        }

        private async Task<BasicLoginResult> AuthenticateUser(string userOrEmail, string password)
        {
            var tcs = new TaskCompletionSource<BasicLoginResult>();

            if (userOrEmail.Contains("@"))
            {
                var req = new LoginWithEmailAddressRequest { Email = userOrEmail, Password = password };
                PlayFabClientAPI.LoginWithEmailAddress(req,
                    r => tcs.TrySetResult(new BasicLoginResult { success = true, playFabId = r.PlayFabId, sessionToken = r.SessionTicket, message = "ok" }),
                    e => tcs.TrySetResult(new BasicLoginResult { success = false, message = GetLoginErrorMessage(e) })
                );
            }
            else
            {
                var req = new LoginWithPlayFabRequest { Username = userOrEmail, Password = password };
                PlayFabClientAPI.LoginWithPlayFab(req,
                    r => tcs.TrySetResult(new BasicLoginResult { success = true, playFabId = r.PlayFabId, sessionToken = r.SessionTicket, message = "ok" }),
                    e => tcs.TrySetResult(new BasicLoginResult { success = false, message = GetLoginErrorMessage(e) })
                );
            }

            return await tcs.Task;
        }

        #endregion

        #region CloudScript helpers

        private async Task<(bool success, JObject payload, string error)> ExecuteCSAsync(string functionName, object param)
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
            try { value = token.ToObject<T>(); return true; } catch { return false; }
        }

        #endregion

        #region Server Calls

        private async Task<OnlineCheckResult> CheckExistingOnlineSession()
        {
            var (ok, payload, err) = await ExecuteCSAsync(CS.CheckExistingOnlineSessions, new Dictionary<string, object>
            {
                ["playerId"]  = userId,
                ["deviceId"]  = SystemInfo.deviceUniqueIdentifier,
                ["deviceInfo"]= GetDeviceInfo()
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

        private async Task SendSecurityAlert(string targetPlayFabId, string attemptAccount)
        {
            try
            {
                _ = await ExecuteCSAsync(CS.NotifySecurityAlert, new Dictionary<string, object>
                {
                    ["playerId"]     = targetPlayFabId,
                    ["targetUserId"] = targetPlayFabId,
                    ["attemptAccount"]= attemptAccount,
                    ["alertInfo"]    = new Dictionary<string, object>
                    {
                        ["AttemptDeviceInfo"] = GetDeviceInfo(),
                        ["AttemptLocation"]   = GetClientLocation(),
                        ["AttemptTime"]       = DateTime.UtcNow.ToString("O"),
                        ["AttemptIP"]         = GetClientIP()
                    }
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayFabAuth] SendSecurityAlert error: {e.Message}");
            }
        }

        private async Task<SessionResult> EstablishNewSession(string playFabId)
        {
            var newSessionId = GenerateSessionId();

            var (ok, payload, err) = await ExecuteCSAsync(CS.EstablishNewSession, new Dictionary<string, object>
            {
                ["playerId"]              = playFabId,
                ["sessionId"]             = newSessionId,
                ["deviceId"]              = SystemInfo.deviceUniqueIdentifier,
                ["deviceInfo"]            = GetDeviceInfo(),
                ["sessionTimeoutSeconds"] = sessionTimeoutSeconds
            });

            if (!ok || payload == null)
                return new SessionResult { success = false, message = err ?? "No response" };

            var success = TryGet(payload, "success", out bool s) && s;
            var message = TryGet(payload, "message", out string m) ? m : "ok";
            return new SessionResult { success = success, sessionId = newSessionId, message = message };
        }

        private async Task<bool> SecureLogoutViaCloudScript()
        {
            var (ok, _, __) = await ExecuteCSAsync(CS.SecureLogout, new Dictionary<string, object> { ["playerId"] = userId });
            return ok;
        }

        #endregion

        #region Maintenance

        private void StartSessionMaintenance()
        {
            if (!enableSessionProtection || !isLoggedIn) return;
            if (maintenanceStarted) return;

            maintenanceStarted = true;

            CancelInvoke(nameof(SendHeartbeat));
            InvokeRepeating(nameof(SendHeartbeat), sessionHeartbeatIntervalSeconds, sessionHeartbeatIntervalSeconds);

            CancelInvoke(nameof(CheckSecurityAlerts));
            InvokeRepeating(nameof(CheckSecurityAlerts), 10f, securityAlertCheckIntervalSeconds);
        }

        private void StopSessionMaintenance()
        {
            if (!maintenanceStarted) return;
            maintenanceStarted = false;

            CancelInvoke(nameof(SendHeartbeat));
            CancelInvoke(nameof(CheckSecurityAlerts));
        }

        private void SendHeartbeat()
        {
            if (!isLoggedIn || !enableSessionProtection) return;
            if (string.IsNullOrEmpty(currentSessionId) || string.IsNullOrEmpty(userId)) return;

            var sinceLogin = (DateTime.UtcNow - loginStartTime).TotalSeconds;
            if (sinceLogin < 10) return;

            var sinceBeat = (DateTime.UtcNow - lastHeartbeat).TotalSeconds;
            if (sinceBeat < MIN_HEARTBEAT_GAP_SECONDS) return;

            _ = SendHeartbeatAsync();
        }

        private async Task SendHeartbeatAsync()
        {
            var (ok, payload, err) = await ExecuteCSAsync(CS.UpdateHeartbeat, new Dictionary<string, object>
            {
                ["playerId"] = userId,
                ["sessionId"] = currentSessionId
            });

            if (!ok || payload == null)
            {
                Debug.LogWarning($"[PlayFabAuth] Heartbeat failed: {err ?? "no payload"}");
                CheckSessionExpiration();
                return;
            }

            if (TryGet(payload, "success", out bool succ) && succ)
            {
                lastHeartbeat = DateTime.UtcNow;
                return;
            }

            var errorCode = TryGet(payload, "errorCode", out string ec) ? ec : "UNKNOWN";
            if (errorCode == "INVALID_SESSION")
            {
                _ = HandleInvalidSessionWithRetryAsync();
            }
            else
            {
                HandleInvalidSession();
            }
        }

        private async Task HandleInvalidSessionWithRetryAsync()
        {
            sessionRetryCount++;
            if (sessionRetryCount <= MAX_SESSION_RETRY)
            {
                var re = await EstablishNewSession(userId);
                if (re.success)
                {
                    currentSessionId = re.sessionId;
                    lastHeartbeat = DateTime.UtcNow;
                    sessionRetryCount = 0;
                    return;
                }
            }

            sessionRetryCount = 0;
            HandleInvalidSession();
        }

        private void HandleInvalidSession()
        {
            Debug.LogWarning("[PlayFabAuth] Invalid session detected - forcing logout");
            ForceLogout("Phiên đăng nhập không hợp lệ");
        }

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

        private void CheckSecurityAlerts()
        {
            if (!isLoggedIn) return;
            _ = CheckSecurityAlertsAsync();
        }

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
                AttemptLocation   = alertInfoObj.Value<string>("AttemptLocation")   ?? "Unknown",
                AttemptIP         = alertInfoObj.Value<string>("AttemptIP")         ?? "Unknown",
                AttemptTime       = DateTime.TryParse(alertInfoObj.Value<string>("AttemptTime"), out var t) ? t : DateTime.UtcNow
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

        public async Task<(bool success, string message)> RegisterAsync(string username, string email, string password, string confirmPassword)
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
                _ => tcs.TrySetResult((true, "Đăng ký thành công!")),
                e => tcs.TrySetResult((false, GetRegisterErrorMessage(e)))
            );

            return await tcs.Task;
        }

        public async Task<(bool success, string message)> ForgotPasswordAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
                return (false, "Email không hợp lệ.");

            var tcs = new TaskCompletionSource<(bool, string)>();
            var req = new SendAccountRecoveryEmailRequest { Email = email, TitleId = PlayFabSettings.TitleId };

            PlayFabClientAPI.SendAccountRecoveryEmail(req,
                _ => tcs.TrySetResult((true, "Vui lòng kiểm tra email để khôi phục tài khoản.")),
                e => tcs.TrySetResult((false, e.ErrorMessage))
            );

            return await tcs.Task;
        }

        public void Logout(Action onSuccess = null) => _ = LogoutAsync(onSuccess);

        public async Task LogoutAsync(Action onSuccess = null)
        {
            try
            {
                if (enableSessionProtection && isLoggedIn)
                    await SecureLogoutViaCloudScript();

                PlayFabClientAPI.ForgetAllCredentials();
                isLoggedIn = false;
                userId = string.Empty;
                currentSessionId = string.Empty;

                StopSessionMaintenance();
                onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayFabAuth] Logout error: {ex.Message}");
                onSuccess?.Invoke();
            }
        }

        private async Task ForceLogoutQuiet()
        {
            try
            {
                StopSessionMaintenance();
                PlayFabClientAPI.ForgetAllCredentials();
                await Task.Delay(80);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayFabAuth] Force logout quiet error: {ex.Message}");
            }
            finally
            {
                isLoggedIn = false;
                userId = string.Empty;
                currentSessionId = string.Empty;
            }
        }

        private void ForceLogout(string reason)
        {
            Debug.LogWarning($"[PlayFabAuth] Force logout: {reason}");
            StopSessionMaintenance();
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
        private string GetClientIP() => "Unknown IP";           // nếu cần, bạn có thể tích hợp service IP
        private string GetClientLocation() => "Unknown Location";

        public async Task<bool> IsAccountOnlineElsewhereAsync(string playerId)
        {
            if (!enableSessionProtection) return false;
            var (ok, payload, _) = await ExecuteCSAsync(CS.CheckUserOnlineStatus, new Dictionary<string, object>
            {
                ["playerId"] = playerId,
                ["userId"]   = playerId
            });

            if (!ok || payload == null) return false;
            return TryGet(payload, "success", out bool succ) && succ &&
                   TryGet(payload, "isOnline", out bool online) && online;
        }

        private bool ValidateLoginInput(string userOrEmail, string password, out string error)
        {
            if (string.IsNullOrWhiteSpace(userOrEmail) || string.IsNullOrWhiteSpace(password))
            {
                error = "Vui lòng nhập đầy đủ thông tin đăng nhập.";
                return false;
            }
            error = null;
            return true;
        }

        private bool ValidateRegisterInput(string username, string email, string password, string confirm, out string error)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirm))
            {
                error = "Vui lòng nhập đầy đủ thông tin đăng ký.";
                return false;
            }
            if (!EmailRegex.IsMatch(email))
            {
                error = "Email không hợp lệ.";
                return false;
            }
            if (password != confirm)
            {
                error = "Mật khẩu và xác nhận mật khẩu không khớp.";
                return false;
            }
            if (password.Length < 6)
            {
                error = "Mật khẩu phải có ít nhất 6 ký tự.";
                return false;
            }
            error = null;
            return true;
        }

        private string GetLoginErrorMessage(PlayFabError error) => error.Error switch
        {
            PlayFabErrorCode.InvalidUsernameOrPassword => "Tên người dùng hoặc mật khẩu không đúng.",
            PlayFabErrorCode.AccountNotFound           => "Tài khoản không tồn tại.",
            PlayFabErrorCode.AccountBanned             => "Tài khoản đã bị khóa.",
            PlayFabErrorCode.InvalidParams             => "Thông tin đăng nhập không hợp lệ.",
            PlayFabErrorCode.ServiceUnavailable        => "Dịch vụ tạm thời không khả dụng. Vui lòng thử lại sau.",
            PlayFabErrorCode.ConnectionError           => "Không thể kết nối đến máy chủ. Vui lòng kiểm tra kết nối mạng.",
            _                                          => $"Lỗi đăng nhập: {error.ErrorMessage}"
        };

        private string GetRegisterErrorMessage(PlayFabError error) => error.Error switch
        {
            PlayFabErrorCode.UsernameNotAvailable      => "Tên người dùng đã được sử dụng.",
            PlayFabErrorCode.EmailAddressNotAvailable  => "Email đã được sử dụng.",
            PlayFabErrorCode.InvalidParams             => "Thông tin đăng ký không hợp lệ.",
            PlayFabErrorCode.ProfaneDisplayName        => "Tên người dùng chứa từ ngữ không phù hợp.",
            PlayFabErrorCode.ServiceUnavailable        => "Dịch vụ tạm thời không khả dụng. Vui lòng thử lại sau.",
            PlayFabErrorCode.ConnectionError           => "Không thể kết nối đến máy chủ. Vui lòng kiểm tra kết nối mạng.",
            _                                          => $"Lỗi đăng ký: {error.ErrorMessage}"
        };

        #endregion

        #region DTOs
        [Serializable] public class BasicLoginResult { public bool success; public string message; public string playFabId; public string sessionToken; }
        [Serializable] public class OnlineCheckResult { public bool success; public bool hasOtherOnlineSession; public string message; }
        [Serializable] public class SessionResult     { public bool success; public string sessionId; public string message; }
        #endregion
    }
}

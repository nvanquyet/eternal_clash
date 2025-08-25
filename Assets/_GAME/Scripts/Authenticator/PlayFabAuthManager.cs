using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GAME.Scripts.DesignPattern;
using Newtonsoft.Json;
using PlayFab;
using System.Linq;
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
        [Header("Session Settings")] [SerializeField]
        private bool enableSessionProtection = true;

        [SerializeField] private float loginProcessTimeoutSeconds = 30f;
        [SerializeField] private float sessionHeartbeatIntervalSeconds = 45f;
        [SerializeField] private float sessionTimeoutSeconds = 300f; // 5 phút
        [SerializeField] private float securityAlertCheckIntervalSeconds = 30f; // Check alerts mỗi 30s

        private bool isLoggedIn;
        private string userId = string.Empty;
        private string currentSessionId = string.Empty;
        private bool isCurrentlyLoggingIn = false;
        private DateTime loginStartTime;
        private DateTime lastHeartbeat;
        private DateTime lastLoginAttempt = DateTime.MinValue;
        private int loginAttemptCount = 0;
        private const float MIN_LOGIN_RETRY_INTERVAL = 3f; // 3 giây giữa các lần thử

        private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        // Events
        public event Action<SecurityAlertInfo> OnSecurityAlert; // Player A nhận cảnh báo
        public event Action OnSessionExpired; // Session hết hạn
        public event Action<string> OnLoginSuccess; // Login thành công
        public event Action<string> OnLoginFailed; // Login thất bại

        public PlayFabAuthManager Singleton => Instance;
        public bool IsLoggedIn => isLoggedIn;
        public string UserId => userId;
        public string CurrentSessionId => currentSessionId;
        public bool IsCurrentlyLoggingIn => isCurrentlyLoggingIn;

        protected override void Awake()
        {
            base.Awake();
            currentSessionId = GenerateSessionId();
        }

        private void Start()
        {
            // Bắt đầu heartbeat và security check nếu đã đăng nhập
            if (enableSessionProtection && isLoggedIn)
            {
                StartSessionMaintenance();
            }
        }

        #region Login Then Check Flow

        public async Task<(bool success, string message)> LoginAsync(string userOrEmail, string password)
        {
            if (!ValidateLoginInput(userOrEmail, password, out var validationError))
            {
                OnLoginFailed?.Invoke(validationError);
                return (false, validationError);
            }

            if (isCurrentlyLoggingIn)
            {
                var message = "Đang trong quá trình đăng nhập, vui lòng chờ...";
                OnLoginFailed?.Invoke(message);
                return (false, message);
            }

            // ✅ RATE LIMITING CHECK
            var timeSinceLastAttempt = (DateTime.UtcNow - lastLoginAttempt).TotalSeconds;
            if (timeSinceLastAttempt < MIN_LOGIN_RETRY_INTERVAL)
            {
                var waitTime = Mathf.CeilToInt(MIN_LOGIN_RETRY_INTERVAL - (float)timeSinceLastAttempt);
                var message = $"Vui lòng chờ {waitTime} giây trước khi thử lại.";
                OnLoginFailed?.Invoke(message);
                return (false, message);
            }

            // ✅ EXPONENTIAL BACKOFF for multiple failed attempts
            if (loginAttemptCount > 0)
            {
                var backoffDelay = Math.Min(loginAttemptCount * 2, 10); // Max 10 seconds
                if (timeSinceLastAttempt < backoffDelay)
                {
                    var waitTime = Mathf.CeilToInt(backoffDelay - (float)timeSinceLastAttempt);
                    var message = $"Quá nhiều lần thử. Vui lòng chờ {waitTime} giây.";
                    OnLoginFailed?.Invoke(message);
                    return (false, message);
                }
            }

            try
            {
                isCurrentlyLoggingIn = true;
                loginStartTime = DateTime.UtcNow;
                lastLoginAttempt = DateTime.UtcNow;

                // ✅ BƯỚC 1: ĐĂNG NHẬP ĐỂ CÓ QUYỀN GỌI CLOUD SCRIPT
                Debug.Log("[PlayFabAuth] Authenticating user...");
                var loginResult = await AuthenticateUser(userOrEmail, password);
                if (!loginResult.success)
                {
                    loginAttemptCount++;
                    OnLoginFailed?.Invoke(loginResult.message);
                    return (false, loginResult.message);
                }

                // ✅ BƯỚC 2: SAU KHI ĐĂNG NHẬP, KIỂM TRA XEM CÓ SESSION KHÁC ĐANG ONLINE KHÔNG
                Debug.Log("[PlayFabAuth] Checking for existing online sessions...");

                // Add small delay before CloudScript call to avoid rate limit
                await Task.Delay(500);
                userId = loginResult.playFabId;
                var onlineCheckResult = await CheckExistingOnlineSession();

                if (!onlineCheckResult.success)
                {
                    // Nếu có lỗi khi check, vẫn logout để đảm bảo an toàn
                    await ForceLogoutQuiet();
                    loginAttemptCount++;
                    OnLoginFailed?.Invoke("Lỗi kiểm tra phiên đăng nhập");
                    return (false, "Lỗi kiểm tra phiên đăng nhập");
                }

                if (onlineCheckResult.hasOtherOnlineSession)
                {
                    Debug.LogWarning($"[PlayFabAuth] Found existing online session - blocking login");

                    // ✅ BƯỚC 3A: NẾU CÓ SESSION KHÁC, GỬI CẢNH BÁO VÀ LOGOUT
                    await SendSecurityAlert(loginResult.playFabId, userOrEmail);
                    await ForceLogoutQuiet();

                    loginAttemptCount++;
                    var errorMessage = "Tài khoản đang được sử dụng từ thiết bị khác. Không thể đăng nhập.";
                    OnLoginFailed?.Invoke(errorMessage);
                    return (false, errorMessage);
                }

                // ✅ BƯỚC 3B: NẾU KHÔNG CÓ SESSION KHÁC, SET UP PHIÊN LÀM VIỆC
                Debug.Log("[PlayFabAuth] No conflicting sessions found - establishing new session...");

                // Add small delay before CloudScript call
                await Task.Delay(300);

                var sessionResult = await EstablishNewSession(loginResult.playFabId);
                if (!sessionResult.success)
                {
                    await ForceLogoutQuiet();
                    loginAttemptCount++;
                    OnLoginFailed?.Invoke("Không thể thiết lập phiên đăng nhập");
                    return (false, "Không thể thiết lập phiên đăng nhập");
                }

                // ✅ BƯỚC 4: SET LOCAL STATE
                isLoggedIn = true;
                userId = loginResult.playFabId;
                currentSessionId = sessionResult.sessionId;
                lastHeartbeat = DateTime.UtcNow;

                // Reset login attempt counter on success
                loginAttemptCount = 0;

                // ✅ BƯỚC 5: START SESSION MAINTENANCE
                if (enableSessionProtection)
                {
                    StartSessionMaintenance();
                }

                Debug.Log($"[PlayFabAuth] Login successful for user: {userId}");
                OnLoginSuccess?.Invoke("Đăng nhập thành công!");
                return (true, "Đăng nhập thành công!");
            }
            catch (Exception ex)
            {
                var errorMessage = "Đã xảy ra lỗi trong quá trình đăng nhập. Vui lòng thử lại.";
                Debug.LogError($"[PlayFabAuth] Login exception: {ex.Message}");
                await ForceLogoutQuiet();
                loginAttemptCount++;
                OnLoginFailed?.Invoke(errorMessage);
                return (false, errorMessage);
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
                var request = new LoginWithEmailAddressRequest
                {
                    Email = userOrEmail,
                    Password = password
                };

                PlayFabClientAPI.LoginWithEmailAddress(request,
                    result =>
                    {
                        Debug.Log("[PlayFabAuth] Email authentication successful");
                        tcs.TrySetResult(new BasicLoginResult
                        {
                            success = true,
                            playFabId = result.PlayFabId,
                            sessionToken = result.SessionTicket,
                            message = "Authentication successful"
                        });
                    },
                    error =>
                    {
                        var errorMessage = GetLoginErrorMessage(error);
                        Debug.LogError($"[PlayFabAuth] Email authentication failed: {errorMessage}");
                        tcs.TrySetResult(new BasicLoginResult
                        {
                            success = false,
                            message = errorMessage
                        });
                    }
                );
            }
            else
            {
                var request = new LoginWithPlayFabRequest
                {
                    Username = userOrEmail,
                    Password = password
                };

                PlayFabClientAPI.LoginWithPlayFab(request,
                    result =>
                    {
                        Debug.Log("[PlayFabAuth] Username authentication successful");
                        tcs.TrySetResult(new BasicLoginResult
                        {
                            success = true,
                            playFabId = result.PlayFabId,
                            sessionToken = result.SessionTicket,
                            message = "Authentication successful"
                        });
                    },
                    error =>
                    {
                        var errorMessage = GetLoginErrorMessage(error);
                        Debug.LogError($"[PlayFabAuth] Username authentication failed: {errorMessage}");
                        tcs.TrySetResult(new BasicLoginResult
                        {
                            success = false,
                            message = errorMessage
                        });
                    }
                );
            }

            return await tcs.Task;
        }

        private async Task<OnlineCheckResult> CheckExistingOnlineSession()
        {
            var tcs = new TaskCompletionSource<OnlineCheckResult>();

            var request = new ExecuteCloudScriptRequest
            {
                FunctionName = "CheckExistingOnlineSessions",
                FunctionParameter = new Dictionary<string, object>
                {
                    ["deviceId"] = SystemInfo.deviceUniqueIdentifier,
                    ["deviceInfo"] = GetDeviceInfo(),
                    ["playerId"] = userId // ✅ EXPLICITLY PASS PLAYER ID
                },
                GeneratePlayStreamEvent = false
            };

            PlayFabClientAPI.ExecuteCloudScript(request,
                result =>
                {
                    try
                    {
                        Debug.Log($"[PlayFabAuth] FunctionResult Type: {result.FunctionResult?.GetType().Name}");
                        Debug.Log($"[PlayFabAuth] FunctionResult: {result.FunctionResult}");

                        if (result.FunctionResult != null)
                        {
                            Dictionary<string, object> resultDict = null;

                            // Try direct cast first
                            if (result.FunctionResult is Dictionary<string, object> directDict)
                            {
                                resultDict = directDict;
                                Debug.Log("[PlayFabAuth] Using direct Dictionary cast");
                            }
                            // If that fails, try JSON parsing
                            else if (result.FunctionResult is string jsonString)
                            {
                                resultDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                                Debug.Log("[PlayFabAuth] Parsed from JSON string");
                            }
                            else
                            {
                                // Last resort: convert to string then parse
                                var jsonStr = result.FunctionResult.ToString();
                                resultDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonStr);
                                Debug.Log("[PlayFabAuth] Converted to string then parsed");
                            }

                            if (resultDict != null)
                            {
                                // Log each key-value pair
                                foreach (var kvp in resultDict)
                                {
                                    Debug.Log(
                                        $"[PlayFabAuth] Result Key: {kvp.Key}, Value: {kvp.Value}, Type: {kvp.Value?.GetType().Name}");
                                }

                                var checkResult = new OnlineCheckResult
                                {
                                    success = (bool)resultDict["success"],
                                    hasOtherOnlineSession = resultDict.ContainsKey("hasOtherOnlineSession")
                                        ? (bool)resultDict["hasOtherOnlineSession"]
                                        : false,
                                    message = (string)resultDict["message"]
                                };

                                tcs.TrySetResult(checkResult);
                            }
                            else
                            {
                                Debug.LogError("[PlayFabAuth] Failed to parse result");
                                tcs.TrySetResult(new OnlineCheckResult
                                {
                                    success = false,
                                    message = "Failed to parse response"
                                });
                            }
                        }
                        else
                        {
                            Debug.LogError("[PlayFabAuth] FunctionResult is null");
                            tcs.TrySetResult(new OnlineCheckResult
                            {
                                success = false,
                                message = "No response from server"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PlayFabAuth] Parse error: {ex.Message}");
                        tcs.TrySetResult(new OnlineCheckResult
                        {
                            success = false,
                            message = "Parse error"
                        });
                    }
                },
                error =>
                {
                    Debug.LogError($"[PlayFabAuth] Check online sessions error: {error.ErrorMessage}");
                    tcs.TrySetResult(new OnlineCheckResult
                    {
                        success = false,
                        message = error.ErrorMessage
                    });
                }
            );

            return await tcs.Task;
        }

        private async Task SendSecurityAlert(string targetPlayFabId, string attemptAccount)
        {
            try
            {
                var request = new ExecuteCloudScriptRequest
                {
                    FunctionName = "NotifySecurityAlert",
                    FunctionParameter = new Dictionary<string, object>
                    {
                        ["playerId"] = targetPlayFabId, // ✅ EXPLICITLY PASS PLAYER ID
                        ["targetUserId"] = targetPlayFabId,
                        ["alertInfo"] = new Dictionary<string, object>
                        {
                            ["AttemptDeviceInfo"] = GetDeviceInfo(),
                            ["AttemptLocation"] = GetClientLocation(),
                            ["AttemptTime"] = DateTime.UtcNow.ToString("O"),
                            ["AttemptIP"] = GetClientIP()
                        },
                        ["attemptAccount"] = attemptAccount
                    },
                    GeneratePlayStreamEvent = false
                };

                PlayFabClientAPI.ExecuteCloudScript(request,
                    result => Debug.Log("[PlayFabAuth] Security alert sent successfully"),
                    error => Debug.LogWarning($"[PlayFabAuth] Security alert failed: {error.ErrorMessage}")
                );

                // Không await vì không muốn block login process
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayFabAuth] Send security alert error: {ex.Message}");
            }
        }

        private async Task<SessionResult> EstablishNewSession(string playFabId)
        {
            var tcs = new TaskCompletionSource<SessionResult>();
            var newSessionId = GenerateSessionId();

            var request = new ExecuteCloudScriptRequest
            {
                FunctionName = "EstablishNewSession",
                FunctionParameter = new Dictionary<string, object>
                {
                    ["playerId"] = playFabId, // ✅ EXPLICITLY PASS PLAYER ID
                    ["sessionId"] = newSessionId,
                    ["deviceId"] = SystemInfo.deviceUniqueIdentifier,
                    ["deviceInfo"] = GetDeviceInfo(),
                    ["sessionTimeoutSeconds"] = sessionTimeoutSeconds
                },
                GeneratePlayStreamEvent = false
            };

            PlayFabClientAPI.ExecuteCloudScript(request,
                result =>
                {
                    try
                    {
                        // In log tổng quan
                        Debug.Log(
                            $"[PlayFabAuth] EstablishNewSession raw FunctionResult Type: {result.FunctionResult?.GetType().Name}");
                        Debug.Log($"[PlayFabAuth] EstablishNewSession raw FunctionResult: {result.FunctionResult}");

                        // In log từ server (CloudScript log)
                        if (result.Logs != null)
                        {
                            foreach (var log in result.Logs)
                                Debug.Log($"[PlayFabAuth][CS-Log] {log.Level}: {log.Message}");
                        }

                        if (result.FunctionResult != null)
                        {
                            Dictionary<string, object> resultDict = null;

                            // Thử direct cast
                            if (result.FunctionResult is Dictionary<string, object> directDict)
                            {
                                resultDict = directDict;
                                Debug.Log("[PlayFabAuth] Parsed by direct Dictionary cast");
                            }
                            // Thử parse nếu là string
                            else if (result.FunctionResult is string jsonString)
                            {
                                resultDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                                Debug.Log("[PlayFabAuth] Parsed from JSON string");
                            }
                            else
                            {
                                // Parse fallback từ ToString()
                                var jsonStr = result.FunctionResult.ToString();
                                resultDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonStr);
                                Debug.Log("[PlayFabAuth] Parsed from .ToString() then JSON");
                            }

                            if (resultDict != null)
                            {
                                // Log từng key-value
                                foreach (var kvp in resultDict)
                                {
                                    Debug.Log(
                                        $"[PlayFabAuth] Result Key: {kvp.Key}, Value: {kvp.Value}, Type: {kvp.Value?.GetType().Name}");
                                }

                                var sessionResult = new SessionResult
                                {
                                    success = resultDict.ContainsKey("success") && (bool)resultDict["success"],
                                    sessionId = newSessionId,
                                    message = resultDict.ContainsKey("message")
                                        ? resultDict["message"].ToString()
                                        : "No message"
                                };

                                tcs.TrySetResult(sessionResult);
                            }
                            else
                            {
                                Debug.LogError("[PlayFabAuth] Failed to parse FunctionResult into Dictionary");
                                tcs.TrySetResult(new SessionResult
                                    { success = false, message = "Failed to parse response" });
                            }
                        }
                        else
                        {
                            Debug.LogError("[PlayFabAuth] FunctionResult is null");
                            tcs.TrySetResult(new SessionResult { success = false, message = "No response" });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PlayFabAuth] Establish session parse error: {ex.Message}");
                        tcs.TrySetResult(new SessionResult { success = false, message = "Parse error" });
                    }
                },
                error =>
                {
                    Debug.LogError($"[PlayFabAuth] Establish session error: {error.ErrorMessage}");
                    tcs.TrySetResult(new SessionResult { success = false, message = error.ErrorMessage });
                }
            );

            return await tcs.Task;
        }

        private async Task ForceLogoutQuiet()
        {
            try
            {
                PlayFabClientAPI.ForgetAllCredentials();
                await Task.Delay(100); // Small delay to ensure cleanup
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayFabAuth] Force logout quiet error: {ex.Message}");
            }
        }

        #endregion

        #region Session Management

        private void StartSessionMaintenance()
        {
            if (!enableSessionProtection) return;

            // Start heartbeat
            CancelInvoke(nameof(SendHeartbeat));
            InvokeRepeating(nameof(SendHeartbeat), sessionHeartbeatIntervalSeconds, sessionHeartbeatIntervalSeconds);

            // Start security alert checking
            CancelInvoke(nameof(CheckSecurityAlerts));
            InvokeRepeating(nameof(CheckSecurityAlerts), 10f, securityAlertCheckIntervalSeconds);
        }

        private void StopSessionMaintenance()
        {
            CancelInvoke(nameof(SendHeartbeat));
            CancelInvoke(nameof(CheckSecurityAlerts));
        }

        private void SendHeartbeat()
        {
            if (!isLoggedIn || !enableSessionProtection) return;

            var request = new ExecuteCloudScriptRequest
            {
                FunctionName = "UpdateHeartbeat",
                FunctionParameter = new Dictionary<string, object>
                {
                    ["playerId"] = userId, // ✅ EXPLICITLY PASS PLAYER ID
                    ["sessionId"] = currentSessionId
                },
                GeneratePlayStreamEvent = false
            };

            PlayFabClientAPI.ExecuteCloudScript(request,
                result =>
                {
                    try
                    {
                        if (result.FunctionResult != null)
                        {
                            var resultDict = result.FunctionResult as Dictionary<string, object>;
                            if (resultDict != null && (bool)resultDict["success"])
                            {
                                lastHeartbeat = DateTime.UtcNow;
                                // Debug.Log("[PlayFabAuth] Heartbeat sent successfully");
                            }
                            else
                            {
                                Debug.LogWarning("[PlayFabAuth] Heartbeat failed - invalid session");
                                HandleInvalidSession();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PlayFabAuth] Heartbeat parse error: {ex.Message}");
                    }
                },
                error =>
                {
                    Debug.LogWarning($"[PlayFabAuth] Heartbeat request failed: {error.ErrorMessage}");
                    CheckSessionExpiration();
                }
            );
        }

        private void HandleInvalidSession()
        {
            Debug.LogWarning("[PlayFabAuth] Invalid session detected - forcing logout");
            ForceLogout("Phiên đăng nhập không hợp lệ");
        }

        private void CheckSessionExpiration()
        {
            if (!isLoggedIn) return;

            var timeSinceLastHeartbeat = DateTime.UtcNow - lastHeartbeat;
            if (timeSinceLastHeartbeat.TotalSeconds > sessionTimeoutSeconds)
            {
                Debug.LogWarning("[PlayFabAuth] Session expired due to heartbeat timeout");
                ForceLogout("Session đã hết hạn");
            }
        }

        // Listener cho security alerts - sử dụng Cloud Script
        private void CheckSecurityAlerts()
        {
            if (!isLoggedIn) return;

            var request = new ExecuteCloudScriptRequest
            {
                FunctionName = "GetPendingSecurityAlerts",
                FunctionParameter = new Dictionary<string, object>
                {
                    ["playerId"] = userId // ✅ EXPLICITLY PASS PLAYER ID
                },
                GeneratePlayStreamEvent = false
            };

            PlayFabClientAPI.ExecuteCloudScript(request,
                result =>
                {
                    try
                    {
                        if (result.FunctionResult != null)
                        {
                            var resultDict = result.FunctionResult as Dictionary<string, object>;
                            if (resultDict != null && (bool)resultDict["success"])
                            {
                                var alertsList = resultDict["alerts"] as List<object>;
                                if (alertsList != null && alertsList.Count > 0)
                                {
                                    foreach (var alertObj in alertsList)
                                    {
                                        var alertDict = alertObj as Dictionary<string, object>;
                                        if (alertDict != null)
                                        {
                                            var alertInfo = CreateSecurityAlertFromDict(alertDict);
                                            if (alertInfo != null)
                                            {
                                                // Trigger security alert event
                                                OnSecurityAlert?.Invoke(alertInfo);

                                                // Mark alert as read
                                                MarkSecurityAlertAsRead();
                                                break; // Chỉ process alert đầu tiên
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PlayFabAuth] Check security alerts error: {ex.Message}");
                    }
                },
                error => Debug.LogWarning($"[PlayFabAuth] Check security alerts request failed: {error.ErrorMessage}")
            );
        }

        private SecurityAlertInfo CreateSecurityAlertFromDict(Dictionary<string, object> alertDict)
        {
            try
            {
                var alertInfoDict = alertDict["alertInfo"] as Dictionary<string, object>;
                if (alertInfoDict != null)
                {
                    return new SecurityAlertInfo
                    {
                        AttemptDeviceInfo = alertInfoDict["AttemptDeviceInfo"]?.ToString() ?? "Unknown",
                        AttemptLocation = alertInfoDict["AttemptLocation"]?.ToString() ?? "Unknown",
                        AttemptTime = DateTime.TryParse(alertInfoDict["AttemptTime"]?.ToString(), out var time)
                            ? time
                            : DateTime.Now,
                        AttemptIP = alertInfoDict["AttemptIP"]?.ToString() ?? "Unknown"
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayFabAuth] Create security alert error: {ex.Message}");
            }

            return null;
        }

        private void MarkSecurityAlertAsRead()
        {
            var request = new ExecuteCloudScriptRequest
            {
                FunctionName = "MarkSecurityAlertRead",
                FunctionParameter = new Dictionary<string, object>
                {
                    ["playerId"] = userId // ✅ EXPLICITLY PASS PLAYER ID
                },
                GeneratePlayStreamEvent = false
            };

            PlayFabClientAPI.ExecuteCloudScript(request,
                result => Debug.Log("[PlayFabAuth] Security alert marked as read"),
                error => Debug.LogWarning($"[PlayFabAuth] Mark alert read failed: {error.ErrorMessage}")
            );
        }

        #endregion

        #region Register

        public async Task<(bool success, string message)> RegisterAsync(string username, string email, string password,
            string confirmPassword)
        {
            if (!ValidateRegisterInput(username, email, password, confirmPassword, out var validationError))
                return (false, validationError);

            var tcs = new TaskCompletionSource<(bool, string)>();

            var request = new RegisterPlayFabUserRequest
            {
                Email = email,
                Password = password,
                Username = username,
                RequireBothUsernameAndEmail = true
            };

            PlayFabClientAPI.RegisterPlayFabUser(request,
                result =>
                {
                    Debug.Log($"[PlayFabAuth] Registration successful for user: {username}");
                    tcs.TrySetResult((true, "Đăng ký thành công!"));
                },
                error =>
                {
                    var errorMessage = GetRegisterErrorMessage(error);
                    Debug.LogWarning($"[PlayFabAuth] Registration failed: {errorMessage}");
                    tcs.TrySetResult((false, errorMessage));
                }
            );

            return await tcs.Task;
        }

        #endregion

        #region Forgot Password

        public async Task<(bool success, string message)> ForgotPasswordAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
                return (false, "Email không hợp lệ.");

            var tcs = new TaskCompletionSource<(bool, string)>();

            var request = new SendAccountRecoveryEmailRequest
            {
                Email = email,
                TitleId = PlayFabSettings.TitleId
            };

            PlayFabClientAPI.SendAccountRecoveryEmail(request,
                result =>
                {
                    Debug.Log($"[PlayFabAuth] Password recovery email sent to: {email}");
                    tcs.TrySetResult((true, "Vui lòng kiểm tra email để khôi phục tài khoản."));
                },
                error =>
                {
                    Debug.LogWarning($"[PlayFabAuth] Password recovery failed: {error.ErrorMessage}");
                    tcs.TrySetResult((false, error.ErrorMessage));
                }
            );

            return await tcs.Task;
        }

        #endregion

        #region Logout

        public void Logout(Action onSuccess = null)
        {
            _ = LogoutAsync(onSuccess);
        }

        public async Task LogoutAsync(Action onSuccess = null)
        {
            try
            {
                if (enableSessionProtection && isLoggedIn)
                {
                    await SecureLogoutViaCloudScript();
                }

                // Clear local state
                PlayFabClientAPI.ForgetAllCredentials();
                isLoggedIn = false;
                userId = string.Empty;
                currentSessionId = string.Empty;

                StopSessionMaintenance();

                Debug.Log("[PlayFabAuth] Logout successful");
                onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayFabAuth] Logout error: {ex.Message}");
                onSuccess?.Invoke(); // Vẫn callback để UI biết
            }
        }

        private void ForceLogout(string reason)
        {
            Debug.LogWarning($"[PlayFabAuth] Force logout: {reason}");

            PlayFabClientAPI.ForgetAllCredentials();
            isLoggedIn = false;
            userId = string.Empty;
            currentSessionId = string.Empty;

            StopSessionMaintenance();
            OnSessionExpired?.Invoke();
        }

        private async Task SecureLogoutViaCloudScript()
        {
            var tcs = new TaskCompletionSource<bool>();

            var request = new ExecuteCloudScriptRequest
            {
                FunctionName = "SecureLogout",
                FunctionParameter = new Dictionary<string, object>
                {
                    ["playerId"] = userId // ✅ EXPLICITLY PASS PLAYER ID
                },
                GeneratePlayStreamEvent = false
            };

            PlayFabClientAPI.ExecuteCloudScript(request,
                result => tcs.TrySetResult(true),
                error =>
                {
                    Debug.LogWarning($"[PlayFabAuth] Cloud logout error: {error.ErrorMessage}");
                    tcs.TrySetResult(false);
                }
            );

            await tcs.Task;
        }

        #endregion

        #region Helpers

        private string GenerateSessionId()
        {
            return
                $"{SystemInfo.deviceUniqueIdentifier}_{DateTime.UtcNow.Ticks}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string GetDeviceInfo()
        {
            return $"{SystemInfo.deviceModel} ({SystemInfo.operatingSystem})";
        }

        private string GetClientIP()
        {
            // Implement IP detection logic or return placeholder
            return "Unknown IP";
        }

        private string GetClientLocation()
        {
            // Implement location detection logic or return placeholder
            return "Unknown Location";
        }

        // Phương thức public để check user online status
        public async Task<bool> IsAccountOnlineElsewhereAsync(string userId)
        {
            if (!enableSessionProtection) return false;

            var tcs = new TaskCompletionSource<bool>();

            var request = new ExecuteCloudScriptRequest
            {
                FunctionName = "CheckUserOnlineStatus",
                FunctionParameter = new Dictionary<string, object>
                {
                    ["playerId"] = userId, // ✅ EXPLICITLY PASS PLAYER ID
                    ["userId"] = userId
                },
                GeneratePlayStreamEvent = false
            };

            PlayFabClientAPI.ExecuteCloudScript(request,
                result =>
                {
                    try
                    {
                        if (result.FunctionResult != null)
                        {
                            var resultDict = result.FunctionResult as Dictionary<string, object>;
                            if (resultDict != null && (bool)resultDict["success"])
                            {
                                var isOnline = (bool)resultDict["isOnline"];
                                tcs.TrySetResult(isOnline);
                            }
                            else
                            {
                                tcs.TrySetResult(false);
                            }
                        }
                        else
                        {
                            tcs.TrySetResult(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PlayFabAuth] Check online status parse error: {ex.Message}");
                        tcs.TrySetResult(false);
                    }
                },
                error =>
                {
                    Debug.LogWarning($"[PlayFabAuth] Check online status error: {error.ErrorMessage}");
                    tcs.TrySetResult(false);
                }
            );

            return await tcs.Task;
        }

        #endregion

        #region Validation

        private bool ValidateLoginInput(string userOrEmail, string password, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(userOrEmail) || string.IsNullOrWhiteSpace(password))
            {
                errorMessage = "Vui lòng nhập đầy đủ thông tin đăng nhập.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private bool ValidateRegisterInput(string username, string email, string password, string confirmPassword,
            out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(confirmPassword))
            {
                errorMessage = "Vui lòng nhập đầy đủ thông tin đăng ký.";
                return false;
            }

            if (!EmailRegex.IsMatch(email))
            {
                errorMessage = "Email không hợp lệ.";
                return false;
            }

            if (password != confirmPassword)
            {
                errorMessage = "Mật khẩu và xác nhận mật khẩu không khớp.";
                return false;
            }

            if (password.Length < 6)
            {
                errorMessage = "Mật khẩu phải có ít nhất 6 ký tự.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        #endregion

        #region Error Handling

        private string GetLoginErrorMessage(PlayFabError error)
        {
            return error.Error switch
            {
                PlayFabErrorCode.InvalidUsernameOrPassword => "Tên người dùng hoặc mật khẩu không đúng.",
                PlayFabErrorCode.AccountNotFound => "Tài khoản không tồn tại.",
                PlayFabErrorCode.AccountBanned => "Tài khoản đã bị khóa.",
                PlayFabErrorCode.InvalidParams => "Thông tin đăng nhập không hợp lệ.",
                PlayFabErrorCode.ServiceUnavailable => "Dịch vụ tạm thời không khả dụng. Vui lòng thử lại sau.",
                PlayFabErrorCode.ConnectionError => "Không thể kết nối đến máy chủ. Vui lòng kiểm tra kết nối mạng.",
                _ => $"Lỗi đăng nhập: {error.ErrorMessage}"
            };
        }

        private string GetRegisterErrorMessage(PlayFabError error)
        {
            return error.Error switch
            {
                PlayFabErrorCode.UsernameNotAvailable => "Tên người dùng đã được sử dụng.",
                PlayFabErrorCode.EmailAddressNotAvailable => "Email đã được sử dụng.",
                PlayFabErrorCode.InvalidParams => "Thông tin đăng ký không hợp lệ.",
                PlayFabErrorCode.ProfaneDisplayName => "Tên người dùng chứa từ ngữ không phù hợp.",
                PlayFabErrorCode.ServiceUnavailable => "Dịch vụ tạm thời không khả dụng. Vui lòng thử lại sau.",
                PlayFabErrorCode.ConnectionError => "Không thể kết nối đến máy chủ. Vui lòng kiểm tra kết nối mạng.",
                _ => $"Lỗi đăng ký: {error.ErrorMessage}"
            };
        }

        #endregion

        #region Helper Classes

        [System.Serializable]
        public class BasicLoginResult
        {
            public bool success;
            public string message;
            public string playFabId;
            public string sessionToken;
        }

        [System.Serializable]
        public class OnlineCheckResult
        {
            public bool success;
            public bool hasOtherOnlineSession;
            public string message;
        }

        [System.Serializable]
        public class SessionResult
        {
            public bool success;
            public string sessionId;
            public string message;
        }

        #endregion

        #region Debug & Testing

        [ContextMenu("Test Session Check")]
        private async void TestSessionCheck()
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    Debug.Log("[PlayFabAuth] Not logged in - cannot test session check");
                    return;
                }

                var result = await IsAccountOnlineElsewhereAsync(userId);
                Debug.Log($"[PlayFabAuth] Account online elsewhere: {result}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayFabAuth] Test session check error: {e.Message}");
            }
        }

        [ContextMenu("Simulate Security Alert")]
        private void SimulateSecurityAlert()
        {
            var alertInfo = new SecurityAlertInfo
            {
                AttemptDeviceInfo = "iPhone 12 (iOS 15.0)",
                AttemptTime = DateTime.UtcNow,
                AttemptLocation = "Vietnam",
                AttemptIP = "192.168.1.1"
            };
            OnSecurityAlert?.Invoke(alertInfo);
        }

        [ContextMenu("Force Session Cleanup")]
        private async void ForceSessionCleanup()
        {
            if (isLoggedIn)
            {
                await SecureLogoutViaCloudScript();
                Debug.Log("[PlayFabAuth] Session cleanup completed");
            }
        }

        [ContextMenu("Check Pending Alerts")]
        private void TestCheckPendingAlerts()
        {
            CheckSecurityAlerts();
        }

        #endregion
    }
}
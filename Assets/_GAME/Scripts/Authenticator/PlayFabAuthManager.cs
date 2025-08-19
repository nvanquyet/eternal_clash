using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GAME.Scripts.DesignPattern;
using PlayFab;
using PlayFab.ClientModels;

namespace _GAME.Scripts.Authenticator
{
    public class PlayFabAuthManager : SingletonDontDestroy<PlayFabAuthManager>, IAuthManager<PlayFabAuthManager>
    {
        private bool isLoggedIn;
        private string userId = string.Empty;

        private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        public PlayFabAuthManager Singleton => Instance;
        public bool IsLoggedIn => isLoggedIn;
        public string UserId => userId;

        #region Login
        public async Task<(bool success, string message)> LoginAsync(string userOrEmail, string password)
        {
            if (!ValidateLoginInput(userOrEmail, password, out var validationError))
                return (false, validationError);

            var tcs = new TaskCompletionSource<(bool, string)>();

            bool isEmail = EmailRegex.IsMatch(userOrEmail);

            if (isEmail)
            {
                var request = new LoginWithEmailAddressRequest
                {
                    Email = userOrEmail,
                    Password = password,
                    TitleId = PlayFabSettings.TitleId
                };
                PlayFabClientAPI.LoginWithEmailAddress(request,
                    result => SetLoginSuccess(tcs, result.PlayFabId),
                    error => tcs.TrySetResult((false, GetLoginErrorMessage(error))));
            }
            else
            {
                var request = new LoginWithPlayFabRequest
                {
                    Username = userOrEmail,
                    Password = password,
                    TitleId = PlayFabSettings.TitleId
                };
                PlayFabClientAPI.LoginWithPlayFab(request,
                    result => SetLoginSuccess(tcs, result.PlayFabId),
                    error => tcs.TrySetResult((false, GetLoginErrorMessage(error))));
            }

            return await tcs.Task;
        }
        #endregion

        #region Register
        public async Task<(bool success, string message)> RegisterAsync(string username, string email, string password, string confirmPassword)
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
                result => tcs.TrySetResult((true, "Registration successful!")),
                error => tcs.TrySetResult((false, GetRegisterErrorMessage(error)))
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
                result => tcs.TrySetResult((true, "Please check your email for recovery instructions.")),
                error => tcs.TrySetResult((false, error.ErrorMessage))
            );

            return await tcs.Task;
        }
        #endregion

        #region Logout
        public void Logout(Action onSuccess)
        {
            PlayFabClientAPI.ForgetAllCredentials();
            isLoggedIn = false;
            userId = string.Empty;
            onSuccess?.Invoke();
        }
        #endregion

        #region Helpers
        private void SetLoginSuccess(TaskCompletionSource<(bool, string)> tcs, string id)
        {
            isLoggedIn = true;
            userId = id;
            tcs.TrySetResult((true, userId));
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

        private bool ValidateRegisterInput(string username, string email, string password, string confirmPassword, out string errorMessage)
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
                PlayFabErrorCode.ServiceUnavailable => "Dịch vụ tạm thời không khả dụng. Vui lòng thử lại sau.",
                PlayFabErrorCode.ConnectionError => "Không thể kết nối đến máy chủ.",
                _ => error.ErrorMessage
            };
        }

        private string GetRegisterErrorMessage(PlayFabError error)
        {
            return error.Error switch
            {
                PlayFabErrorCode.UsernameNotAvailable => "Tên người dùng đã được sử dụng.",
                PlayFabErrorCode.EmailAddressNotAvailable => "Email đã được sử dụng.",
                _ => error.ErrorMessage
            };
        }
        #endregion
    }
}

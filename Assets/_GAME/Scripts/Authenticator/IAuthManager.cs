using System;
using System.Threading.Tasks;

namespace _GAME.Scripts.Authenticator
{
    public interface IAuthManager<out T>
    {
        T Singleton { get; }
        Task<(bool success, string message)> LoginAsync(string userOrEmail, string password);
        Task<(bool success, string message)> RegisterAsync(string username, string email, string password, string confirmPassword);
        Task<(bool success, string message)> ForgotPasswordAsync(string email);
        void Logout(Action onSuccess);
        bool IsLoggedIn { get; }
        string UserId { get; }
    }
}
using UnityEngine;

namespace _GAME.Scripts.Data
{
    public static class LocalData
    {
        public static string UserName
        {
            get => PlayerPrefs.GetString("UserName", string.Empty);
            set => PlayerPrefs.SetString("UserName", value);
        }
        
        public static string UserId
        {
            get => PlayerPrefs.GetString("UserId", string.Empty);
            set => PlayerPrefs.SetString("UserId", value);
        }
        
        public static string UserPassword
        {
            get => PlayerPrefs.GetString("UserPassword", string.Empty);
            set => PlayerPrefs.SetString("UserPassword", value);
        }

        public static bool StayLoggedIn
        {
            get => PlayerPrefs.GetInt("StayLoggedIn", 0) == 1;
            set => PlayerPrefs.SetInt("StayLoggedIn", value ? 1 : 0);
        }
    }
}

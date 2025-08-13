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
    }
}

using GAME.Scripts.DesignPattern;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.CameraHnS
{
    public class CameraCustom : Singleton<CameraCustom>
    {
        [SerializeField] private Transform aimTarget;
        
        public Transform AimTarget => aimTarget;
    }
}

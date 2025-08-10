using UnityEngine;

namespace _GAME.Scripts.Player.Config
{
    [CreateAssetMenu(fileName = "PlayerConfig", menuName = "Config/Base/Player Config")]
    public class PlayerConfig : ScriptableObject
    {
        public PlayerLocomotionConfig locomotionConfig;
        public PlayerDashConfig dashConfig;
        
        //public PlayerCameraConfig CameraConfig;
    }
}
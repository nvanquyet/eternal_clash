using Unity.Cinemachine;
using UnityEngine;

namespace _GAME.Scripts.Player
{
    public class MobileCameraControl : MonoBehaviour
    {
        [Header("References")]
        public CinemachineCamera cinemachineCamera;

        public GameObject gamepadObject;
        
        [Header("Settings")]
        public float sensitivityX = 100f;
        public float sensitivityY = 100f;
        public float smoothTime = 0.1f;
        
        private Vector2 lastTouchPosition;
        private bool isTouching = false;
        private CinemachineOrbitalFollow orbitalFollow;
        private CinemachineInputAxisController inputController;
        
        private float currentHorizontal;
        private float currentVertical;
        private float horizontalVelocity;
        private float verticalVelocity;

//         void Start()
//         {
//             // Lấy Orbital Follow component
//             orbitalFollow = cinemachineCamera.GetComponent<CinemachineOrbitalFollow>();
//             inputController = cinemachineCamera.GetComponent<CinemachineInputAxisController>();
//             
// #if UNITY_ANDROID || UNITY_IOS
//             // Tắt Input Axis Controller trên mobile
//             if (inputController != null)
//             {
//                 inputController.enabled = false;
//             }
//             if(gamepadObject) gamepadObject.SetActive(false);
// #endif
//
//             if (orbitalFollow != null)
//             {
//                 currentHorizontal = orbitalFollow.HorizontalAxis.Value;
//                 currentVertical = orbitalFollow.VerticalAxis.Value;
//             }
//         }
//
//         void Update()
//         {
// #if UNITY_ANDROID || UNITY_IOS
//             HandleTouchInput();
// #else
//             // PC vẫn dùng Input Controller
//             if (inputController != null && !inputController.enabled)
//             {
//                 inputController.enabled = true;
//             }
// #endif
//         }
//
//         void HandleTouchInput()
//         {
//             if (orbitalFollow == null) return;
//
//             if (Input.touchCount > 0)
//             {
//                 Touch touch = Input.GetTouch(0);
//
//                 switch (touch.phase)
//                 {
//                     case TouchPhase.Began:
//                         lastTouchPosition = touch.position;
//                         isTouching = true;
//                         break;
//                         
//                     case TouchPhase.Moved when isTouching:
//                         Vector2 delta = touch.deltaPosition;
//                         
//                         // Cập nhật giá trị target
//                         currentHorizontal += delta.x * sensitivityX * Time.deltaTime;
//                         currentVertical -= delta.y * sensitivityY * Time.deltaTime;
//                         
//                         // Clamp vertical (nếu cần)
//                         currentVertical = Mathf.Clamp(currentVertical, 
//                             orbitalFollow.VerticalAxis.Range.x, 
//                             orbitalFollow.VerticalAxis.Range.y);
//                         
//                         lastTouchPosition = touch.position;
//                         break;
//                         
//                     case TouchPhase.Ended:
//                     case TouchPhase.Canceled:
//                         isTouching = false;
//                         break;
//                 }
//             }
//
//             // Smooth update axes
//             orbitalFollow.HorizontalAxis.Value = Mathf.SmoothDamp(
//                 orbitalFollow.HorizontalAxis.Value,
//                 currentHorizontal,
//                 ref horizontalVelocity,
//                 smoothTime
//             );
//             
//             orbitalFollow.VerticalAxis.Value = Mathf.SmoothDamp(
//                 orbitalFollow.VerticalAxis.Value,
//                 currentVertical,
//                 ref verticalVelocity,
//                 smoothTime
//             );
//         }
    }
}
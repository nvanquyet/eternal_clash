using _GAME.Scripts.Player.Config;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Player
{
    public class PlayerController : NetworkBehaviour
    {
        [SerializeField] private PlayerConfig playerConfig;

        [SerializeField] private CharacterController characterController;

        [SerializeField] private Animator animator;
        
        [SerializeField] private GameObject fppCamera;
        [SerializeField] private GameObject tppCamera;

        private PlayerLocomotion _playerLocomotion;
        private PlayerDash _playerDash;


#if UNITY_EDITOR
        /// <summary>
        /// Ensures required components are assigned in the editor.
        /// </summary>
        private void OnValidate()
        {
            characterController ??= GetComponentInChildren<CharacterController>();
            animator ??= GetComponentInChildren<Animator>();
        }
#endif


        /// <summary>
        /// Initializes the locomotion system on start.
        /// </summary>
        private void Start()
        {
            if (!IsOwner) ForceDeactivateCameras();
            else
            {
                ActiveTppCamera(); 
                _playerLocomotion =
                    new PlayerLocomotion(playerConfig.locomotionConfig, characterController, animator);
                _playerDash = new PlayerDash(playerConfig.dashConfig, characterController);
            }
        }

        /// <summary>
        /// Updates the current locomotion state every frame.
        /// </summary>
        private void Update()
        {
            if(!IsOwner) return;
            _playerLocomotion?.OnUpdate();
            _playerDash?.OnUpdate();
        }

        /// <summary>
        ///  Handles fixed updates for physics calculations.
        /// </summary>
        private void FixedUpdate()
        {
            if(!IsOwner) return;
            _playerLocomotion?.OnFixedUpdate();
        }

        /// <summary>
        /// Handles late updates for final adjustments.
        /// </summary>
        private void LateUpdate()
        {
            if(!IsOwner) return;
            _playerLocomotion?.OnLateUpdate();
        }
    
        
        //ActiveCam
        private void ActiveTppCamera()
        {
            if (!IsOwner || tppCamera == null) return;
            tppCamera.SetActive(true);
            fppCamera.SetActive(false);
        }

        private void ActiveFppCamera()
        {
            if (!IsOwner || fppCamera == null) return;
            fppCamera.SetActive(true);
            tppCamera.SetActive(false);
        }
        
        private void ForceDeactivateCameras()
        {
            if (tppCamera != null) tppCamera.SetActive(false);
            if (fppCamera != null) fppCamera.SetActive(false);
        }
    }
}

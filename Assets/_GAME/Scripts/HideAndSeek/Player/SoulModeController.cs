using _GAME.Scripts.Player;
using _GAME.Scripts.UI;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.HideAndSeek.Player
{
    /// <summary>
    /// Handles Soul Mode functionality for Hider players
    /// Local only - no network synchronization needed
    /// </summary>
    public class SoulModeController : MonoBehaviour
    {
        [Header("Soul Mode Settings")]
        [SerializeField] private float maxSoulEnergy = 100f;
        [SerializeField] private float energyDrainRate = 2f; // energy per second
        [SerializeField] private float energyRegenRate = 1f; // energy per second
        [SerializeField] private float minEnergyToActivate = 10f;
        
        [Header("Camera Settings")]
        [SerializeField] private GameObject soulCamera;
        [SerializeField] private float soulCameraSpeed = 5f;
        
        
        [Header("UI References")]
        [SerializeField] private GameObject uiPanel; 
        [SerializeField] private Slider energySlider; 
        [SerializeField] private ButtonEvents forwardButton;
        [SerializeField] private ButtonEvents backwardButton;
        [SerializeField] private ButtonEvents upButton;
        [SerializeField] private ButtonEvents downButton;
        
        // Events
        //public Action<bool> OnSoulModeToggled;
        
        // Input direction from buttons - với giới hạn
        private Vector3 inputDirection = Vector3.zero;
        private bool hasMovementInput = false;
        
        
        
        private PlayerController player;
        public PlayerController Player
        {
            get
            {
                if (player == null)
                    player = GetComponentInParent<PlayerController>();
                return player;
            }
        }
        
        // State
        private bool isInSoulMode = false;
        private float currentSoulEnergy;
        private bool isDrainingEnergy = false;
        private bool isRegeneratingEnergy = false;
        
        #region Unity Lifecycle
        
        private void Start()
        {
            currentSoulEnergy = maxSoulEnergy;
            InitializeButtons();
            if(soulCamera && soulCamera.activeSelf) soulCamera.SetActive(false);
        }

        private void InitializeButtons()
        {
            // Initialize button event handlers
            if (forwardButton != null)
            {
                forwardButton.OnPointerDownEvent += () => AddInputDirection(Vector3.forward);
                forwardButton.OnPointerUpEvent += () => RemoveInputDirection(Vector3.forward);
            }
            
            if (backwardButton != null)
            {
                backwardButton.OnPointerDownEvent += () => AddInputDirection(Vector3.back);
                backwardButton.OnPointerUpEvent += () => RemoveInputDirection(Vector3.back);
            }
            
            if (upButton != null)
            {
                upButton.OnPointerDownEvent += () => AddInputDirection(Vector3.up);
                upButton.OnPointerUpEvent += () => RemoveInputDirection(Vector3.up);
            }
            
            if (downButton != null)
            {
                downButton.OnPointerDownEvent += () => AddInputDirection(Vector3.down);
                downButton.OnPointerUpEvent += () => RemoveInputDirection(Vector3.down);
            }
        }

        private void Update()
        {
            if (isInSoulMode && hasMovementInput)
            {
                HandleSoulCameraMovement();
            }
        }
        
        private void OnDestroy()
        {
            // Clean up button events properly
            CleanupButtonEvents();
            
            // Cancel any running invokes
            CancelInvoke();
        }
        
        private void CleanupButtonEvents()
        {
            // Note: Proper cleanup would require storing the Action references
            // For now, just set to null if the buttons still exist
            if (forwardButton != null)
            {
                forwardButton.OnPointerDownEvent -= () => AddInputDirection(Vector3.forward);
                forwardButton.OnPointerUpEvent -= () => RemoveInputDirection(Vector3.forward);
            }
            
            if (backwardButton != null)
            {
                backwardButton.OnPointerDownEvent -= () => AddInputDirection(Vector3.back);
                backwardButton.OnPointerUpEvent -= () => RemoveInputDirection(Vector3.back);
            }
            
            if (upButton != null)
            {
                upButton.OnPointerDownEvent -= () => AddInputDirection(Vector3.up);
                upButton.OnPointerUpEvent -= () => RemoveInputDirection(Vector3.up);
            }
            
            if (downButton != null)
            {
                downButton.OnPointerDownEvent -= () => AddInputDirection(Vector3.down);
                downButton.OnPointerUpEvent -= () => RemoveInputDirection(Vector3.down);
            }
        }
        
        #endregion
        
        #region Input Handling
        
        private void AddInputDirection(Vector3 direction)
        {
            inputDirection += direction;
            // Clamp input direction để tránh tích lũy
            inputDirection = Vector3.ClampMagnitude(inputDirection, 1f);
            hasMovementInput = inputDirection.magnitude > 0.1f;
        } 
        
        private void RemoveInputDirection(Vector3 direction)
        {
            inputDirection -= direction;
            // Clamp về 0 nếu quá nhỏ để tránh floating point errors
            if (inputDirection.magnitude < 0.1f)
                inputDirection = Vector3.zero;
            
            hasMovementInput = inputDirection.magnitude > 0.1f;
        }
        
        #endregion
        
        #region Public Methods
        
        public bool CanToggleSoulMode()
        {
            if (isInSoulMode)
                return true; // Can always turn off
            
            return currentSoulEnergy >= minEnergyToActivate;
        }
        
        public bool ToggleSoulMode()
        {
            if (!CanToggleSoulMode() && !isInSoulMode)
            {
                Debug.LogWarning("[SoulMode] Not enough energy to activate Soul Mode");
                return false;
            }
            
            isInSoulMode = !isInSoulMode;
            if (isInSoulMode)
            {
                EnterSoulMode();
            }
            else
            {
                ExitSoulMode();
            }
            
            //OnSoulModeToggled?.Invoke(isInSoulMode);
            return true;
        }

        private float GetSoulEnergyPercentage()
        {
            return maxSoulEnergy > 0 ? currentSoulEnergy / maxSoulEnergy : 0f;
        }
        public bool IsInSoulMode => isInSoulMode;
        
        #endregion
        
        #region Soul Mode Management
        
        private void EnterSoulMode()
        {
            Debug.Log("[SoulMode] Entering Soul Mode");
            
            // Disable player movement
            Player?.EnableSoulMode(true);
            
            // Enable soul camera
            if (soulCamera != null)
            {
                soulCamera.gameObject.SetActive(true);
            }
            
            // Fixed: Set local position correctly
            // Assuming you want to move object backward when in soul mode
            transform.localPosition = new Vector3(0, 0, -1f);
            
            // Start energy drain using Invoke
            isDrainingEnergy = true;
            isRegeneratingEnergy = false;
            CancelInvoke(nameof(RegenerateEnergyStep));
            InvokeRepeating(nameof(DrainEnergyStep), 0f, 1f);
        }
        
        private void ExitSoulMode()
        {
            Debug.Log("[SoulMode] Exiting Soul Mode");
            
            // Re-enable player movement
            Player?.EnableSoulMode(false);
            
            // Disable soul camera
            if (soulCamera != null)
            {
                soulCamera.gameObject.SetActive(false);
            }
            
            // Fixed: Reset local position correctly  
            transform.localPosition = Vector3.zero;
            
            // Reset input
            inputDirection = Vector3.zero;
            hasMovementInput = false;
            
            // Stop energy drain and start regeneration
            isDrainingEnergy = false;
            CancelInvoke(nameof(DrainEnergyStep));
            
            // Start energy regeneration
            if (!isRegeneratingEnergy && currentSoulEnergy < maxSoulEnergy)
            {
                isRegeneratingEnergy = true;
                InvokeRepeating(nameof(RegenerateEnergyStep), 1f, 1f);
            }
        }
        
        #endregion
        
        #region Camera Movement
        
        private void HandleSoulCameraMovement()
        {
            if (soulCamera == null || inputDirection.magnitude < 0.1f) return;
            
            // Calculate movement direction relative to camera
            Vector3 cameraForward = soulCamera.transform.forward;
            Vector3 cameraUp = soulCamera.transform.up;
            
            var input = inputDirection.normalized;
            Vector3 moveDirection = (cameraForward * input.z + cameraUp * input.y).normalized;
            
            // Move soul camera by lerp for smooth transition 
            this.transform.position += moveDirection * soulCameraSpeed * Time.deltaTime;
        }
        
        #endregion
        
        #region Energy Management
        
        private void DrainEnergyStep()
        {
            if (!isDrainingEnergy || !isInSoulMode) 
            {
                CancelInvoke(nameof(DrainEnergyStep));
                return;
            }
            
            currentSoulEnergy -= energyDrainRate;
            currentSoulEnergy = Mathf.Max(0f, currentSoulEnergy);
            
            var percentage = Mathf.Clamp01(GetSoulEnergyPercentage());
            energySlider.DOValue(percentage, 1f);
            
            // Auto-exit when energy is depleted
            if (currentSoulEnergy <= 0f)
            {
                ToggleSoulMode();
            }
        }
        
        private void RegenerateEnergyStep()
        {
            // Stop if entered soul mode
            if (isInSoulMode)
            {
                isRegeneratingEnergy = false;
                CancelInvoke(nameof(RegenerateEnergyStep));
                return;
            }
            
            currentSoulEnergy += energyRegenRate;
            currentSoulEnergy = Mathf.Min(maxSoulEnergy, currentSoulEnergy);
            
            var percentage = Mathf.Clamp01(GetSoulEnergyPercentage());
            energySlider.DOValue(percentage, 1f);
            
            // Stop regeneration when full
            if (currentSoulEnergy >= maxSoulEnergy)
            {
                isRegeneratingEnergy = false;
                CancelInvoke(nameof(RegenerateEnergyStep));
            }
        }
        
        #endregion
        
        #region Debug
        
        [ContextMenu("Toggle Soul Mode")]
        private void DebugToggleSoulMode()
        {
            ToggleSoulMode();
        }
        
        [ContextMenu("Refill Energy")]
        private void DebugRefillEnergy()
        {
            currentSoulEnergy = maxSoulEnergy;
        }
        
        [ContextMenu("Drain Energy")]
        private void DebugDrainEnergy()
        {
            currentSoulEnergy = 0f;
        }
        
        #endregion


        public void Clear()
        {
            // Clear state
            if (isInSoulMode)
            {
                ExitSoulMode();
                isInSoulMode = false;
                //OnSoulModeToggled?.Invoke(isInSoulMode);
            }
            currentSoulEnergy = maxSoulEnergy;
            
            // Clean up button events properly
            CleanupButtonEvents();
            
            // Cancel any running invokes
            CancelInvoke();
        }
        
        #region Validation
        
        private void OnValidate()
        {
            // Ensure valid values in inspector
            maxSoulEnergy = Mathf.Max(1f, maxSoulEnergy);
            energyDrainRate = Mathf.Max(0.1f, energyDrainRate);
            energyRegenRate = Mathf.Max(0.1f, energyRegenRate);
            minEnergyToActivate = Mathf.Clamp(minEnergyToActivate, 0f, maxSoulEnergy);
            soulCameraSpeed = Mathf.Max(0.1f, soulCameraSpeed);
        }
        
        #endregion
    }
}
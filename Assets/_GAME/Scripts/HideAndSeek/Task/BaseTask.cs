using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using HideAndSeekGame.Core;
using HideAndSeekGame.Managers;
using _GAME.Scripts.DesignPattern.Interaction;

namespace HideAndSeekGame.Tasks
{
    #region Base Task Class
    
    public abstract class BaseTask : ATriggerInteractable, IGameTask
    {
        [Header("Task Settings")]
        [SerializeField] protected int taskId;
        [SerializeField] protected TaskType taskType;
        [SerializeField] protected float completionTime = 3f;
        [SerializeField] protected bool isCompleted = false;
        
        [Header("UI")]
        [SerializeField] protected Canvas taskUI;
        [SerializeField] protected Slider progressSlider;
        [SerializeField] protected GameObject completedIndicator;
        
        // Network variables
        protected NetworkVariable<bool> networkCompleted = new NetworkVariable<bool>(false);
        protected NetworkVariable<float> networkProgress = new NetworkVariable<float>(0f);
        
        protected IHider currentHider;
        protected Coroutine taskCoroutine;
        
        // IGameTask implementation
        public int TaskId => taskId;
        public TaskType Type => taskType;
        public bool IsCompleted => networkCompleted.Value;
        public override Vector3 Position => transform.position;
        
        public static event Action<int, TaskType> OnTaskStarted;
        public static event Action<int, TaskType> OnTaskCompleted;
        public static event Action<int, float> OnTaskProgressUpdated;
        
        protected override void Awake()
        {
            base.Awake();
            
            if (taskUI != null)
                taskUI.gameObject.SetActive(false);
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            networkCompleted.OnValueChanged += OnCompletedChanged;
            networkProgress.OnValueChanged += OnProgressChanged;
            
            if (IsServer)
            {
                taskId = GetInstanceID();
            }
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            
            networkCompleted.OnValueChanged -= OnCompletedChanged;
            networkProgress.OnValueChanged -= OnProgressChanged;
        }
        
        public virtual void StartTask(IHider hider)
        {
            if (IsCompleted || currentHider != null) return;
            
            currentHider = hider;
            
            if (IsServer)
            {
                StartTaskServerRpc(hider.ClientId);
            }
        }
        
        public virtual void CompleteTask()
        {
            if (IsCompleted) return;
            
            if (IsServer)
            {
                networkCompleted.Value = true;
                networkProgress.Value = 1f;
                
                CompleteTaskClientRpc();
                OnTaskCompleted?.Invoke(taskId, taskType);
            }
            
            if (currentHider != null)
            {
                currentHider.CompleteTask(taskId);
                currentHider.OnTaskCompleted(taskId);
            }
        }
        
        public abstract void OnTaskInteraction(IHider hider);
        
        #region Server RPCs
        
        [ServerRpc(RequireOwnership = false)]
        protected void StartTaskServerRpc(ulong hiderId)
        {
            if (IsCompleted) return;
            
            StartTaskClientRpc(hiderId);
            OnTaskStarted?.Invoke(taskId, taskType);
        }
        
        [ServerRpc(RequireOwnership = false)]
        protected void UpdateProgressServerRpc(float progress)
        {
            if (IsCompleted) return;
            
            networkProgress.Value = Mathf.Clamp01(progress);
            
            if (progress >= 1f)
            {
                CompleteTask();
            }
        }
        
        [ServerRpc(RequireOwnership = false)]
        protected void CancelTaskServerRpc()
        {
            if (IsCompleted) return;
            
            networkProgress.Value = 0f;
            CancelTaskClientRpc();
        }
        
        #endregion
        
        #region Client RPCs
        
        [ClientRpc]
        protected virtual void StartTaskClientRpc(ulong hiderId)
        {
            if (taskUI != null)
                taskUI.gameObject.SetActive(true);
                
            StartTaskUI();
        }
        
        [ClientRpc]
        protected virtual void CompleteTaskClientRpc()
        {
            if (taskUI != null)
                taskUI.gameObject.SetActive(false);
                
            if (completedIndicator != null)
                completedIndicator.SetActive(true);
                
            CompleteTaskUI();
        }
        
        [ClientRpc]
        protected virtual void CancelTaskClientRpc()
        {
            if (taskUI != null)
                taskUI.gameObject.SetActive(false);
                
            CancelTaskUI();
            currentHider = null;
        }
        
        #endregion
        
        #region UI Methods
        
        protected abstract void StartTaskUI();
        protected abstract void CompleteTaskUI();
        protected abstract void CancelTaskUI();
        
        #endregion
        
        #region Event Handlers
        
        private void OnCompletedChanged(bool previousValue, bool newValue)
        {
            isCompleted = newValue;
            
            if (newValue && completedIndicator != null)
                completedIndicator.SetActive(true);
        }
        
        private void OnProgressChanged(float previousValue, float newValue)
        {
            if (progressSlider != null)
                progressSlider.value = newValue;
                
            OnTaskProgressUpdated?.Invoke(taskId, newValue);
        }
        
        #endregion
        
        #region Trigger System Integration
        
        protected override void OnInteractorEntered(IInteractable interactor)
        {
            if (interactor is IHider hider && !IsCompleted)
            {
                // Show interaction prompt
                ShowInteractionPrompt(true);
            }
        }
        
        protected override void OnInteractorExited(IInteractable interactor)
        {
            if (interactor is IHider hider)
            {
                // Hide interaction prompt
                ShowInteractionPrompt(false);
                
                // Cancel task if in progress
                if (currentHider == hider)
                {
                    if (IsServer)
                        CancelTaskServerRpc();
                }
            }
        }
        
        public override bool Interact(IInteractable target)
        {
            if (target is IHider hider && !IsCompleted)
            {
                OnTaskInteraction(hider);
                return true;
            }
            return false;
        }
        
        public override void OnInteracted(IInteractable initiator)
        {
            if (initiator is IHider hider && !IsCompleted)
            {
                StartTask(hider);
            }
        }
        
        private void ShowInteractionPrompt(bool show)
        {
            // Implement interaction prompt UI
            // This would show/hide an interaction hint like "Press E to start task"
        }
        
        #endregion
    }
    
    #endregion
    
    #region Shape Sort Task
    
    public class ShapeSortTask : BaseTask
    {
        [Header("Shape Sort Settings")]
        [SerializeField] private Transform[] shapeSlots;
        [SerializeField] private GameObject[] shapePrefabs;
        [SerializeField] private int targetShapeCount = 5;
        
        [Header("Shape Sort UI")]
        [SerializeField] private Transform shapeContainer;
        [SerializeField] private Button[] shapeButtons;
        [SerializeField] private Image[] slotImages;
        
        private int[] targetSequence;
        private int[] currentSequence;
        private int currentIndex = 0;
        
        protected override void Awake()
        {
            base.Awake();
            GenerateRandomSequence();
            SetupUI();
        }
        
        private void GenerateRandomSequence()
        {
            targetSequence = new int[targetShapeCount];
            currentSequence = new int[targetShapeCount];
            
            for (int i = 0; i < targetShapeCount; i++)
            {
                targetSequence[i] = UnityEngine.Random.Range(0, shapePrefabs.Length);
                currentSequence[i] = -1;
            }
        }
        
        private void SetupUI()
        {
            if (shapeButtons != null)
            {
                for (int i = 0; i < shapeButtons.Length; i++)
                {
                    int shapeIndex = i;
                    shapeButtons[i].onClick.AddListener(() => OnShapeButtonClicked(shapeIndex));
                }
            }
        }
        
        public override void OnTaskInteraction(IHider hider)
        {
            StartTask(hider);
        }
        
        private void OnShapeButtonClicked(int shapeIndex)
        {
            if (IsCompleted || currentIndex >= targetShapeCount) return;
            
            // Check if correct shape
            if (shapeIndex == targetSequence[currentIndex])
            {
                currentSequence[currentIndex] = shapeIndex;
                currentIndex++;
                
                // Update UI
                UpdateShapeUI();
                
                // Update progress
                float progress = (float)currentIndex / targetShapeCount;
                if (IsServer)
                    UpdateProgressServerRpc(progress);
                
                // Check completion
                if (currentIndex >= targetShapeCount)
                {
                    CompleteTask();
                }
            }
            else
            {
                // Wrong shape, reset
                ResetSequence();
            }
        }
        
        private void ResetSequence()
        {
            currentIndex = 0;
            for (int i = 0; i < currentSequence.Length; i++)
            {
                currentSequence[i] = -1;
            }
            UpdateShapeUI();
            
            if (IsServer)
                UpdateProgressServerRpc(0f);
        }
        
        private void UpdateShapeUI()
        {
            if (slotImages == null) return;
            
            for (int i = 0; i < slotImages.Length && i < targetShapeCount; i++)
            {
                if (i < currentIndex)
                {
                    // Show completed shape
                    slotImages[i].sprite = GetShapeSprite(currentSequence[i]);
                    slotImages[i].color = Color.green;
                }
                else if (i == currentIndex)
                {
                    // Show target shape
                    slotImages[i].sprite = GetShapeSprite(targetSequence[i]);
                    slotImages[i].color = Color.yellow;
                }
                else
                {
                    // Show empty slot
                    slotImages[i].sprite = null;
                    slotImages[i].color = Color.gray;
                }
            }
        }
        
        private Sprite GetShapeSprite(int shapeIndex)
        {
            if (shapeIndex >= 0 && shapeIndex < shapePrefabs.Length)
            {
                var spriteRenderer = shapePrefabs[shapeIndex].GetComponent<SpriteRenderer>();
                return spriteRenderer?.sprite;
            }
            return null;
        }
        
        protected override void StartTaskUI()
        {
            UpdateShapeUI();
        }
        
        protected override void CompleteTaskUI()
        {
            // Show completion effects
        }
        
        protected override void CancelTaskUI()
        {
            ResetSequence();
        }
    }
    
    #endregion
    
    #region Bar Slider Task
    
    public class BarSliderTask : BaseTask
    {
        [Header("Bar Slider Settings")]
        [SerializeField] private float targetValue = 0.5f;
        [SerializeField] private float tolerance = 0.05f;
        [SerializeField] private float holdTime = 2f;
        
        [Header("Bar Slider UI")]
        [SerializeField] private Slider valueSlider;
        [SerializeField] private RectTransform targetIndicator;
        [SerializeField] private Image sliderFill;
        
        private float currentValue = 0f;
        private float holdTimer = 0f;
        private bool isHolding = false;
        
        protected override void Awake()
        {
            base.Awake();
            
            targetValue = UnityEngine.Random.Range(0.2f, 0.8f);
            SetupSliderUI();
        }
        
        private void SetupSliderUI()
        {
            if (valueSlider != null)
            {
                valueSlider.value = 0f;
                valueSlider.onValueChanged.AddListener(OnSliderValueChanged);
            }
            
            if (targetIndicator != null)
            {
                // Position target indicator
                var rectTransform = targetIndicator.GetComponent<RectTransform>();
                var sliderRect = valueSlider.GetComponent<RectTransform>();
                
                float targetPosition = targetValue * sliderRect.rect.width;
                rectTransform.anchoredPosition = new Vector2(targetPosition, 0);
            }
        }
        
        public override void OnTaskInteraction(IHider hider)
        {
            StartTask(hider);
        }
        
        private void Update()
        {
            if (!IsOwner || IsCompleted || currentHider == null) return;
            
            // Handle input for slider control
            if (Input.GetKey(KeyCode.Space))
            {
                currentValue = Mathf.MoveTowards(currentValue, 1f, Time.deltaTime * 2f);
            }
            else
            {
                currentValue = Mathf.MoveTowards(currentValue, 0f, Time.deltaTime * 2f);
            }
            
            // Update slider
            if (valueSlider != null)
                valueSlider.value = currentValue;
            
            // Check if in target range
            if (Mathf.Abs(currentValue - targetValue) <= tolerance)
            {
                if (!isHolding)
                {
                    isHolding = true;
                    holdTimer = 0f;
                }
                
                holdTimer += Time.deltaTime;
                
                // Update progress
                float progress = holdTimer / holdTime;
                if (IsServer)
                    UpdateProgressServerRpc(progress);
                
                // Update UI color
                if (sliderFill != null)
                    sliderFill.color = Color.Lerp(Color.yellow, Color.green, progress);
                
                // Check completion
                if (holdTimer >= holdTime)
                {
                    CompleteTask();
                }
            }
            else
            {
                if (isHolding)
                {
                    isHolding = false;
                    holdTimer = 0f;
                    
                    if (IsServer)
                        UpdateProgressServerRpc(0f);
                    
                    if (sliderFill != null)
                        sliderFill.color = Color.white;
                }
            }
        }
        
        private void OnSliderValueChanged(float value)
        {
            currentValue = value;
        }
        
        protected override void StartTaskUI()
        {
            if (valueSlider != null)
                valueSlider.gameObject.SetActive(true);
        }
        
        protected override void CompleteTaskUI()
        {
            if (sliderFill != null)
                sliderFill.color = Color.green;
        }
        
        protected override void CancelTaskUI()
        {
            currentValue = 0f;
            holdTimer = 0f;
            isHolding = false;
            
            if (valueSlider != null)
            {
                valueSlider.value = 0f;
                valueSlider.gameObject.SetActive(false);
            }
            
            if (sliderFill != null)
                sliderFill.color = Color.white;
        }
    }
    
    #endregion
}
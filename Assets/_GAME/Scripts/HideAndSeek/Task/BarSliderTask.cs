using _GAME.Scripts.DesignPattern.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.HideAndSeek.Task
{
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

        protected override void OnStateChanged(InteractionState previousState, InteractionState newState)
        {
            throw new System.NotImplementedException();
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
    
}
using _GAME.Scripts.DesignPattern.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.HideAndSeek.Task
{
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

        protected override void OnStateChanged(InteractionState previousState, InteractionState newState)
        {
            throw new System.NotImplementedException();
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
    
}
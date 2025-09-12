// using System;
// using _GAME.Scripts.DesignPattern.Interaction;
// using Unity.Netcode;
// using UnityEngine;
// using UnityEngine.UI;
//
// namespace _GAME.Scripts.HideAndSeek.Task
// {
//     public abstract class BaseTask : APassiveInteractable, IGameTask
//     {
//         [Header("Task Settings")]
//         [SerializeField] protected int taskId;
//         [SerializeField] protected TaskType taskType;
//         [SerializeField] protected float completionTime = 3f;
//         [SerializeField] protected bool isCompleted = false;
//         
//         [Header("UI")]
//         [SerializeField] protected Canvas taskUI;
//         [SerializeField] protected Slider progressSlider;
//         [SerializeField] protected GameObject completedIndicator;
//         
//         // Network variables
//         protected NetworkVariable<bool> networkCompleted = new NetworkVariable<bool>(false);
//         protected NetworkVariable<float> networkProgress = new NetworkVariable<float>(0f);
//         
//         protected IGamePlayer currentHider;
//         protected Coroutine taskCoroutine;
//         
//         // IGameTask implementation
//         public int TaskId => taskId;
//         public TaskType Type => taskType;
//         public bool IsCompleted => networkCompleted.Value;
//         public static event Action<int, TaskType> OnTaskStarted;
//         public static event Action<int, TaskType> OnTaskCompleted;
//         public static event Action<int, float> OnTaskProgressUpdated;
//         
//         protected override void Awake()
//         {
//             base.Awake();
//             
//             if (taskUI != null)
//                 taskUI.gameObject.SetActive(false);
//         }
//         
//         public override void OnNetworkSpawn()
//         {
//             base.OnNetworkSpawn();
//             
//             networkCompleted.OnValueChanged += OnCompletedChanged;
//             networkProgress.OnValueChanged += OnProgressChanged;
//             
//             if (IsServer)
//             {
//                 taskId = GetInstanceID();
//             }
//         }
//         
//         public override void OnNetworkDespawn()
//         {
//             base.OnNetworkDespawn();
//             
//             networkCompleted.OnValueChanged -= OnCompletedChanged;
//             networkProgress.OnValueChanged -= OnProgressChanged;
//         }
//         
//         public virtual void StartTask(IGamePlayer hider)
//         {
//             if (IsCompleted || currentHider != null) return;
//             
//             currentHider = hider;
//             
//             if (IsServer)
//             {
//                 StartTaskServerRpc(hider.ClientId);
//             }
//         }
//
//         public virtual void CompleteTask()
//         {
//             if (IsCompleted) return;
//             
//             if (IsServer)
//             {
//                 networkCompleted.Value = true;
//                 networkProgress.Value = 1f;
//                 
//                 CompleteTaskClientRpc();
//                 OnTaskCompleted?.Invoke(taskId, taskType);
//             }
//             
//             if (currentHider != null)
//             {
//                 currentHider.CompleteTask(taskId);
//                 currentHider.OnTaskCompleted(taskId);
//             }
//         }
//
//         public void OnTaskInteraction(IGamePlayer hider)
//         {
//             throw new NotImplementedException();
//         }
//
//         public abstract void OnTaskInteraction(IHider hider);
//         
//         #region Server RPCs
//         
//         [ServerRpc(RequireOwnership = false)]
//         protected void StartTaskServerRpc(ulong hiderId)
//         {
//             if (IsCompleted) return;
//             
//             StartTaskClientRpc(hiderId);
//             OnTaskStarted?.Invoke(taskId, taskType);
//         }
//         
//         [ServerRpc(RequireOwnership = false)]
//         protected void UpdateProgressServerRpc(float progress)
//         {
//             if (IsCompleted) return;
//             
//             networkProgress.Value = Mathf.Clamp01(progress);
//             
//             if (progress >= 1f)
//             {
//                 CompleteTask();
//             }
//         }
//         
//         [ServerRpc(RequireOwnership = false)]
//         protected void CancelTaskServerRpc()
//         {
//             if (IsCompleted) return;
//             
//             networkProgress.Value = 0f;
//             CancelTaskClientRpc();
//         }
//         
//         #endregion
//         
//         #region Client RPCs
//         
//         [ClientRpc]
//         protected virtual void StartTaskClientRpc(ulong hiderId)
//         {
//             if (taskUI != null)
//                 taskUI.gameObject.SetActive(true);
//                 
//             StartTaskUI();
//         }
//         
//         [ClientRpc]
//         protected virtual void CompleteTaskClientRpc()
//         {
//             if (taskUI != null)
//                 taskUI.gameObject.SetActive(false);
//                 
//             if (completedIndicator != null)
//                 completedIndicator.SetActive(true);
//                 
//             CompleteTaskUI();
//         }
//         
//         [ClientRpc]
//         protected virtual void CancelTaskClientRpc()
//         {
//             if (taskUI != null)
//                 taskUI.gameObject.SetActive(false);
//                 
//             CancelTaskUI();
//             currentHider = null;
//         }
//         
//         #endregion
//         
//         #region UI Methods
//         
//         protected abstract void StartTaskUI();
//         protected abstract void CompleteTaskUI();
//         protected abstract void CancelTaskUI();
//         
//         #endregion
//         
//         #region Event Handlers
//         
//         private void OnCompletedChanged(bool previousValue, bool newValue)
//         {
//             isCompleted = newValue;
//             
//             if (newValue && completedIndicator != null)
//                 completedIndicator.SetActive(true);
//         }
//         
//         private void OnProgressChanged(float previousValue, float newValue)
//         {
//             if (progressSlider != null)
//                 progressSlider.value = newValue;
//                 
//             OnTaskProgressUpdated?.Invoke(taskId, newValue);
//         }
//         
//         #endregion
//         
//         #region Trigger System Integration
//         
//         protected override void OnInteractionEntered(IInteractable interaction)
//         {
//             if (interaction is IHider hider && !IsCompleted)
//             {
//                 // Show interaction prompt
//                 ShowInteractionPrompt(true);
//             }
//         }
//         
//         protected override void OnInteractionExited(IInteractable interaction)
//         {
//             if (interaction is IHider hider)
//             {
//                 // Hide interaction prompt
//                 ShowInteractionPrompt(false);
//                 
//                 // Cancel task if in progress
//                 if (currentHider == hider)
//                 {
//                     if (IsServer)
//                         CancelTaskServerRpc();
//                 }
//             }
//         }
//         
//         public override bool Interact(IInteractable target)
//         {
//             if (target is IHider hider && !IsCompleted)
//             {
//                 OnTaskInteraction(hider);
//                 return true;
//             }
//             return false;
//         }
//         
//         public override void OnInteracted(IInteractable initiator)
//         {
//             if (initiator is IHider hider && !IsCompleted)
//             {
//                 StartTask(hider);
//             }
//         }
//         
//         private void ShowInteractionPrompt(bool show)
//         {
//             // Implement interaction prompt UI
//             // This would show/hide an interaction hint like "Press E to start task"
//         }
//         
//         #endregion
//     }
//     
// }
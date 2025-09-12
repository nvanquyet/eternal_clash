using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Config
{
    [System.Serializable]
    public struct TaskData
    {
        public TaskType type;
        public float completionTime;     // Thời gian hoàn thành
        public string description;
        public GameObject prefab;
    }
    
    [CreateAssetMenu(menuName = "_GAME/HideAndSeek/TaskDataConfig", fileName = "TaskDataConfig")]
    public class TaskDataConfig : BaseData<TaskData, TaskType>
    {
        protected override void InitDictionary()
        {
            DataDictionary.Clear();
            foreach (var t in data)
            {
                if (!DataDictionary.TryAdd(t.type, t))
                {
                    Debug.LogWarning($"Duplicate TaskType key found: {t.type}. Skipping this entry.");
                }
            }
        }
    }
}
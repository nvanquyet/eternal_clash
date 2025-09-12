using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Config
{
    [System.Serializable]
    public struct ObjectData
    {
        public ObjectType type;
        public float health;
        public Vector3 size;
        public GameObject prefab;
        public string displayName;
    }

    [CreateAssetMenu(menuName = "_GAME/HideAndSeek/ObjectDataConfig", fileName = "ObjectDataConfig")]
    public class ObjectDataConfig : BaseData<ObjectData, ObjectType>
    {
        protected override void InitDictionary()
        {
            DataDictionary.Clear();
            foreach (var o in data)
            {
                if (!DataDictionary.TryAdd(o.type, o))
                {
                    Debug.LogWarning($"Duplicate skill type in SkillDataConfig: {o.type}");
                }
            }
        }
    }
}
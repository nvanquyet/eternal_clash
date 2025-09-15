using System.Collections.Generic;
using _GAME.Scripts.HideAndSeek.Config;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player.Graphics
{
    public enum ModelType
    {
        Person,
        Object,
        Special
    }

    [System.Serializable]
    public class ModelConfigData
    {
        [Header("Model Info")] public string modelName;
        public ModelType modelType; // Person, Object, etc.
        public GameObject modelPrefab; // Prefab đã có Animator setup sẵn

        [Header("Animation Setup")] [Tooltip("Override controller được setup sẵn cho model này")]
        public AnimatorOverrideController overrideController;
        public Avatar avatar; // Nếu cần mask animation cho một số phần của model

        [Header("Game Mode Availability")] public bool availableInPersonVsPerson = true;
        public bool availableInPersonVsObject = true;

        [Header("Role Restrictions")] public bool availableForHider = true;
        public bool availableForSeeker = true;
    }

    [CreateAssetMenu(fileName = "ModelConfigSO", menuName = "Game/Config/Model Config")]
    public class ModelConfigSO : BaseData<ModelConfigData, ModelType>
    {
        protected override void InitDictionary()
        {
            DataDictionary.Clear();
            foreach (var modelData in data)
            {
                if (!DataDictionary.ContainsKey(modelData.modelType))
                {
                    DataDictionary.Add(modelData.modelType, modelData);
                }
            }
        }

        // Helper methods
        public List<ModelConfigData> GetModelsForGameMode(GameMode gameMode, Role playerRole)
        {
            List<ModelConfigData> availableModels = new List<ModelConfigData>();

            foreach (var modelData in data)
            {
                // Check game mode availability
                bool gameModeValid = gameMode switch
                {
                    GameMode.PersonVsPerson => modelData.availableInPersonVsPerson,
                    GameMode.PersonVsObject => modelData.availableInPersonVsObject,
                    _ => false
                };

                // Check role availability
                bool roleValid = playerRole switch
                {
                    Role.Hider => modelData.availableForHider,
                    Role.Seeker => modelData.availableForSeeker,
                    _ => true
                };

                if (gameModeValid && roleValid)
                {
                    availableModels.Add(modelData);
                }
            }

            return availableModels;
        }

        public List<ModelConfigData> GetModelsByType(ModelType type)
        {
            List<ModelConfigData> models = new List<ModelConfigData>();
            foreach (var modelData in data)
            {
                if (modelData.modelType == type)
                {
                    models.Add(modelData);
                }
            }

            return models;
        }
    }
}
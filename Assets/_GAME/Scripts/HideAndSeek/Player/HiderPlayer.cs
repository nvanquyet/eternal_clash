using System;
using System.Linq;
using _GAME.Scripts.HideAndSeek.Object;
using _GAME.Scripts.HideAndSeek.SkillSystem;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player
{
    public class HiderPlayer : RolePlayer, IHider
    {
        [Header("Hider Settings")] [SerializeField]
        private int completedTasks = 0;

        [SerializeField] private int totalTasks = 5;
        [SerializeField] private Transform ghostCamera; // For Case 2 - soul view

        // Current disguise (Case 2)
        private IObjectDisguise currentDisguise;
        private bool isInSoulMode = false;

        // Network variables
        private NetworkVariable<int> networkCompletedTasks = new NetworkVariable<int>(0);
        private NetworkVariable<bool> networkInSoulMode = new NetworkVariable<bool>(false);

        // IHider implementation
        public int CompletedTasks => networkCompletedTasks.Value;
        public int TotalTasks => totalTasks;
        public bool HasSkillsAvailable => Skills.Values.Any(s => s.CanUse);

        public static event Action<int, int> OnTaskProgressChanged;
        public static event Action<bool> OnSoulModeChanged;

        protected override void InitializeSkills()
        {
            // Add hider skills
            var freezeSkill = gameObject.AddComponent<FreezeSkill>();
            freezeSkill.Initialize(SkillType.FreezeSeeker, GameManager.GetSkillData(SkillType.FreezeSeeker));
            Skills[SkillType.FreezeSeeker] = freezeSkill;

            var teleportSkill = gameObject.AddComponent<TeleportSkill>();
            teleportSkill.Initialize(SkillType.Teleport, GameManager.GetSkillData(SkillType.Teleport));
            Skills[SkillType.Teleport] = teleportSkill;

            var shapeshiftSkill = gameObject.AddComponent<ShapeShiftSkill>();
            shapeshiftSkill.Initialize(SkillType.ShapeShift, GameManager.GetSkillData(SkillType.ShapeShift));
            Skills[SkillType.ShapeShift] = shapeshiftSkill;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            networkCompletedTasks.OnValueChanged += OnTasksChanged;
            networkInSoulMode.OnValueChanged += OnSoulModeNetworkChanged;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            networkCompletedTasks.OnValueChanged -= OnTasksChanged;
            networkInSoulMode.OnValueChanged -= OnSoulModeNetworkChanged;
        }

        protected override void HandleInput()
        {
            // Skill inputs
            if (Input.GetKeyDown(KeyCode.Q)) // Freeze seeker
            {
                UseSkill(SkillType.FreezeSeeker);
            }
            else if (Input.GetKeyDown(KeyCode.E)) // Teleport
            {
                UseSkill(SkillType.Teleport);
            }
            else if (Input.GetKeyDown(KeyCode.R)) // Shape shift
            {
                UseSkill(SkillType.ShapeShift);
            }

            // Soul mode toggle (Case 2 only)
            if (GameManager.CurrentMode == GameMode.PersonVsObject)
            {
                if (Input.GetKeyDown(KeyCode.F) && currentDisguise != null)
                {
                    ToggleSoulModeServerRpc();
                }
            }
        }


        public void UseSkill(SkillType skillType, Vector3? targetPosition = null)
        {
            if (!Skills.ContainsKey(skillType) || !Skills[skillType].CanUse) return;

            UseSkillServerRpc(skillType, targetPosition ?? Vector3.zero, targetPosition.HasValue);
        }

        public void CompleteTask(int taskId)
        {
            CompleteTaskServerRpc(taskId);
        }

        public void OnTaskCompleted(int taskId)
        {
            // Handle task completion effects
            Debug.Log($"Task {taskId} completed by {playerName}");
        }

        public void EnterDisguise(IObjectDisguise disguise)
        {
            if (GameManager.CurrentMode != GameMode.PersonVsObject) return;

            currentDisguise = disguise;
            disguise.OccupyObject(this);

            // Hide player model
            GetComponent<Renderer>().enabled = false;
            GetComponent<Collider>().enabled = false;
        }

        public void ExitDisguise()
        {
            if (currentDisguise == null) return;

            currentDisguise.ReleaseObject();
            currentDisguise = null;

            // Show player model
            GetComponent<Renderer>().enabled = true;
            GetComponent<Collider>().enabled = true;
        }

        private void ToggleSoulMode()
        {
            isInSoulMode = !isInSoulMode;

            if (isInSoulMode)
            {
                // Enable ghost camera
                if (ghostCamera != null)
                {
                    ghostCamera.gameObject.SetActive(true);
                }

                // Make player invisible to seekers but visible to other hiders
                SetLayerRecursively(gameObject, LayerMask.NameToLayer("HiderSoul"));
            }
            else
            {
                // Disable ghost camera
                if (ghostCamera != null)
                {
                    ghostCamera.gameObject.SetActive(false);
                }

                // Return to normal layer
                SetLayerRecursively(gameObject, LayerMask.NameToLayer("Player"));
            }
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        #region Server RPCs

        [ServerRpc]
        private void UseSkillServerRpc(SkillType skillType, Vector3 targetPosition, bool hasTarget)
        {
            if (!Skills.ContainsKey(skillType) || !Skills[skillType].CanUse) return;

            Vector3? target = hasTarget ? targetPosition : null;
            Skills[skillType].UseSkill(this, target);
        }

        [ServerRpc]
        private void CompleteTaskServerRpc(int taskId)
        {
            networkCompletedTasks.Value++;
            GameManager.PlayerTaskCompletedServerRpc(ClientId, taskId);
        }

        [ServerRpc]
        private void ToggleSoulModeServerRpc()
        {
            networkInSoulMode.Value = !networkInSoulMode.Value;
        }

        #endregion

        #region Event Handlers

        private void OnTasksChanged(int previousValue, int newValue)
        {
            OnTaskProgressChanged?.Invoke(newValue, totalTasks);
        }

        private void OnSoulModeNetworkChanged(bool previousValue, bool newValue)
        {
            isInSoulMode = newValue;
            ToggleSoulMode();
            OnSoulModeChanged?.Invoke(newValue);
        }

        #endregion

        #region Game Events

        public override void OnGameStart()
        {
            // Initialize based on game mode
            if (GameManager.CurrentMode == GameMode.PersonVsPerson)
            {
                totalTasks = GameManager.Settings.tasksToComplete;
            }
            else if (GameManager.CurrentMode == GameMode.PersonVsObject)
            {
                // Find and enter initial disguise
                var nearestDisguise = FindAvailableDisguise();
                if (nearestDisguise != null)
                {
                    EnterDisguise(nearestDisguise);
                }
            }
        }

        public override void OnGameEnd(PlayerRole winnerRole)
        {
            // Handle game end
            if (currentDisguise != null)
            {
                ExitDisguise();
            }
        }

        #endregion

        private IObjectDisguise FindAvailableDisguise()
        {
            //Todo: filter and random disguise object
            return gameObject.AddComponent<TableDisguise>();
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using HideAndSeekGame.Core;
using HideAndSeekGame.Managers;
using _GAME.Scripts.DesignPattern.Interaction;

namespace HideAndSeekGame.Players
{
    #region Base Player Class
    
    public abstract class BasePlayer : ACombatEntity, IGamePlayer
    {
        [Header("Player Settings")]
        [SerializeField] protected string playerName = "Player";
        [SerializeField] protected PlayerRole role;
        [SerializeField] protected float moveSpeed = 5f;
        
        [Header("Network")]
        [SerializeField] protected NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>();
        [SerializeField] protected NetworkVariable<bool> networkIsAlive = new NetworkVariable<bool>(true);
        
        protected CharacterController characterController;
        protected GameManager gameManager;
        protected Dictionary<SkillType, ISkill> skills = new Dictionary<SkillType, ISkill>();
        
        // IGamePlayer implementation
        public ulong ClientId => NetworkObject.OwnerClientId;
        public PlayerRole Role => role;
        public string PlayerName => playerName;
        public override bool IsAlive => networkIsAlive.Value;
        public override Vector3 Position => transform.position;
        
        public static event Action<ulong, PlayerRole> OnPlayerRoleChanged;
        
        protected override void Awake()
        {
            base.Awake();
            characterController = GetComponent<CharacterController>();
            gameManager = FindObjectOfType<GameManager>();
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            networkPosition.OnValueChanged += OnPositionChanged;
            networkIsAlive.OnValueChanged += OnAliveStatusChanged;
            
            if (IsOwner)
            {
                gameManager?.RegisterPlayer(this);
            }
            
            InitializeSkills();
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            networkPosition.OnValueChanged -= OnPositionChanged;
            networkIsAlive.OnValueChanged -= OnAliveStatusChanged;
        }
        
        protected virtual void Update()
        {
            if (!IsOwner || !IsAlive) return;
            
            HandleMovement();
            HandleInput();
            
            // Update network position
            if (Vector3.Distance(networkPosition.Value, transform.position) > 0.1f)
            {
                UpdatePositionServerRpc(transform.position);
            }
        }
        
        protected virtual void HandleMovement()
        {
            if (characterController == null) return;
            
            float horizontal = Input.GetAxis("Horizontal");
            float vertical = Input.GetAxis("Vertical");
            
            Vector3 movement = new Vector3(horizontal, 0, vertical) * moveSpeed * Time.deltaTime;
            
            // Add gravity
            movement.y = -9.81f * Time.deltaTime;
            
            characterController.Move(movement);
        }
        
        protected abstract void HandleInput();
        protected abstract void InitializeSkills();
        
        #region Network RPCs
        
        [ServerRpc]
        protected void UpdatePositionServerRpc(Vector3 position)
        {
            networkPosition.Value = position;
        }
        
        [ServerRpc]
        protected void SetAliveStatusServerRpc(bool isAlive)
        {
            networkIsAlive.Value = isAlive;
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnPositionChanged(Vector3 previousValue, Vector3 newValue)
        {
            if (!IsOwner)
            {
                transform.position = newValue;
            }
        }
        
        private void OnAliveStatusChanged(bool previousValue, bool newValue)
        {
            if (!newValue)
            {
                OnDeath();
            }
        }
        
        #endregion
        
        #region IGamePlayer Implementation
        
        public virtual void SetRole(PlayerRole newRole)
        {
            role = newRole;
            OnPlayerRoleChanged?.Invoke(ClientId, newRole);
        }
        
        public abstract void OnGameStart();
        public abstract void OnGameEnd(PlayerRole winnerRole);
        
        #endregion
        
        #region Interaction System Integration
        
        public override bool Interact(IInteractable target)
        {
            // Base interaction logic
            return true;
        }
        
        public override void OnInteracted(IInteractable initiator)
        {
            // Handle being interacted with
        }
        
        protected override void OnStateChanged(InteractionState previousState, InteractionState newState)
        {
            // Handle state changes
            if (newState == InteractionState.Dead && IsAlive)
            {
                SetAliveStatusServerRpc(false);
            }
        }
        
        #endregion
    }
    
    #endregion
    
    #region Hider Player Class
    
    public class HiderPlayer : BasePlayer, IHider
    {
        [Header("Hider Settings")]
        [SerializeField] private int completedTasks = 0;
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
        public bool HasSkillsAvailable => skills.Values.Any(s => s.CanUse);
        
        public static event Action<int, int> OnTaskProgressChanged;
        public static event Action<bool> OnSoulModeChanged;
        
        protected override void InitializeSkills()
        {
            // Add hider skills
            var freezeSkill = gameObject.AddComponent<FreezeSkill>();
            freezeSkill.Initialize(SkillType.FreezeSeeker, gameManager.GetSkillData(SkillType.FreezeSeeker));
            skills[SkillType.FreezeSeeker] = freezeSkill;
            
            var teleportSkill = gameObject.AddComponent<TeleportSkill>();
            teleportSkill.Initialize(SkillType.Teleport, gameManager.GetSkillData(SkillType.Teleport));
            skills[SkillType.Teleport] = teleportSkill;
            
            var shapeshiftSkill = gameObject.AddComponent<ShapeShiftSkill>();
            shapeshiftSkill.Initialize(SkillType.ShapeShift, gameManager.GetSkillData(SkillType.ShapeShift));
            skills[SkillType.ShapeShift] = shapeshiftSkill;
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
            if (gameManager.CurrentMode == GameMode.PersonVsObject)
            {
                if (Input.GetKeyDown(KeyCode.F) && currentDisguise != null)
                {
                    ToggleSoulModeServerRpc();
                }
            }
        }
        
        protected override void HandleMovement()
        {
            // Don't move if in disguise mode (Case 2) and not in soul mode
            if (gameManager.CurrentMode == GameMode.PersonVsObject && currentDisguise != null && !isInSoulMode)
            {
                return;
            }
            
            base.HandleMovement();
        }
        
        public void UseSkill(SkillType skillType, Vector3? targetPosition = null)
        {
            if (!skills.ContainsKey(skillType) || !skills[skillType].CanUse) return;
            
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
            if (gameManager.CurrentMode != GameMode.PersonVsObject) return;
            
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
            if (!skills.ContainsKey(skillType) || !skills[skillType].CanUse) return;
            
            Vector3? target = hasTarget ? targetPosition : null;
            skills[skillType].UseSkill(this, target);
        }
        
        [ServerRpc]
        private void CompleteTaskServerRpc(int taskId)
        {
            networkCompletedTasks.Value++;
            gameManager.PlayerTaskCompletedServerRpc(ClientId, taskId);
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
            if (gameManager.CurrentMode == GameMode.PersonVsPerson)
            {
                totalTasks = gameManager.Settings.tasksToComplete;
            }
            else if (gameManager.CurrentMode == GameMode.PersonVsObject)
            {
                // Find and enter initial disguise
                var nearestDisguise = FindNearestAvailableDisguise();
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
        
        private IObjectDisguise FindNearestAvailableDisguise()
        {
            var disguises = FindObjectsOfType<MonoBehaviour>().OfType<IObjectDisguise>();
            return disguises.Where(d => !d.IsOccupied)
                           .OrderBy(d => Vector3.Distance(Position, d.Position))
                           .FirstOrDefault();
        }
    }
    
    #endregion
    
    #region Seeker Player Class
    
    public class SeekerPlayer : BasePlayer, ISeeker
    {
        [Header("Seeker Settings")]
        [SerializeField] private float shootRange = 100f;
        [SerializeField] private float shootCooldown = 0.5f;
        [SerializeField] private LayerMask shootableLayers = -1;
        
        private Camera playerCamera;
        private float nextShootTime;
        private NetworkVariable<float> networkHealth = new NetworkVariable<float>(100f);
        
        // ISeeker implementation
        public float CurrentHealth => networkHealth.Value;
        public float MaxHealth => gameManager.Settings.seekerHealth;
        public bool HasSkillsAvailable => skills.Values.Any(s => s.CanUse);
        public bool CanShoot => Time.time >= nextShootTime && IsAlive;
        
        public static event Action<float, float> OnHealthChanged;
        public static event Action<Vector3, bool> OnShootPerformed; // position, hit target
        
        protected override void Awake()
        {
            base.Awake();
            playerCamera = GetComponentInChildren<Camera>();
        }
        
        protected override void InitializeSkills()
        {
            // Add seeker skills
            var detectSkill = gameObject.AddComponent<DetectSkill>();
            detectSkill.Initialize(SkillType.Detect, gameManager.GetSkillData(SkillType.Detect));
            skills[SkillType.Detect] = detectSkill;
            
            var freezeSkill = gameObject.AddComponent<FreezeSkill>();
            freezeSkill.Initialize(SkillType.FreezeHider, gameManager.GetSkillData(SkillType.FreezeHider));
            skills[SkillType.FreezeHider] = freezeSkill;
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            networkHealth.OnValueChanged += OnHealthNetworkChanged;
            
            if (IsServer)
            {
                networkHealth.Value = gameManager.Settings.seekerHealth;
            }
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            networkHealth.OnValueChanged -= OnHealthNetworkChanged;
        }
        
        protected override void HandleInput()
        {
            // Shooting
            if (Input.GetMouseButtonDown(0) && CanShoot)
            {
                Shoot();
            }
            
            // Skills
            if (Input.GetKeyDown(KeyCode.Q)) // Detect
            {
                UseSkill(SkillType.Detect);
            }
            else if (Input.GetKeyDown(KeyCode.E)) // Freeze hider
            {
                UseSkill(SkillType.FreezeHider);
            }
        }
        
        private void Shoot()
        {
            if (playerCamera == null) return;
            
            Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
            Vector3 shootDirection = ray.direction;
            Vector3 hitPoint = ray.origin + ray.direction * shootRange;
            
            if (Physics.Raycast(ray, out RaycastHit hit, shootRange, shootableLayers))
            {
                hitPoint = hit.point;
            }
            
            Shoot(shootDirection, hitPoint);
        }
        
        public void Shoot(Vector3 direction, Vector3 hitPoint)
        {
            if (!CanShoot) return;
            
            ShootServerRpc(direction, hitPoint);
            nextShootTime = Time.time + shootCooldown;
        }
        
        public void UseSkill(SkillType skillType, Vector3? targetPosition = null)
        {
            if (!skills.ContainsKey(skillType) || !skills[skillType].CanUse) return;
            
            UseSkillServerRpc(skillType, targetPosition ?? Vector3.zero, targetPosition.HasValue);
        }
        
        public void TakeDamage(float damage)
        {
            TakeDamageServerRpc(damage);
        }
        
        public void RestoreHealth(float amount)
        {
            RestoreHealthServerRpc(amount);
        }
        
        #region Server RPCs
        
        [ServerRpc]
        private void ShootServerRpc(Vector3 direction, Vector3 hitPoint)
        {
            // Perform raycast on server
            Ray ray = new Ray(playerCamera.transform.position, direction);
            bool hitTarget = false;
            
            if (Physics.Raycast(ray, out RaycastHit hit, shootRange, shootableLayers))
            {
                // Check what was hit
                var hitObject = hit.collider.gameObject;
                
                // Check if hit a hider
                var hiderPlayer = hitObject.GetComponent<HiderPlayer>();
                if (hiderPlayer != null)
                {
                    // Kill hider
                    gameManager.PlayerKilledServerRpc(ClientId, hiderPlayer.ClientId);
                    hitTarget = true;
                }
                else
                {
                    // Check if hit disguised object
                    var disguiseObject = hitObject.GetComponent<IObjectDisguise>();
                    if (disguiseObject != null && disguiseObject.IsOccupied)
                    {
                        disguiseObject.TakeDamage(baseDamage, this);
                        hitTarget = true;
                    }
                    else
                    {
                        // Hit environment - take damage
                        TakeDamage(gameManager.Settings.environmentDamage);
                    }
                }
            }
            else
            {
                // Missed - take damage
                TakeDamage(gameManager.Settings.environmentDamage);
            }
            
            // Notify all clients about the shot
            ShootEffectClientRpc(hitPoint, hitTarget);
        }
        
        [ServerRpc]
        private void UseSkillServerRpc(SkillType skillType, Vector3 targetPosition, bool hasTarget)
        {
            if (!skills.ContainsKey(skillType) || !skills[skillType].CanUse) return;
            
            Vector3? target = hasTarget ? targetPosition : null;
            skills[skillType].UseSkill(this, target);
        }
        
        [ServerRpc]
        private void TakeDamageServerRpc(float damage)
        {
            networkHealth.Value = Mathf.Max(0, networkHealth.Value - damage);
            gameManager.PlayerTookDamageServerRpc(ClientId, damage);
            
            if (networkHealth.Value <= 0)
            {
                SetAliveStatusServerRpc(false);
            }
        }
        
        [ServerRpc]
        private void RestoreHealthServerRpc(float amount)
        {
            networkHealth.Value = Mathf.Min(MaxHealth, networkHealth.Value + amount);
        }
        
        #endregion
        
        #region Client RPCs
        
        [ClientRpc]
        private void ShootEffectClientRpc(Vector3 hitPoint, bool hitTarget)
        {
            OnShootPerformed?.Invoke(hitPoint, hitTarget);
            
            // Add visual/audio effects here
            // CreateMuzzleFlash();
            // CreateBulletTrail(hitPoint);
            // if (hitTarget) CreateHitEffect(hitPoint);
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnHealthNetworkChanged(float previousValue, float newValue)
        {
            OnHealthChanged?.Invoke(newValue, MaxHealth);
        }
        
        #endregion
        
        #region Game Events
        
        public override void OnGameStart()
        {
            // Seeker-specific initialization
        }
        
        public override void OnGameEnd(PlayerRole winnerRole)
        {
            // Handle game end
        }
        
        #endregion
    }
    
    #endregion
}
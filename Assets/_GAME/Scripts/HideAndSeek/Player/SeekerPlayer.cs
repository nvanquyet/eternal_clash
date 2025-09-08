using System;
using System.Linq;
using _GAME.Scripts.HideAndSeek.SkillSystem;
using _GAME.Scripts.Utils;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.HideAndSeek.Player
{
     public class SeekerPlayer : RolePlayer, ISeeker
    {
        [Header("Seeker Settings")]
        [SerializeField] private float shootRange = 100f;
        [SerializeField] private float shootCooldown = 0.5f;
        [SerializeField] private LayerMask shootableLayers = -1;
        
        [SerializeField] private InputActionReference attackActionRef;
        private InputAction attackAction; 
        private Camera playerCamera;
        private float nextShootTime;
        private NetworkVariable<float> networkHealth = new NetworkVariable<float>(100f);
        
        // ISeeker implementation
        public float CurrentHealth => networkHealth.Value;
        public float MaxHealth => GameManager.Instance.Settings.seekerHealth;
        public bool HasSkillsAvailable => Skills.Values.Any(s => s.CanUse);
        public bool CanShoot => Time.time >= nextShootTime && IsAlive;
        
        public static event Action<float, float> OnHealthChanged;
        public static event Action<Vector3, bool> OnShootPerformed; // position, hit target
        
        protected override void Awake()
        {
            base.Awake();
            playerCamera = Camera.main;
        }
        

        protected override void HandleUnRegisterInput()
        {
            if (attackAction == null) return;
            attackAction.performed -= Attack;
            attackAction?.Disable();
            attackAction = null;
        }

        protected override void InitializeSkills()
        {
            // Add seeker skills
            var detectSkill = gameObject.AddComponent<DetectSkill>();
            detectSkill.Initialize(SkillType.Detect, GameManager.Instance.GetSkillData(SkillType.Detect));
            Skills[SkillType.Detect] = detectSkill;
            
            var freezeSkill = gameObject.AddComponent<FreezeSkill>();
            freezeSkill.Initialize(SkillType.FreezeHider, GameManager.Instance.GetSkillData(SkillType.FreezeHider));
            Skills[SkillType.FreezeHider] = freezeSkill;
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            networkHealth.OnValueChanged += OnHealthNetworkChanged;
            
            if (IsServer)
            {
                networkHealth.Value = GameManager.Instance.Settings.seekerHealth;
            }
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            networkHealth.OnValueChanged -= OnHealthNetworkChanged;
        }

        protected override void HandleRegisterInput()
        {
            if (IsOwner)
            {
                int instanceId = GetInstanceID();

                // Tạo actions sử dụng factory
                attackAction = InputActionFactory.CreateUniqueAction(attackActionRef, instanceId);
                if (attackAction != null)
                {
                    Debug.Log("$[SeekerPlayer] Setting up input actions for SeekerPlayer");
                    attackAction.performed += Attack;
                    attackAction?.Enable();
                }
            }

            // // Skills
            // if (Input.GetKeyDown(KeyCode.Q)) // Detect
            // {
            //     UseSkill(SkillType.Detect);
            // }
            // else if (Input.GetKeyDown(KeyCode.E)) // Freeze hider
            // {
            //     UseSkill(SkillType.FreezeHider);
            // }
        }


        private void Attack(InputAction.CallbackContext obj)
        {
            Debug.Log($"[SeekerPlayer] Attack input received");
            if(CanShoot) Shoot();
        }

        
        private void Shoot()
        {
            Debug.Log($"Seeker shooting");
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
            Debug.Log($"Seeker {ClientId} shooting from {playerCamera.transform.position} towards {direction} to hit point {hitPoint}");
            if (!CanShoot) return;
            
            ShootServerRpc(direction, hitPoint);
            nextShootTime = Time.time + shootCooldown;
        }
        
        public void UseSkill(SkillType skillType, Vector3? targetPosition = null)
        {
            if (!Skills.ContainsKey(skillType) || !Skills[skillType].CanUse) return;
            
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
            Debug.Log($"Seeker {ClientId} shooting from {playerCamera.transform.position} towards {direction} to hit point {hitPoint}");
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
                    GameManager.Instance.PlayerKilledServerRpc(ClientId, hiderPlayer.ClientId);
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
                        TakeDamage(GameManager.Instance.Settings.environmentDamage);
                    }
                }
            }
            else
            {
                // Missed - take damage
                TakeDamage(GameManager.Instance.Settings.environmentDamage);
            }
            
            // Notify all clients about the shot
            ShootEffectClientRpc(hitPoint, hitTarget);
        }
        
        [ServerRpc]
        private void UseSkillServerRpc(SkillType skillType, Vector3 targetPosition, bool hasTarget)
        {
            if (!Skills.ContainsKey(skillType) || !Skills[skillType].CanUse) return;
            
            Vector3? target = hasTarget ? targetPosition : null;
            Skills[skillType].UseSkill(this, target);
        }
        
        [ServerRpc]
        private void TakeDamageServerRpc(float damage)
        {
            networkHealth.Value = Mathf.Max(0, networkHealth.Value - damage);
            GameManager.Instance.PlayerTookDamageServerRpc(ClientId, damage);
            
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
    
}
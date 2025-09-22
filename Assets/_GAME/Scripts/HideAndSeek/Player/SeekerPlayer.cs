using System;
using System.Collections.Generic;
using System.Linq;
using _GAME.Scripts.HideAndSeek.SkillSystem;
using _GAME.Scripts.Utils;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _GAME.Scripts.HideAndSeek.Player
{
    /// <summary>
    /// Seeker player implementation with detection and pursuit abilities
    /// Network synchronized with proper validation
    /// </summary>
    public class SeekerPlayer : RolePlayer
    {
        [Header("Seeker Settings")]
        [SerializeField] private float detectionRange = 15f;
        [SerializeField] private float catchRange = 2f;
        [SerializeField] private LayerMask hiderLayers = -1;
        [SerializeField] private LayerMask obstacleLayersForLineOfSight = -1;

        [Header("Input References")]
        [SerializeField] private InputActionReference detectSkillRef;
        [SerializeField] private InputActionReference freezeSkillRef;
        [SerializeField] private InputActionReference rushSkillRef;

        // Input Actions
        private InputAction detectSkillAction;
        private InputAction freezeSkillAction;
        private InputAction rushSkillAction;

        // Network Variables (Server Authoritative)
        private NetworkVariable<int> networkHidersCaught = new NetworkVariable<int>(
            0, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );

        private NetworkVariable<bool> networkIsDetecting = new NetworkVariable<bool>(
            false, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );

        private NetworkVariable<float> networkDetectionRadius = new NetworkVariable<float>(
            0f, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );

        private NetworkVariable<bool> networkIsRushing = new NetworkVariable<bool>(
            false, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server
        );

        // Server-side tracking
        private readonly HashSet<ulong> caughtHiders = new HashSet<ulong>();
        private readonly Dictionary<ulong, float> hiderCatchTimes = new Dictionary<ulong, float>();
        private readonly List<IGamePlayer> detectedHiders = new List<IGamePlayer>();

        // Events
        public static event Action<int> OnHidersCaughtChanged;
        public static event Action<bool, float> OnDetectionStateChanged;
        public static event Action<List<IGamePlayer>> OnHidersDetected;

        #region IGamePlayer Implementation

        public int HidersCaught => networkHidersCaught.Value;
        public bool IsDetecting => networkIsDetecting.Value;
        public float DetectionRadius => networkDetectionRadius.Value;
        public bool IsRushing => networkIsRushing.Value;
        public override bool HasSkillsAvailable => Skills.Values.Any(s => s.CanUse) && IsAlive;

        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Subscribe to network variable changes
            networkHidersCaught.OnValueChanged += OnHidersCaughtNetworkChanged;
            networkIsDetecting.OnValueChanged += OnDetectionStateNetworkChanged;
            networkDetectionRadius.OnValueChanged += OnDetectionRadiusNetworkChanged;
            networkIsRushing.OnValueChanged += OnRushingStateNetworkChanged;

            // Initialize server-side values
            if (IsServer)
            {
                networkHidersCaught.Value = 0;
                networkIsDetecting.Value = false;
                networkDetectionRadius.Value = 0f;
                networkIsRushing.Value = false;

                // Initialize seeker health from game settings
                var seekerHealth = GameManager?.Settings?.seekerHealth ?? 100f;
                networkCurrentHealth.Value = seekerHealth;
            }

            LogNetworkState("SeekerPlayer spawned");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            // Cleanup subscriptions
            if (networkHidersCaught != null)
                networkHidersCaught.OnValueChanged -= OnHidersCaughtNetworkChanged;
            if (networkIsDetecting != null)
                networkIsDetecting.OnValueChanged -= OnDetectionStateNetworkChanged;
            if (networkDetectionRadius != null)
                networkDetectionRadius.OnValueChanged -= OnDetectionRadiusNetworkChanged;
            if (networkIsRushing != null)
                networkIsRushing.OnValueChanged -= OnRushingStateNetworkChanged;

            // Server cleanup
            if (IsServer)
            {
                caughtHiders.Clear();
                hiderCatchTimes.Clear();
                detectedHiders.Clear();
            }

            LogNetworkState("SeekerPlayer despawned");
        }

        #endregion

        #region Skill Initialization

        protected override void InitializeSkills()
        {
            if (Role != Role.Seeker) return;

            try
            {
                // Initialize seeker skills with proper validation
                var detectSkill = gameObject.GetComponent<DetectSkill>() ?? gameObject.AddComponent<DetectSkill>();
                detectSkill.Initialize(SkillType.Detect, GameManager.GetSkillData(SkillType.Detect));
                Skills[SkillType.Detect] = detectSkill;

                var freezeSkill = gameObject.GetComponent<FreezeSkill>() ?? gameObject.AddComponent<FreezeSkill>();
                freezeSkill.Initialize(SkillType.FreezeHider, GameManager.GetSkillData(SkillType.FreezeHider));
                Skills[SkillType.FreezeHider] = freezeSkill;

                // Optional rush skill
                var skill = GameManager.GetSkillData(SkillType.Rush);
                if (skill.type != SkillType.None) // giả sử bạn có enum None = 0
                {
                    var rushSkill = gameObject.GetComponent<RushSkill>() ?? gameObject.AddComponent<RushSkill>();
                    rushSkill.Initialize(SkillType.Rush, GameManager.GetSkillData(SkillType.Rush));
                    Skills[SkillType.Rush] = rushSkill;
                }

                Debug.Log($"[SeekerPlayer] Skills initialized: {Skills.Count}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SeekerPlayer] Failed to initialize skills: {ex.Message}");
            }
        }

        #endregion

        #region Input System

        protected override void HandleRegisterInput()
        {
            if (!IsOwner) return;

            try
            {
                // Detect skill
                if (detectSkillRef?.action != null)
                {
                    detectSkillAction = detectSkillRef.action;
                    detectSkillAction.performed += OnDetectSkillPerformed;
                    detectSkillAction.Enable();
                }

                // Freeze skill
                if (freezeSkillRef?.action != null)
                {
                    freezeSkillAction = freezeSkillRef.action;
                    freezeSkillAction.performed += OnFreezeSkillPerformed;
                    freezeSkillAction.Enable();
                }

                // Rush skill
                if (rushSkillRef?.action != null)
                {
                    rushSkillAction = rushSkillRef.action;
                    rushSkillAction.performed += OnRushSkillPerformed;
                    rushSkillAction.Enable();
                }

                Debug.Log("[SeekerPlayer] Input registered successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SeekerPlayer] Failed to register input: {ex.Message}");
            }
        }

        protected override void HandleUnRegisterInput()
        {
            if (!IsOwner) return;

            try
            {
                detectSkillAction?.Disable();
                freezeSkillAction?.Disable();
                rushSkillAction?.Disable();

                Debug.Log("[SeekerPlayer] Input unregistered successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SeekerPlayer] Failed to unregister input: {ex.Message}");
            }
        }

        #region Input Callbacks

        private void OnDetectSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || !IsAlive) return;
            
            UseSkill(SkillType.Detect, transform.position);
        }

        private void OnFreezeSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || !IsAlive) return;
            
            // Target the nearest hider or use current position
            Vector3 targetPosition = GetNearestHiderPosition() ?? transform.position;
            UseSkill(SkillType.FreezeHider, targetPosition);
        }

        private void OnRushSkillPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || !IsAlive) return;
            
            if (Skills.ContainsKey(SkillType.Rush))
            {
                UseSkill(SkillType.Rush, transform.forward * 10f + transform.position);
            }
        }

        private Vector3? GetNearestHiderPosition()
        {
            var nearestHider = FindNearestHider();
            return nearestHider?.Position;
        }

        private IGamePlayer FindNearestHider()
        {
            var hiders = GameManager?.GetAlivePlayers()?.Where(p => p.Role == Role.Hider);
            if (hiders == null) return null;

            IGamePlayer nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (var hider in hiders)
            {
                float distance = Vector3.Distance(transform.position, hider.Position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = hider;
                }
            }

            return nearest;
        }

        #endregion

        #endregion

        #region Detection System (Server Authoritative)

        public void StartDetection()
        {
            if (IsOwner)
            {
                StartDetectionServerRpc();
            }
        }

        [ServerRpc]
        private void StartDetectionServerRpc()
        {
            if (!IsServer || !IsAlive) return;
            if (networkIsDetecting.Value) return;

            networkIsDetecting.Value = true;
            networkDetectionRadius.Value = detectionRange;

            // Start detection update loop
            InvokeRepeating(nameof(UpdateDetection), 0f, 0.1f);

            Debug.Log($"[SeekerPlayer] Detection started by client {ClientId}");
        }

        public void StopDetection()
        {
            if (IsServer)
            {
                StopDetectionServer();
            }
            else if (IsOwner)
            {
                StopDetectionServerRpc();
            }
        }

        [ServerRpc]
        private void StopDetectionServerRpc()
        {
            StopDetectionServer();
        }

        private void StopDetectionServer()
        {
            if (!IsServer) return;
            if (!networkIsDetecting.Value) return;

            networkIsDetecting.Value = false;
            networkDetectionRadius.Value = 0f;

            // Stop detection update loop
            CancelInvoke(nameof(UpdateDetection));

            // Clear detected hiders
            detectedHiders.Clear();
            NotifyHidersDetectedClientRpc(new ulong[0]);

            Debug.Log($"[SeekerPlayer] Detection stopped by client {ClientId}");
        }

        private void UpdateDetection()
        {
            if (!IsServer || !networkIsDetecting.Value || !IsAlive) return;

            // Find hiders within detection range
            var hidersInRange = FindHidersInRange();
            
            // Update detected hiders list
            detectedHiders.Clear();
            detectedHiders.AddRange(hidersInRange);

            // Notify clients about detected hiders
            var detectedIds = hidersInRange.Select(h => h.ClientId).ToArray();
            NotifyHidersDetectedClientRpc(detectedIds);
        }

        private List<IGamePlayer> FindHidersInRange()
        {
            var detectedList = new List<IGamePlayer>();
            var allHiders = GameManager?.GetAlivePlayers()?.Where(p => p.Role == Role.Hider);
            
            if (allHiders == null) return detectedList;

            foreach (var hider in allHiders)
            {
                // Check distance
                float distance = Vector3.Distance(transform.position, hider.Position);
                if (distance > detectionRange) continue;

                // Check line of sight
                if (HasLineOfSight(hider.Position))
                {
                    detectedList.Add(hider);
                }
            }

            return detectedList;
        }

        private bool HasLineOfSight(Vector3 targetPosition)
        {
            Vector3 direction = targetPosition - transform.position;
            float distance = direction.magnitude;

            // Raycast to check for obstacles
            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, direction.normalized, 
                out RaycastHit hit, distance, obstacleLayersForLineOfSight))
            {
                return false; // Obstacle blocking line of sight
            }

            return true;
        }

        [ClientRpc]
        private void NotifyHidersDetectedClientRpc(ulong[] detectedHiderIds)
        {
            var detectedPlayers = new List<IGamePlayer>();
            
            if (detectedHiderIds != null && detectedHiderIds.Length > 0)
            {
                var allPlayers = GameManager?.GetAllPlayers();
                if (allPlayers != null)
                {
                    foreach (var id in detectedHiderIds)
                    {
                        var player = allPlayers.FirstOrDefault(p => p.ClientId == id);
                        if (player != null)
                        {
                            detectedPlayers.Add(player);
                        }
                    }
                }
            }

            OnHidersDetected?.Invoke(detectedPlayers);

            if (IsOwner)
            {
                UpdateDetectedHidersUI(detectedPlayers);
            }
        }

        #endregion

        #region Catch System (Server Authoritative)

        public void AttemptCatchHider(IGamePlayer hider)
        {
            if (!IsOwner || hider == null) return;
            
            // Client-side validation
            if (!CanCatchHiderLocally(hider))
            {
                ShowCatchErrorMessage("Cannot catch this hider");
                return;
            }

            AttemptCatchHiderServerRpc(hider.ClientId, transform.position);
        }

        [ServerRpc]
        private void AttemptCatchHiderServerRpc(ulong hiderClientId, Vector3 seekerPosition)
        {
            if (!IsServer) return;

            // Server-side validation
            if (!ValidateCatchAttemptServer(hiderClientId, seekerPosition))
            {
                NotifyCatchFailedClientRpc(hiderClientId, "Server validation failed");
                return;
            }

            // Execute catch
            bool success = ExecuteCatchServer(hiderClientId);
            
            if (success)
            {
                // Update caught count
                caughtHiders.Add(hiderClientId);
                hiderCatchTimes[hiderClientId] = Time.time;
                networkHidersCaught.Value++;

                // Notify game manager
                GameManager?.OnHiderCaught(hiderClientId, ClientId);

                // Notify all clients
                OnHiderCaughtClientRpc(hiderClientId, seekerPosition);

                Debug.Log($"[SeekerPlayer] Hider {hiderClientId} caught by seeker {ClientId}");
            }
            else
            {
                NotifyCatchFailedClientRpc(hiderClientId, "Catch execution failed");
            }
        }

        [ClientRpc]
        private void OnHiderCaughtClientRpc(ulong hiderClientId, Vector3 catchPosition)
        {
            OnHiderCaughtLocal(hiderClientId, catchPosition);
        }

        [ClientRpc]
        private void NotifyCatchFailedClientRpc(ulong hiderClientId, string reason)
        {
            if (IsOwner)
            {
                ShowCatchErrorMessage($"Failed to catch hider: {reason}");
            }
        }

        #region Catch Validation

        private bool CanCatchHiderLocally(IGamePlayer hider)
        {
            if (!IsAlive || hider == null) return false;
            if (hider.Role != Role.Hider) return false;
            if (!hider.IsAlive) return false;
            
            // Check distance
            float distance = Vector3.Distance(transform.position, hider.Position);
            if (distance > catchRange) return false;

            return true;
        }

        private bool ValidateCatchAttemptServer(ulong hiderClientId, Vector3 seekerPosition)
        {
            if (!IsServer) return false;
            if (!networkIsAlive.Value) return false;
            if (caughtHiders.Contains(hiderClientId)) return false;

            // Find the hider player
            var hider = GameManager?.GetPlayerByClientId(hiderClientId);
            if (hider == null || !hider.IsAlive || hider.Role != Role.Hider) return false;

            // Validate distance between seeker and hider
            float distance = Vector3.Distance(seekerPosition, hider.Position);
            if (distance > catchRange * 1.2f) // Allow slight tolerance for network lag
            {
                Debug.LogWarning($"[SeekerPlayer] Catch attempt failed - distance too far: {distance} > {catchRange}");
                return false;
            }

            return true;
        }

        private bool ExecuteCatchServer(ulong hiderClientId)
        {
            if (!IsServer) return false;

            try
            {
                var hider = GameManager?.GetPlayerByClientId(hiderClientId);
                if (hider == null) return false;

                // Set hider as caught/dead
                hider.SetAliveState(false);
                
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SeekerPlayer] Catch execution error: {ex.Message}");
                return false;
            }
        }

        private void OnHiderCaughtLocal(ulong hiderClientId, Vector3 catchPosition)
        {
            // Play catch effects for all clients
            PlayCatchEffect(catchPosition);

            // Update UI for owner
            if (IsOwner)
            {
                ShowCatchSuccessMessage();
                UpdateCaughtCountUI();
            }

            // Handle game state changes
            CheckWinCondition();
        }

        #endregion

        #endregion

        #region Rush System (Server Authoritative)

        public void StartRush()
        {
            if (IsOwner && Skills.ContainsKey(SkillType.Rush))
            {
                StartRushServerRpc();
            }
        }

        [ServerRpc]
        private void StartRushServerRpc()
        {
            if (!IsServer || !IsAlive) return;
            if (networkIsRushing.Value) return;

            // Validate rush skill availability
            if (!Skills.ContainsKey(SkillType.Rush) || !Skills[SkillType.Rush].CanUse) return;

            networkIsRushing.Value = true;

            // Apply rush effects (increased speed, etc.)
            // Auto-stop rush after duration
            float rushDuration = Skills[SkillType.Rush].GetEffectDuration();
            Invoke(nameof(StopRushServer), rushDuration);

            Debug.Log($"[SeekerPlayer] Rush started by client {ClientId}");
        }

        private void StopRushServer()
        {
            if (!IsServer) return;
            if (!networkIsRushing.Value) return;

            networkIsRushing.Value = false;

            // Remove rush effects
            RemoveRushEffectsServer();

            Debug.Log($"[SeekerPlayer] Rush stopped for client {ClientId}");
        }
        
        private void RemoveRushEffectsServer()
        {
            if (!IsServer) return;

        }

        #endregion

        #region Network Variable Callbacks

        private void OnHidersCaughtNetworkChanged(int previousValue, int newValue)
        {
            OnHidersCaughtChanged?.Invoke(newValue);

            if (IsOwner)
            {
                UpdateCaughtCountUI();
            }

            // Check win condition on server
            if (IsServer && newValue > previousValue)
            {
                CheckWinCondition();
            }
        }

        private void OnDetectionStateNetworkChanged(bool previousValue, bool newValue)
        {
            float radius = newValue ? networkDetectionRadius.Value : 0f;
            OnDetectionStateChanged?.Invoke(newValue, radius);

            if (IsOwner)
            {
                UpdateDetectionUI(newValue, radius);
            }
        }

        private void OnDetectionRadiusNetworkChanged(float previousValue, float newValue)
        {
            if (networkIsDetecting.Value)
            {
                OnDetectionStateChanged?.Invoke(true, newValue);
                
                if (IsOwner)
                {
                    UpdateDetectionUI(true, newValue);
                }
            }
        }

        private void OnRushingStateNetworkChanged(bool previousValue, bool newValue)
        {
            ApplyRushVisualEffects(newValue);

            if (IsOwner)
            {
                UpdateRushUI(newValue);
            }
        }

        #endregion

        #region Skill System Override

        protected override void OnSkillExecutedLocal(SkillType skillType, Vector3? target)
        {
            base.OnSkillExecutedLocal(skillType, target);

            if (IsOwner)
            {
                switch (skillType)
                {
                    case SkillType.Detect:
                        ShowSkillFeedback("Detection activated!");
                        StartDetection();
                        break;
                    case SkillType.FreezeHider:
                        ShowSkillFeedback("Hider frozen!");
                        PlayFreezeEffect(target ?? transform.position);
                        break;
                    case SkillType.Rush:
                        ShowSkillFeedback("Rush activated!");
                        StartRush();
                        break;
                }
            }
        }

        protected override void OnSkillUsageFailedLocal(SkillType skillType, string reason)
        {
            base.OnSkillUsageFailedLocal(skillType, reason);
            
            if (IsOwner)
            {
                ShowSkillErrorMessage($"{skillType}: {reason}");
            }
        }

        protected override void UpdateSkillCooldownUI()
        {
            if (!IsOwner) return;

            foreach (var skill in Skills)
            {
                UpdateSkillCooldownDisplay(skill.Key, skill.Value.GetCooldownTime());
            }
        }

        #endregion

        #region UI Updates

        private void UpdateCaughtCountUI()
        {
            if (!IsOwner) return;
            
            Debug.Log($"[SeekerPlayer] Hiders Caught: {HidersCaught}");
            // SeekerUI.Instance?.UpdateCaughtCount(HidersCaught);
        }

        private void UpdateDetectionUI(bool isDetecting, float radius)
        {
            if (!IsOwner) return;
            
            Debug.Log($"[SeekerPlayer] Detection: {(isDetecting ? "ON" : "OFF")}, Radius: {radius}");
            // SeekerUI.Instance?.UpdateDetectionIndicator(isDetecting, radius);
        }

        private void UpdateDetectedHidersUI(List<IGamePlayer> detectedHiders)
        {
            if (!IsOwner) return;
            
            Debug.Log($"[SeekerPlayer] Detected {detectedHiders.Count} hiders");
            // SeekerUI.Instance?.UpdateDetectedHiders(detectedHiders);
        }

        private void UpdateRushUI(bool isRushing)
        {
            if (!IsOwner) return;
            
            Debug.Log($"[SeekerPlayer] Rush: {(isRushing ? "ON" : "OFF")}");
            // SeekerUI.Instance?.UpdateRushIndicator(isRushing);
        }

        protected override void UpdateHealthUI(float currentHealth, float maxHealth)
        {
            if (!IsOwner) return;
            
            Debug.Log($"[SeekerPlayer] Health: {currentHealth}/{maxHealth}");
            // SeekerUI.Instance?.UpdateHealthBar(currentHealth / maxHealth);
        }

        protected override void UpdateAliveStateUI(bool isAlive)
        {
            if (!IsOwner) return;
            
            if (!isAlive)
            {
                ShowDeathUI();
            }
        }

        private void UpdateSkillCooldownDisplay(SkillType skillType, float remainingCooldown)
        {
            Debug.Log($"[SeekerPlayer] {skillType} cooldown: {remainingCooldown:F1}s");
            // SeekerUI.Instance?.UpdateSkillCooldown(skillType, remainingCooldown);
        }

        #endregion

        #region UI Feedback Methods

        private void ShowCatchSuccessMessage()
        {
            Debug.Log("[SeekerPlayer] Hider caught successfully!");
            // SeekerUI.Instance?.ShowCatchSuccessFeedback();
        }

        private void ShowCatchErrorMessage(string message)
        {
            Debug.LogWarning($"[SeekerPlayer] Catch Error: {message}");
            // SeekerUI.Instance?.ShowErrorMessage(message);
        }

        private void ShowSkillFeedback(string message)
        {
            Debug.Log($"[SeekerPlayer] Skill: {message}");
            // SeekerUI.Instance?.ShowSkillFeedback(message);
        }

        private void ShowSkillErrorMessage(string message)
        {
            Debug.LogWarning($"[SeekerPlayer] Skill Error: {message}");
            // SeekerUI.Instance?.ShowErrorMessage(message);
        }

        private void ShowDeathUI()
        {
            Debug.Log("[SeekerPlayer] Player died - showing death UI");
            // SeekerUI.Instance?.ShowDeathScreen();
        }

        private void ShowSkillHints()
        {
            Debug.Log("[SeekerPlayer] Skills: Q-Detect, R-Freeze, E-Rush");
            // SeekerUI.Instance?.ShowSkillHints("Q - Detection | R - Freeze Hider | E - Rush");
        }

        private void ShowEndGameResults(Role winner)
        {
            string resultMessage = winner == Role.Seeker ? "SEEKERS WIN!" : "HIDERS WIN!";
            Debug.Log($"[SeekerPlayer] {resultMessage}");
            // SeekerUI.Instance?.ShowGameResult(resultMessage, winner == Role.Seeker);
        }

        #endregion

        #region Visual Effects

        private void PlayCatchEffect(Vector3 position)
        {
            Debug.Log($"[SeekerPlayer] Playing catch effect at {position}");
            // EffectManager.Instance?.PlayCatchEffect(position);
        }

        private void PlayFreezeEffect(Vector3 position)
        {
            Debug.Log($"[SeekerPlayer] Playing freeze effect at {position}");
            // EffectManager.Instance?.PlayFreezeEffect(position);
        }

        private void ApplyRushVisualEffects(bool isRushing)
        {
            if (isRushing)
            {
                Debug.Log("[SeekerPlayer] Applying rush visual effects");
                // EffectManager.Instance?.ApplyRushEffect(gameObject);
            }
            else
            {
                Debug.Log("[SeekerPlayer] Removing rush visual effects");
                // EffectManager.Instance?.RemoveRushEffect(gameObject);
            }
        }

        #endregion

        #region Win Condition Checks

        private void CheckWinCondition()
        {
            if (!IsServer) return;

            // Check if all hiders have been caught
            var totalHiders = GameManager?.GetPlayersByRole(Role.Hider)?.Count() ?? 0;
            
            if (networkHidersCaught.Value >= totalHiders && totalHiders > 0)
            {
                GameManager?.TriggerSeekerWin();
            }
        }

        public bool CanWin()
        {
            if (!IsAlive) return false;
            
            var totalHiders = GameManager?.GetPlayersByRole(Role.Hider)?.Count() ?? 0;
            return networkHidersCaught.Value >= totalHiders && totalHiders > 0;
        }

        #endregion

        #region Game Events Override

        public override void OnGameStart()
        {
            Debug.Log($"[SeekerPlayer] Game started for {PlayerName}");

            if (IsOwner)
            {
                // Enable seeker-specific UI
                // SeekerUI.Instance?.ShowSeekerUI(true);
                // SeekerUI.Instance?.UpdateSkillCooldowns(Skills);

                // Show skill hints
                ShowSkillHints();

                // Initialize detection range indicator
                UpdateDetectionUI(false, 0f);
            }

            // Server initialization
            if (IsServer)
            {
                // Reset all network variables
                networkHidersCaught.Value = 0;
                networkIsDetecting.Value = false;
                networkDetectionRadius.Value = 0f;
                networkIsRushing.Value = false;
                
                // Clear server-side tracking
                caughtHiders.Clear();
                hiderCatchTimes.Clear();
                detectedHiders.Clear();

                // Set detection parameters from game settings
                detectionRange = GameManager?.Settings?.seekerDetectionRange ?? 15f;
                catchRange = GameManager?.Settings?.seekerCatchRange ?? 2f;
            }
        }

        public override void OnGameEnd(Role winnerRole)
        {
            Debug.Log($"[SeekerPlayer] Game ended, winner: {winnerRole}");

            if (IsOwner)
            {
                // Handle game end cleanup
                StopDetection();

                // Disable UI
                // SeekerUI.Instance?.ShowSeekerUI(false);

                // Disable controls
                HandleUnRegisterInput();

                // Show end game UI
                ShowEndGameResults(winnerRole);
            }

            // Server cleanup
            if (IsServer)
            {
                // Cancel all recurring invokes
                CancelInvoke();
                
                // Stop all active abilities
                StopDetectionServer();
                if (networkIsRushing.Value)
                {
                    StopRushServer();
                }

                // Reset states
                networkIsDetecting.Value = false;
                networkDetectionRadius.Value = 0f;
                networkIsRushing.Value = false;
            }
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            if (!IsOwner) return;

            // Handle detection input (hold to detect)
            if (detectSkillAction != null && detectSkillAction.IsPressed() && IsAlive)
            {
                if (!IsDetecting && Skills.ContainsKey(SkillType.Detect) && Skills[SkillType.Detect].CanUse)
                {
                    StartDetection();
                }
            }
            else if (IsDetecting)
            {
                StopDetection();
            }

            // Handle proximity-based catching
            CheckForNearbyHiders();
        }

        private void CheckForNearbyHiders()
        {
            if (!IsOwner || !IsAlive) return;

            var nearbyHiders = FindHidersInRange();
            foreach (var hider in nearbyHiders)
            {
                float distance = Vector3.Distance(transform.position, hider.Position);
                if (distance <= catchRange)
                {
                    // Auto-attempt catch when very close
                    AttemptCatchHider(hider);
                    break; // Only catch one at a time
                }
            }
        }

        #endregion

        #region Debug and Logging

        public override string ToString()
        {
            return $"SeekerPlayer[{ClientId}] - Role:{Role}, Alive:{IsAlive}, Caught:{HidersCaught}, Detecting:{IsDetecting}, Rushing:{IsRushing}";
        }

        private void LogCatchAttempt(ulong hiderClientId, bool success)
        {
            LogNetworkState($"Catch attempt on hider {hiderClientId}: {(success ? "SUCCESS" : "FAILED")}");
        }

        private void LogDetectionState(bool detecting)
        {
            LogNetworkState($"Detection {(detecting ? "started" : "stopped")}. Range: {detectionRange}");
        }

        #endregion

        #region OnTrigger Events for Automatic Catching

        private void OnTriggerEnter(Collider other)
        {
            if (!IsOwner || !IsAlive) return;

            var hiderPlayer = other.GetComponent<IGamePlayer>();
            if (hiderPlayer != null && hiderPlayer.Role == Role.Hider && hiderPlayer.IsAlive)
            {
                AttemptCatchHider(hiderPlayer);
            }
        }

        #endregion
    }
}
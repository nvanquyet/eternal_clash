using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

namespace _GAME.Scripts.HideAndSeek.Player.AI
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class Citizen_AIScript : NetworkBehaviour
    {
        #region Enums & Classes

        public enum CitizenState
        {
            Idle,
            Walking,
            GoingToPoint,
            Talking,
            Waving,
            Texting,
            Watering,
            Sitting,
            Dead
        }

        public enum PointType
        {
            Bench,
            FlowerPot,
            ShopCounter,
            SocialSpot,
            PhoneBooth,
            WavingPoint,
            RandomWalk
        }

        [System.Serializable]
        public class PointOfInterest
        {
            public Transform location;
            public PointType type;
            public float interactionRadius = 2f;
            public float attractionWeight = 5f;
            public int maxOccupants = 1;
            [HideInInspector] public int currentOccupants = 0;
            public bool IsAvailable => currentOccupants < maxOccupants && location != null;
        }

        [System.Serializable]
        public class AnimationState
        {
            public CitizenState state;
            public string animationString = "Idle";
        }

        [System.Serializable]
        public class CitizenPersonality
        {
            [Range(0f, 100f)] public float sociability = 50f;
            [Range(0f, 100f)] public float activeness = 50f;
            [Range(0f, 100f)] public float curiosity = 50f;
            public float restTimeMin = 2f;
            public float restTimeMax = 5f;
            public float interactionTimeMin = 3f;
            public float interactionTimeMax = 8f;
            public float walkSpeed = 3.5f;
        }

        #endregion

        #region Inspector Fields

        [Header("=== NETWORK SETTINGS ===")] [SerializeField]
        private bool isServerControlled = true;

        [Header("=== CITIZEN IDENTITY ===")] [SerializeField]
        private string citizenName = "Citizen_01";

        [Header("=== PERSONALITY ===")] [SerializeField]
        private CitizenPersonality personality = new CitizenPersonality();

        [Header("=== ZONES ===")] [SerializeField]
        private float activityZone = 20f;

        [SerializeField] private float socialRadius = 5f;

        [Header("=== OPTIMIZATION ===")] [SerializeField, Tooltip("Update interval khi xa player")]
        private float farUpdateInterval = 5f;

        [SerializeField, Tooltip("Update interval khi gần player")]
        private float nearUpdateInterval = 2f;

        [SerializeField, Tooltip("Khoảng cách LOD")]
        private float lodDistance = 30f;

        [Header("=== ANIMATIONS ===")] [SerializeField]
        private AnimationState[] animationStates;

        [Header("=== DEBUG ===")] [SerializeField]
        private bool logChanges = false;

        #endregion

        #region Private Variables

        private Animator animator;
        private NavMeshAgent navMeshAgent;
        private NetworkAnimator networkAnimator;
        private Vector3 homePosition;

        private CitizenState currentState;
        private NetworkVariable<int> networkState = new NetworkVariable<int>(0);

        private PointOfInterest currentPOI;
        private PointOfInterest previousPOI;
        private Citizen_AIScript conversationPartner;
        private Vector3 targetLocation;

        private float stateTimer;
        private float nextAIUpdateTime;
        private float currentUpdateInterval;
        private bool isInitialized = false;

        // Optimization
        private List<PointOfInterest> cachedPOIs = new List<PointOfInterest>();
        private float poiCacheTime = 0f;
        private const float POI_CACHE_DURATION = 5f;
        private Transform nearestPlayer;
        private float playerDistanceCheckTime = 0f;
        private const float PLAYER_DISTANCE_CHECK_INTERVAL = 1f;

        private static List<Citizen_AIScript> allCitizens = new List<Citizen_AIScript>();
        public static List<Citizen_AIScript> AllCitizens => allCitizens;


        private Dictionary<CitizenState, AnimationState> _dictStateAnimation;

        private Dictionary<CitizenState, AnimationState> DictStateAnimation
        {
            get
            {
                if (_dictStateAnimation == null || _dictStateAnimation.Count == 0)
                {
                    _dictStateAnimation = new Dictionary<CitizenState, AnimationState>();
                    if (animationStates != null && animationStates.Length > 0)
                    {
                        foreach (var state in animationStates)
                        {
                            if (!_dictStateAnimation.ContainsKey(state.state))
                            {
                                _dictStateAnimation.Add(state.state, state);
                            }
                        }
                    }

                    // Fallback: tạo default states nếu không có
                    foreach (CitizenState stateEnum in System.Enum.GetValues(typeof(CitizenState)))
                    {
                        if (!_dictStateAnimation.ContainsKey(stateEnum))
                        {
                            _dictStateAnimation.Add(stateEnum, new AnimationState
                            {
                                state = stateEnum,
                                animationString = stateEnum.ToString()
                            });
                        }
                    }
                }

                return _dictStateAnimation;
            }
        }

        private PointManager pointManager;
        private List<PointOfInterest> privatePoints = new List<PointOfInterest>();

        public PointManager PointManager
        {
            get
            {
                if (pointManager == null) pointManager = PointManager.Instance;
                return pointManager;
            }
        }

        #endregion

        #region Properties

        public CitizenState CurrentState => currentState;
        public string CitizenName => citizenName;
        public bool IsDead => currentState == CitizenState.Dead;

        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if(logChanges) Debug.Log($"[{citizenName}] OnNetworkSpawn: IsServer={IsServer}, IsClient={IsClient}");
            if (IsServer)
            {
                // Server controls AI
                isServerControlled = true;

                // Stagger initial update
                currentUpdateInterval = Random.Range(nearUpdateInterval, farUpdateInterval);
                nextAIUpdateTime = Time.time + Random.Range(0f, 2f);
            }
            else
            {
                // Client: disable AI logic, only visual
                isServerControlled = false;
                if (navMeshAgent) navMeshAgent.enabled = false;
                enabled = true; // Keep enabled for network sync
            }

            // Subscribe to state changes
            networkState.OnValueChanged += OnNetworkStateChanged;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            networkState.OnValueChanged -= OnNetworkStateChanged;
            ReleasePOI();
        }

        private void OnNetworkStateChanged(int oldState, int newState)
        {
            if (!IsServer)
            {
                // Client updates visual based on server state
                currentState = (CitizenState)newState;
                UpdateClientVisuals();
            }
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if(logChanges) Debug.Log($"[{citizenName}] Awake: pos={transform.position}");
            animator = GetComponentInChildren<Animator>();
            navMeshAgent = GetComponent<NavMeshAgent>();
            networkAnimator = GetComponent<NetworkAnimator>();
            homePosition = transform.position;

            if (!animator || !navMeshAgent)
            {
                if(logChanges) Debug.LogError($"[{citizenName}] Missing components!");
                enabled = false;
                return;
            }

            animator.applyRootMotion = false;
            if (navMeshAgent)
            {
                navMeshAgent.stoppingDistance = 0.5f;
            }
        }

        private void OnEnable()
        {
            if (!allCitizens.Contains(this))
                allCitizens.Add(this);
        }

        private void OnDisable()
        {
            allCitizens.Remove(this);
            ReleasePOI();
        }

        private void Start()
        {
            if (IsServer)
            {
                StartCoroutine(InitializeWithDelay());
            }
        }

        private void Update()
        {
            if (!IsServer)
            {
                UpdateClientVisuals();
                return;
            }

            if (!isInitialized || currentState == CitizenState.Dead)
            {
                return;
            }

            // LOD check
            if (Time.time >= playerDistanceCheckTime)
            {
                UpdatePlayerDistance();
                playerDistanceCheckTime = Time.time + PLAYER_DISTANCE_CHECK_INTERVAL;
            }

            // ✅ FIXED: Check destination - Loại bỏ điều kiện velocity
            if (navMeshAgent.enabled && !navMeshAgent.isStopped)
            {
                // Chờ path được tính xong
                if (!navMeshAgent.pathPending)
                {
                    if (navMeshAgent.hasPath)
                    {
                        // Kiểm tra remainingDistance hợp lệ
                        if (!float.IsPositiveInfinity(navMeshAgent.remainingDistance))
                        {
                            // ✅ CHỈ CHECK DISTANCE, KHÔNG CHECK VELOCITY
                            if (navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance + 0.2f)
                            {
                                OnReachedDestination();
                            }
                        }
                    }
                    // Path failed
                    else if (navMeshAgent.pathStatus == NavMeshPathStatus.PathInvalid)
                    {
                        if(logChanges) Debug.LogWarning($"[{citizenName}] Path invalid, returning to idle");
                        SetState(CitizenState.Idle);
                    }
                }
            }

            // State timer
            if (stateTimer > 0f)
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    OnStateTimerExpired();
                }
            }

            // AI update
            if (Time.time >= nextAIUpdateTime)
            {
                DecideNextAction();
                nextAIUpdateTime = Time.time + currentUpdateInterval;
            }
        }

        #endregion

        #region Initialization

        private IEnumerator InitializeWithDelay()
        {
            if(logChanges) Debug.Log($"[{citizenName}] Init start: isOnNavMesh={navMeshAgent.isOnNavMesh}");
            // Đảm bảo NavMeshAgent đã sẵn sàng
            yield return new WaitForEndOfFrame();

            // Kiểm tra NavMesh
            if (!navMeshAgent.isOnNavMesh)
            {
                if(logChanges) Debug.LogWarning($"[{citizenName}] Not on NavMesh!");
                NavMeshHit hit;
                if (NavMesh.SamplePosition(transform.position, out hit, 5f, NavMesh.AllAreas))
                {
                    navMeshAgent.Warp(hit.position);
                }
                else
                {
                    if(logChanges) Debug.LogError($"[{citizenName}] Cannot find valid NavMesh position!");
                    yield break;
                }
            }

            if(logChanges) Debug.Log($"[{citizenName}] Init done. Home={homePosition}");
            yield return new WaitForSeconds(Random.Range(0.1f, 0.5f)); // Giảm delay

            isInitialized = true;
            SetState(CitizenState.Idle);

            // Force trigger first action
            nextAIUpdateTime = Time.time + 0.5f;

            if (logChanges)
                Debug.Log($"[{citizenName}] Initialized at {transform.position} (Server)");
        }

        #endregion

        #region Optimization

        private void UpdatePlayerDistance()
        {
            // Find nearest player (assuming players have specific tag/layer)
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");

            if (players.Length == 0)
            {
                nearestPlayer = null;
                currentUpdateInterval = farUpdateInterval;
                return;
            }

            float nearestDist = float.MaxValue;
            Transform nearest = null;

            foreach (var player in players)
            {
                float dist = Vector3.Distance(transform.position, player.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = player.transform;
                }
            }

            nearestPlayer = nearest;

            // Adjust update interval based on distance
            if (nearestDist < lodDistance)
            {
                currentUpdateInterval = nearUpdateInterval; // Near player: update often
            }
            else
            {
                currentUpdateInterval = farUpdateInterval; // Far from player: update slow
            }
        }

        private List<PointOfInterest> GetCachedPOIs()
        {
            // Refresh cache if expired
            if (Time.time - poiCacheTime > POI_CACHE_DURATION)
            {
                RefreshPOICache();
                poiCacheTime = Time.time;
            }

            return cachedPOIs;
        }

        private void RefreshPOICache()
        {
            cachedPOIs.Clear();

            // From manager
            if (PointManager != null)
            {
                //var available = PointManager.GetAvailablePOIs(transform.position, activityZone);
                var available = PointManager.GetAvailablePOIs(homePosition, activityZone);
                cachedPOIs.AddRange(available);
            }

            // From private list
            foreach (var poi in privatePoints)
            {
                if (poi.IsAvailable)
                {
                    float distance = Vector3.Distance(transform.position, poi.location.position);
                    if (distance <= activityZone)
                    {
                        cachedPOIs.Add(poi);
                    }
                }
            }
        }

        #endregion

        #region AI Decision

        private void DecideNextAction()
        {
            if (currentState == CitizenState.Dead)
                return;

            // Don't interrupt certain states
            if (stateTimer > 0f &&
                (currentState == CitizenState.Talking ||
                 currentState == CitizenState.Watering ||
                 currentState == CitizenState.Sitting))
                return;

            if (currentState != CitizenState.GoingToPoint &&
                currentState != CitizenState.Watering &&
                currentState != CitizenState.Sitting)
            {
                ReleasePOI();
            }

            // Use cached POIs
            List<PointOfInterest> availablePOIs = GetCachedPOIs();
            float randomValue = Random.Range(0f, 100f);

            // 1. POI-based decision
            if (availablePOIs.Count > 0 && randomValue < personality.curiosity)
            {
                PointOfInterest bestPOI = SelectBestPOI(availablePOIs);
                if (bestPOI != null)
                {
                    GoToPOI(bestPOI);
                    return;
                }
            }

            // 2. Social interaction
            if (randomValue < personality.sociability)
            {
                Citizen_AIScript nearby = FindNearbyCitizen();
                if (nearby != null)
                {
                    StartConversation(nearby);
                    return;
                }
                else
                {
                    SetState(CitizenState.Waving);
                    return;
                }
            }

            // 3. Walk around
            if (randomValue < personality.activeness)
            {
                WalkToRandomLocation();
                return;
            }

            // 4. Default idle
            SetState(CitizenState.Idle);
        }

        private PointOfInterest SelectBestPOI(List<PointOfInterest> pois)
        {
            if (pois.Count == 0) return null;

            PointOfInterest best = null;
            float bestScore = 0f;

            foreach (var poi in pois)
            {
                if (poi == previousPOI) continue;
                float distance = Vector3.Distance(transform.position, poi.location.position);
                float distanceFactor = 1f - (distance / activityZone);
                float score = poi.attractionWeight * distanceFactor;

                // Personality modifiers
                if (poi.type == PointType.SocialSpot)
                    score *= (personality.sociability / 50f);
                else if (poi.type == PointType.Bench)
                    score *= (2f - personality.activeness / 50f);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = poi;
                }
            }

            return best;
        }

        private Citizen_AIScript FindNearbyCitizen()
        {
            Citizen_AIScript nearest = null;
            float nearestDistance = socialRadius;

            foreach (var citizen in allCitizens)
            {
                if (citizen == this || citizen.IsDead)
                    continue;

                if (citizen.currentState != CitizenState.Idle &&
                    citizen.currentState != CitizenState.Walking)
                    continue;

                float distance = Vector3.Distance(transform.position, citizen.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = citizen;
                }
            }

            return nearest;
        }

        #endregion

        #region Actions

        private void GoToPOI(PointOfInterest poi)
        {
            if (poi == null || !poi.IsAvailable)
                return;

            ReleasePOI();
            currentPOI = poi;
            currentPOI.currentOccupants++;
            targetLocation = poi.location.position;
            SetState(CitizenState.GoingToPoint);
        }

        private void StartConversation(Citizen_AIScript partner)
        {
            conversationPartner = partner;
            SetState(CitizenState.Talking);
        }

        private void WalkToRandomLocation()
        {
            Vector3 randomPos = GetRandomPositionInZone();
            if (randomPos != Vector3.zero)
            {
                targetLocation = randomPos;
                SetState(CitizenState.Walking);
            }
            else
            {
                SetState(CitizenState.Idle);
            }
        }

        private Vector3 GetRandomPositionInZone()
        {
            for (int i = 0; i < 30; i++) // Tăng số lần thử
            {
                Vector2 randomCircle = Random.insideUnitCircle * activityZone;
                Vector3 targetPos = homePosition + new Vector3(randomCircle.x, 0, randomCircle.y);

                NavMeshHit hit;
                if (NavMesh.SamplePosition(targetPos, out hit, 5f, NavMesh.AllAreas))
                {
                    // Check if path exists
                    NavMeshPath path = new NavMeshPath();
                    if (navMeshAgent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
                    {
                        return hit.position;
                    }
                }
            }

            if(logChanges) Debug.LogWarning($"[{citizenName}] Cannot find valid random position in zone");
            return Vector3.zero;
        }

        private void ReleasePOI()
        {
            if (currentPOI != null)
            {
                currentPOI.currentOccupants = Mathf.Max(0, currentPOI.currentOccupants - 1);
                //Set previousPOI for potential future use
                previousPOI = currentPOI;
                currentPOI = null;
            }
        }

        #endregion

        #region State Management

        private void SetState(CitizenState newState)
        {
            if (!IsServer) return;

            if (currentState == CitizenState.Dead && newState != CitizenState.Dead)
                return;
            if(logChanges) Debug.Log($"[{citizenName}] State change: {currentState} -> {newState}");
            CitizenState previousState = currentState;
            currentState = newState;

            if (previousState != newState)
            {
                OnExitState(previousState);
                OnEnterState(newState);

                // Sync to clients
                networkState.Value = (int)newState;

                if (logChanges)
                    Debug.Log($"[{citizenName}] {previousState} -> {newState}");
            }
        }

        private void OnEnterState(CitizenState state)
        {
            if (!IsServer) return;

            switch (state)
            {
                case CitizenState.Idle:
                    // ✅ Đảm bảo dừng hoàn toàn
                    if (navMeshAgent.enabled)
                    {
                        navMeshAgent.isStopped = true;
                        navMeshAgent.ResetPath();
                    }

                    SafePlayAnimation(CitizenState.Idle);
                    stateTimer = Random.Range(personality.restTimeMin, personality.restTimeMax);
                    break;

                case CitizenState.Walking:
                case CitizenState.GoingToPoint:
                    if (!SetNavMeshDestination(targetLocation))
                    {
                        if(logChanges) Debug.LogWarning($"[{citizenName}] Cannot set destination, going idle");
                        SetState(CitizenState.Idle);
                        return;
                    }

                    SafePlayAnimation(CitizenState.Walking);
                    break;

                case CitizenState.Talking:
                    // ✅ Dừng ngay
                    if (navMeshAgent.enabled)
                    {
                        navMeshAgent.isStopped = true;
                        navMeshAgent.ResetPath();
                    }

                    if (conversationPartner != null)
                    {
                        Vector3 direction = conversationPartner.transform.position - transform.position;
                        direction.y = 0;
                        if (direction.sqrMagnitude > 0.01f)
                            transform.rotation = Quaternion.LookRotation(direction);
                    }

                    SafePlayAnimation(CitizenState.Talking);
                    stateTimer = Random.Range(personality.interactionTimeMin, personality.interactionTimeMax);
                    break;

                case CitizenState.Waving:
                    if (navMeshAgent.enabled)
                    {
                        navMeshAgent.isStopped = true;
                        navMeshAgent.ResetPath();
                    }

                    SafePlayAnimation(CitizenState.Waving);
                    stateTimer = Random.Range(2f, 4f);
                    break;

                case CitizenState.Texting:
                case CitizenState.Watering:
                case CitizenState.Sitting:
                    if (navMeshAgent.enabled)
                    {
                        navMeshAgent.isStopped = true;
                        navMeshAgent.ResetPath();
                    }

                    SafePlayAnimation(state);
                    stateTimer = state == CitizenState.Sitting
                        ? Random.Range(10f, 20f)
                        : Random.Range(personality.interactionTimeMin, personality.interactionTimeMax);
                    break;

                case CitizenState.Dead:
                    if (navMeshAgent.enabled)
                    {
                        navMeshAgent.isStopped = true;
                        navMeshAgent.enabled = false;
                    }

                    ReleasePOI();
                    SafePlayAnimation(CitizenState.Dead);
                    enabled = false;
                    break;
            }
        }


        private void OnExitState(CitizenState state)
        {
            if (state == CitizenState.Talking)
                conversationPartner = null;
        }

        private void OnReachedDestination()
        {
            if(logChanges) Debug.Log($"[{citizenName}] Reached destination at {transform.position}, currentPOI={currentPOI?.type}");

            // ✅ DỪNG NAVMESH NGAY LẬP TỨC
            if (navMeshAgent.enabled)
            {
                navMeshAgent.isStopped = true;
                navMeshAgent.ResetPath(); // Clear path để không còn remainingDistance
                navMeshAgent.velocity = Vector3.zero; // Force stop
            }

            // Look at currentPOI if any
            if (currentPOI != null)
            {
                transform.DOLookAt(currentPOI.location.position, 0.2f);
            }

            if (currentState == CitizenState.GoingToPoint && currentPOI != null)
            {
                switch (currentPOI.type)
                {
                    case PointType.FlowerPot:
                        SetState(CitizenState.Watering);
                        break;
                    case PointType.Bench:
                        SetState(CitizenState.Sitting);
                        break;
                    case PointType.PhoneBooth:
                        SetState(CitizenState.Texting);
                        break;
                    case PointType.SocialSpot:
                        var nearby = FindNearbyCitizen();
                        SetState(nearby != null ? CitizenState.Talking : CitizenState.Waving);
                        break;
                    default:
                        SetState(CitizenState.Idle);
                        break;
                }
            }
            else if (currentState == CitizenState.Walking)
            {
                SetState(CitizenState.Idle);
            }
        }

        private void OnStateTimerExpired()
        {
            DecideNextAction();
        }

        #endregion

        #region Client Visuals

        private void UpdateClientVisuals()
        {
            if (IsServer) return;

            // Client just plays appropriate animations based on synced state
            // NetworkTransform handles position smoothly
            // NetworkAnimator handles animation parameters
        }

        #endregion

        #region Animation

        private void PlayRandomAnimation(AnimationState[] states)
        {
            if (states == null || states.Length == 0) return;
            var state = states[Random.Range(0, states.Length)];
            PlayRandomAnimation(state);
        }

        private void PlayRandomAnimation(AnimationState state)
        {
            if (state == null) return;
            if (!string.IsNullOrEmpty(state.animationString))
                animator.Play(state.animationString);
        }

        private void SafePlayAnimation(CitizenState state)
        {
            if (DictStateAnimation.ContainsKey(state))
            {
                PlayRandomAnimation(DictStateAnimation[state]);
            }
            else
            {
                if (logChanges)
                    Debug.LogWarning($"[{citizenName}] No animation for state {state}");
            }
        }

        private bool SetNavMeshDestination(Vector3 destination)
        {
            if(logChanges) Debug.Log($"[{citizenName}] Try set destination: {destination}");
            if (!navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
            {
                if(logChanges) Debug.LogError($"[{citizenName}] NavMeshAgent not ready!");
                return false;
            }

            // Check if destination is on NavMesh
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(destination, out hit, 2f, NavMesh.AllAreas))
            {
                if(logChanges) Debug.LogWarning($"[{citizenName}] Destination not on NavMesh: {destination}");
                return false;
            }

            navMeshAgent.isStopped = false;
            navMeshAgent.speed = personality.walkSpeed;

            bool success = navMeshAgent.SetDestination(hit.position);

            if (!success)
            {
                if(logChanges) Debug.LogWarning($"[{citizenName}] SetDestination failed for {hit.position}");
            }

            return success;
        }

        #endregion

        #region Public API

        [ServerRpc(RequireOwnership = false)]
        public void KillServerRpc()
        {
            if (currentState != CitizenState.Dead)
            {
                SetState(CitizenState.Dead);
                if (logChanges)
                    Debug.Log($"[{citizenName}] Killed via ServerRpc");
            }
        }

        // Backward compatible
        public void Kill()
        {
            if (IsServer)
            {
                SetState(CitizenState.Dead);
            }
            else
            {
                KillServerRpc();
            }
        }


        public void ClearAll()
        {
            //Todo: Stop everything and reset to initial state
        }
        #endregion
    }
}
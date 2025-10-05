using System.Collections;
using System.Collections.Generic;
using _GAME.Scripts.Networking;
using _GAME.Scripts.UI.Base;
using TMPro;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.UI
{
    /// <summary>
    /// Hiển thị trạng thái game: số lượng Seeker/Hider và kill feed notifications
    /// </summary>
    public class GameStatusHUD : BaseUI
    {
        [Header("Team Count Display")]
        [SerializeField] private TextMeshProUGUI seekersCountText;
        [SerializeField] private TextMeshProUGUI hidersCountText;
        
        [Header("Kill Feed Settings")]
        [SerializeField] private Transform killFeedContainer; // Vertical Layout Group
        [SerializeField] private GameObject killFeedItemPrefab;
        [SerializeField] private int maxKillFeedItems = 5;
        [SerializeField] private float killFeedDuration = 4f;
        [SerializeField] private float fadeInDuration = 0.3f;
        [SerializeField] private float fadeOutDuration = 0.5f;
        [SerializeField] private GameObject arrowRoleSeeker;
        [SerializeField] private GameObject arrowRoleHider;
        
        [Header("Colors")]
        [SerializeField] private Color seekerColor = new Color(0.9f, 0.2f, 0.2f); // Red
        [SerializeField] private Color hiderColor = new Color(0.2f, 0.6f, 1f); // Blue
        [SerializeField] private Color disconnectColor = new Color(0.7f, 0.7f, 0.7f); // Gray
        
        [Header("Optional: Player Name Database")]
        [SerializeField] private bool usePlayerNames = true;
        
        private Queue<KillFeedItem> activeKillFeeds = new Queue<KillFeedItem>();
        private Dictionary<ulong, string> playerNames = new Dictionary<ulong, string>();
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            ValidateComponents();
        }
        
        private void OnDestroy()
        {
            // Unsubscribe from events
            GameEvent.OnRoleAssigned -= OnRoleAssigned;
            GameEvent.OnPlayerKilled -= OnPlayerKilled;
            GameManager.OnPlayersListUpdated -= OnPlayersListUpdated;
            GameEvent.OnGameStarted -= OnGameStarted;
            GameEvent.OnGameEnded -= OnGameEnded;
            
            // Clean up active feeds
            ClearAllKillFeeds();
        }
        
        private void Start()
        {
            GameEvent.OnRoleAssigned += OnRoleAssigned;
            GameEvent.OnPlayerKilled += OnPlayerKilled;
            GameManager.OnPlayersListUpdated += OnPlayersListUpdated;
            GameEvent.OnGameStarted += OnGameStarted;
            GameEvent.OnGameEnded += OnGameEnded;
            gameObject.SetActive(false);
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnRoleAssigned()
        {
            UpdateTeamCounts();
            CachePlayerNames();
            gameObject.SetActive(true);
            
            var isSeeker = GameManager.Instance.GetPlayerRoleWithId(PlayerIdManager.LocalClientId) == Role.Seeker;
            if(isSeeker) arrowRoleSeeker.SetActive(true);
            else arrowRoleHider.SetActive(true);
        }
        
        private void OnGameStarted()
        {
            UpdateTeamCounts();
            ClearAllKillFeeds();
        }
        
        private void OnGameEnded(Role winnerRole)
        {
            // Keep kill feed visible after game ends
        }
        
        private void OnPlayersListUpdated(List<NetworkPlayerData> players)
        {
            UpdateTeamCounts();
        }
        
        private void OnPlayerKilled(ulong killerId, ulong victimId)
        {
            // Update counts
            UpdateTeamCounts();
            
            // Add to kill feed
            AddKillFeedNotification(killerId, victimId);
        }
        
        #endregion
        
        #region Team Count Display
        
        private void UpdateTeamCounts()
        {
            if (GameManager.Instance == null) return;
            
            int aliveSeekers = 0;
            int aliveHiders = 0;
            
            foreach (var player in GameManager.Instance.AllPlayers)
            {
                if (!player.isAlive) continue;
                
                if (player.role == Role.Seeker)
                    aliveSeekers++;
                else if (player.role == Role.Hider)
                    aliveHiders++;
            }
            
            // Update UI
            if (seekersCountText != null)
            {
                seekersCountText.text = $"{aliveSeekers} Seeker{(aliveSeekers != 1 ? "s" : "")}";
                seekersCountText.color = seekerColor;
            }
            
            if (hidersCountText != null)
            {
                hidersCountText.text = $"{aliveHiders} Hider{(aliveHiders != 1 ? "s" : "")}";
                hidersCountText.color = hiderColor;
            }
        }
        
        #endregion
        
        #region Kill Feed System
        
        private void AddKillFeedNotification(ulong killerId, ulong victimId)
        {
            if (killFeedContainer == null || killFeedItemPrefab == null) return;
            
            // Get player data
            Role victimRole = GameManager.Instance.GetPlayerRoleWithId(victimId);
            Role killerRole = GameManager.Instance.GetPlayerRoleWithId(killerId);
            
            string victimName = GetPlayerName(victimId);
            string message = "";
            Color messageColor;
            
            // Generate message based on kill type
            if (victimRole == Role.Hider && killerRole == Role.Seeker)
            {
                message = $"{victimName} was caught!";
                messageColor = seekerColor;
            }
            else if (victimRole == Role.Seeker)
            {
                if (killerRole == Role.Seeker)
                {
                    message = $"{victimName} eliminated (Friendly Fire)";
                }
                else
                {
                    message = $"{victimName} was eliminated!";
                }
                messageColor = seekerColor;
            }
            else if (victimRole == Role.Hider && killerRole == Role.Hider)
            {
                message = $"{victimName} eliminated (Team Kill)";
                messageColor = hiderColor;
            }
            else
            {
                message = $"{victimName} was eliminated";
                messageColor = Color.white;
            }
            
            // Create kill feed item
            CreateKillFeedItem(message, messageColor);
        }
        
        private void CreateKillFeedItem(string message, Color color)
        {
            // Remove oldest if exceeding max
            if (activeKillFeeds.Count >= maxKillFeedItems)
            {
                var oldestItem = activeKillFeeds.Dequeue();
                if (oldestItem != null && oldestItem.gameObject != null)
                {
                    Destroy(oldestItem.gameObject);
                }
            }
            
            // Instantiate new item
            GameObject itemObj = Instantiate(killFeedItemPrefab, killFeedContainer);
            itemObj.transform.SetAsLastSibling(); // Add to bottom
            
            // Setup item
            KillFeedItem item = itemObj.GetComponent<KillFeedItem>();
            if (item == null)
            {
                item = itemObj.AddComponent<KillFeedItem>();
            }
            
            item.Initialize(message, color);
            activeKillFeeds.Enqueue(item);
            item.gameObject.SetActive(true);
            // Start fade routine
            StartCoroutine(KillFeedLifecycleRoutine(item));
        }
        
        private IEnumerator KillFeedLifecycleRoutine(KillFeedItem item)
        {
            if (item == null) yield break;
            
            // Fade in
            yield return item.FadeIn(fadeInDuration);
            
            // Hold
            yield return new WaitForSeconds(killFeedDuration);
            
            // Fade out
            yield return item.FadeOut(fadeOutDuration);
            
            // Remove from queue and destroy
            if (activeKillFeeds.Contains(item))
            {
                // Note: Can't efficiently remove from middle of Queue, 
                // but it will be cleaned up naturally
            }
            
            if (item != null && item.gameObject != null)
            {
                Destroy(item.gameObject);
            }
        }
        
        private void ClearAllKillFeeds()
        {
            StopAllCoroutines();
            
            while (activeKillFeeds.Count > 0)
            {
                var item = activeKillFeeds.Dequeue();
                if (item != null && item.gameObject != null)
                {
                    Destroy(item.gameObject);
                }
            }
        }
        
        #endregion
        
        #region Player Name Management
        
        private void CachePlayerNames()
        {
            playerNames.Clear();
            
            if (!usePlayerNames || GameManager.Instance == null) return;
            
            foreach (var player in GameManager.Instance.AllPlayers)
            {
                // TODO: Get actual player name from NetworkPlayerData
                playerNames[player.clientId] = $"Player {player.clientId}";
            }
        }
        
        private string GetPlayerName(ulong clientId)
        {
            if (usePlayerNames && playerNames.TryGetValue(clientId, out string name))
            {
                return name;
            }
            
            return $"Player {clientId}";
        }
        
        public void UpdatePlayerName(ulong clientId, string playerName)
        {
            playerNames[clientId] = playerName;
        }
        
        #endregion
        
        #region Initialization & Validation
        
        private void ValidateComponents()
        {
            if (seekersCountText == null)
                Debug.LogError("[GameStatusHUD] Seekers Count Text is not assigned!");
            
            if (hidersCountText == null)
                Debug.LogError("[GameStatusHUD] Hiders Count Text is not assigned!");
            
            if (killFeedContainer == null)
                Debug.LogError("[GameStatusHUD] Kill Feed Container is not assigned!");
            
            if (killFeedItemPrefab == null)
                Debug.LogError("[GameStatusHUD] Kill Feed Item Prefab is not assigned!");
        }
        
        #endregion
        
        #region Public API
        
        public void ShowCustomKillFeed(string message, Color color)
        {
            CreateKillFeedItem(message, color);
        }
        
        public void ForceUpdateCounts()
        {
            UpdateTeamCounts();
        }
        
        #endregion
    }
}
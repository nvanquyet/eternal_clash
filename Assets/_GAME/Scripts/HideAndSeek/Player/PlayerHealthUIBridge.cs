using _GAME.Scripts.HideAndSeek.UI;
using _GAME.Scripts.UI.Base;
using Michsky.MUIP;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.HideAndSeek.Player
{
    /// <summary>
    /// Bridge giữa RolePlayer (health system) và Health UI
    /// Chỉ chạy cho local player (IsOwner)
    /// </summary>
    [RequireComponent(typeof(RolePlayer))]
    public class PlayerHealthUIBridge : NetworkBehaviour
    {
        private RolePlayer rolePlayer;
        private PlayerHealthUI healthUI;
        
        void Awake()
        {
            rolePlayer = GetComponent<RolePlayer>();
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            // ✅ CHỈ chạy cho local player
            if (!IsOwner) return;
            
            // ✅ Lấy health slider từ HUD
            // Giả sử bạn có: HUD.Instance.Get<PlayerHealthUI>().healthSlider
            healthUI = HUD.Instance?.GetUI<PlayerHealthUI>(UIType.Health);
            
            // Subscribe health change event từ ADefendable
            if (rolePlayer != null)
            {
                rolePlayer.OnHealthChanged += OnHealthChanged;
                
                // ✅ Init slider lần đầu
                UpdateHealthUI(rolePlayer.CurrentHealth, rolePlayer.MaxHealth);
            }
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            
            if (!IsOwner) return;
            
            // Cleanup
            if (rolePlayer != null)
            {
                rolePlayer.OnHealthChanged -= OnHealthChanged;
            }
        }
        
        private void OnHealthChanged(float currentHealth, float maxHealth)
        {
            UpdateHealthUI(currentHealth, maxHealth);
        }

        private void UpdateHealthUI(float current, float max) => healthUI.UpdateHealth(current, max);
    }
}
using System;
using _GAME.Scripts.UI.Base;
using Michsky.MUIP;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _GAME.Scripts.HideAndSeek.UI
{
    /// <summary>
    /// UI component quản lý health display
    /// Attach vào HUD Canvas
    /// </summary>
    public class PlayerHealthUI : BaseUI
    {
        [SerializeField] private SliderManager healthSlider;
        
        private void Start()
        {
            healthSlider.mainSlider.minValue = 0;
            healthSlider.usePercent = false; // Enabling/disabling percent
            healthSlider.useRoundValue = false; // Show simplifed value
            healthSlider.UpdateUI(); // Updating UI
            
        }


        /// <summary>
        /// Public method để update từ bridge
        /// </summary>
        public void UpdateHealth(float current, float max)
        {
            Debug.Log($"[PlayerHealthUI] UpdateHealth: {current}/{max}");
            if (healthSlider != null)
            {
                healthSlider.mainSlider.maxValue = max;
                healthSlider.mainSlider.value = current;
            }
            healthSlider.UpdateUI(); // Updating UI
        }
    }
}
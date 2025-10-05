using System;
using _GAME.Scripts.Controller;
using Michsky.MUIP;
using UnityEngine;

namespace _GAME.Scripts.UI.Setting
{
    public class AudioSettingsTab : MonoBehaviour
    {
        [Header("Music Controls")]
        [SerializeField] private SwitchManager musicSwitch;
        [SerializeField] private SliderManager musicSlider;
        
        [Header("Sound Controls")]
        [SerializeField] private SwitchManager soundSwitch;
        [SerializeField] private SliderManager soundSlider;
        
        [Header("Settings")]
        [SerializeField] private bool saveOnChange = true;
        
        private bool _isMusicEnabled = true;
        private bool _isSoundEnabled = true;
        
        private void Start()
        {
            InitializeControls();
            SetupListeners();
            LoadSettings();
        }
        
        private void InitializeControls()
        {
            // Setup Music Slider
            if (musicSlider != null)
            {
                musicSlider.mainSlider.minValue = 0f;
                musicSlider.mainSlider.maxValue = 1f;
                musicSlider.usePercent = true;
                musicSlider.useRoundValue = false;
            }
            
            // Setup Sound Slider
            if (soundSlider != null)
            {
                soundSlider.mainSlider.minValue = 0f;
                soundSlider.mainSlider.maxValue = 1f;
                soundSlider.usePercent = true;
                soundSlider.useRoundValue = false;
            }
        }
        
        private void SetupListeners()
        {
            // Music controls
            if (musicSwitch != null)
                musicSwitch.onValueChanged.AddListener(OnMusicSwitchChanged);
                
            if (musicSlider != null)
                musicSlider.mainSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
            
            // Sound controls
            if (soundSwitch != null)
                soundSwitch.onValueChanged.AddListener(OnSoundSwitchChanged);
                
            if (soundSlider != null)
                soundSlider.mainSlider.onValueChanged.AddListener(OnSoundVolumeChanged);
        }
        
        private void OnMusicSwitchChanged(bool isOn)
        {
            _isMusicEnabled = isOn;
            
            if (musicSlider != null)
                musicSlider.mainSlider.interactable = isOn;
            
            if (isOn)
            {
                // Bật lại với volume đã save
                float volume = musicSlider != null ? musicSlider.mainSlider.value : 1f;
                AudioManager.Instance?.SetMusicVolume(volume);
            }
            else
            {
                // Tắt hoàn toàn
                AudioManager.Instance?.SetMusicVolume(0f);
            }
            
            if (saveOnChange)
            {
                AudioManager.Instance?.SetMusicEnabled(isOn);
            }
        }
        
        private void OnSoundSwitchChanged(bool isOn)
        {
            _isSoundEnabled = isOn;
            
            if (soundSlider != null)
                soundSlider.mainSlider.interactable = isOn;
            
            if (isOn)
            {
                // Bật lại với volume đã save
                float volume = soundSlider != null ? soundSlider.mainSlider.value : 1f;
                AudioManager.Instance?.SetSfxVolume(volume);
            }
            else
            {
                // Tắt hoàn toàn
                AudioManager.Instance?.SetSfxVolume(0f);
            }
            
            if (saveOnChange)
            {
                AudioManager.Instance?.SetSfxEnabled(isOn);
            }
        }
        
        private void OnMusicVolumeChanged(float value)
        {
            if (_isMusicEnabled)
            {
                AudioManager.Instance?.SetMusicVolume(value);
            }
            
            if (saveOnChange)
            {
                AudioManager.Instance?.SetMusicVolume(value);
            }
        }
        
        private void OnSoundVolumeChanged(float value)
        {
            if (_isSoundEnabled)
            {
                AudioManager.Instance?.SetSfxVolume(value);
            }
            
            if (saveOnChange)
            {
                AudioManager.Instance?.SetSfxVolume(value);
            }
        }
        
        private void LoadSettings()
        {
            // Load Music settings
            _isMusicEnabled = AudioManager.Instance.IsMusicEnabled();
            float musicVolume =  AudioManager.Instance.GetMusicVolume();
            
            if (musicSwitch != null)
            {
                musicSwitch.isOn = _isMusicEnabled;
                musicSwitch.UpdateUI();
            }
            
            if (musicSlider != null)
            {
                musicSlider.mainSlider.value = musicVolume;
                musicSlider.mainSlider.interactable = _isMusicEnabled;
            }
            
            // Load Sound settings
            _isSoundEnabled = AudioManager.Instance.IsSfxEnabled();
            float soundVolume =  AudioManager.Instance.GetSfxVolume();
            
            if (soundSwitch != null)
            {
                soundSwitch.isOn = _isSoundEnabled;
                soundSwitch.UpdateUI();
            }
            
            if (soundSlider != null)
            {
                soundSlider.mainSlider.value = soundVolume;
                soundSlider.mainSlider.interactable = _isSoundEnabled;
            }
            
            // Apply to AudioManager
            if (_isMusicEnabled)
                AudioManager.Instance?.SetMusicVolume(musicVolume);
            else
                AudioManager.Instance?.SetMusicVolume(0f);
                
            if (_isSoundEnabled)
                AudioManager.Instance?.SetSfxVolume(soundVolume);
            else
                AudioManager.Instance?.SetSfxVolume(0f);
        }
        
        // Public methods để có thể gọi từ bên ngoài
        public void ResetToDefault()
        {
            if (musicSwitch != null)
            {
                musicSwitch.SetOn();
            }
            
            if (soundSwitch != null)
            {
                soundSwitch.SetOn();
            }
            
            if (musicSlider != null)
            {
                musicSlider.mainSlider.value = 1f;
            }
            
            if (soundSlider != null)
            {
                soundSlider.mainSlider.value = 1f;
            }
        }
        
        private void OnDestroy()
        {
            // Music controls
            if (musicSwitch != null)
                musicSwitch.onValueChanged.RemoveListener(OnMusicSwitchChanged);
                
            if (musicSlider != null)
                musicSlider.mainSlider.onValueChanged.RemoveListener(OnMusicVolumeChanged);
            
            // Sound controls
            if (soundSwitch != null)
                soundSwitch.onValueChanged.RemoveListener(OnSoundSwitchChanged);
                
            if (soundSlider != null)
                soundSlider.mainSlider.onValueChanged.RemoveListener(OnSoundVolumeChanged);
        }
    }
}

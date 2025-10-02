using System;
using GAME.Scripts.DesignPattern;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace _GAME.Scripts.Controller
{
    public class AudioManager : SingletonDontDestroy<AudioManager>
    {

        [Header("Background Music")]
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioClip[] menuMusicClips;
        [SerializeField] private AudioClip[] gamePlayMusicClips;
        [SerializeField] private AudioClip[] intenseMusicClips;
    
        [Header("Settings")]
        [SerializeField] private float musicVolume = 0.5f;
        [SerializeField] private float sfxVolume = 0.7f;

        // Keys cho PlayerPrefs
        private const string MUSIC_ENABLED_KEY = "MusicEnabled";
        private const string SFX_ENABLED_KEY = "SFXEnabled";
        private const string MUSIC_VOLUME_KEY = "MusicVolume";
        private const string SFX_VOLUME_KEY = "SFXVolume";

        private bool _musicEnabled = true;
        private bool _sfxEnabled = true;

        protected override void OnAwake()
        {
            base.OnAwake(); 
            InitializeMusicSource();
            LoadSettings();
        }

        private void InitializeMusicSource()
        {
            // Tạo AudioSource cho nhạc nền nếu chưa có
            if (musicSource == null)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
                musicSource.loop = true;
                musicSource.playOnAwake = false;
            }
        }

        private void LoadSettings()
        {
            // Load cài đặt từ PlayerPrefs
            _musicEnabled = PlayerPrefs.GetInt(MUSIC_ENABLED_KEY, 1) == 1;
            _sfxEnabled = PlayerPrefs.GetInt(SFX_ENABLED_KEY, 1) == 1;
            musicVolume = PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, 0.5f);
            sfxVolume = PlayerPrefs.GetFloat(SFX_VOLUME_KEY, 0.7f);

            ApplyMusicSettings();
        }

        private void ApplyMusicSettings()
        {
            musicSource.volume = musicVolume;
            musicSource.mute = !_musicEnabled;
        }

        #region Background Music Controls

        /// <summary>
        /// Phát nhạc nền với AudioClip chỉ định
        /// </summary>
        private void PlayMusic(AudioClip clip, bool loop = true)
        {
            if (clip == null || !_musicEnabled) return;

            musicSource.clip = clip;
            musicSource.loop = loop;
            musicSource.Play();
        }

        /// <summary>
        /// Phát nhạc nền theo index từ mảng musicClips
        /// </summary>
        public void PlayMenuMusic(bool loop = true) => PlayMusic(menuMusicClips, loop);
        public void PlayGamePlayMusic(bool loop = true) => PlayMusic(gamePlayMusicClips, loop);
        
        public void PlayIntenseMusic(bool loop = true) => PlayMusic(intenseMusicClips, loop);

        private void PlayMusic(AudioClip[] clips, bool loop = true)
        {
            Debug.Log($"[AudioManager] PlayMusic from array, clips length: {clips?.Length ?? 0}, musicEnabled: {_musicEnabled}");
            if (clips == null || clips.Length == 0 || !_musicEnabled) return;
            var index = Random.Range(0, clips.Length);
            if(index < 0 || index >= clips.Length) return;
            PlayMusic(clips[index], loop);
        }

        /// <summary>
        /// Dừng nhạc nền
        /// </summary>
        public void StopMusic()
        {
            musicSource.Stop();
        }

        /// <summary>
        /// Pause nhạc nền
        /// </summary>
        public void PauseMusic()
        {
            musicSource.Pause();
        }

        /// <summary>
        /// Resume nhạc nền
        /// </summary>
        public void ResumeMusic()
        {
            if (_musicEnabled)
                musicSource.UnPause();
        }

        /// <summary>
        /// Bật/tắt nhạc nền
        /// </summary>
        public void ToggleMusic()
        {
            _musicEnabled = !_musicEnabled;
            musicSource.mute = !_musicEnabled;
            PlayerPrefs.SetInt(MUSIC_ENABLED_KEY, _musicEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Set trạng thái nhạc nền
        /// </summary>
        public void SetMusicEnabled(bool enabled)
        {
            _musicEnabled = enabled;
            musicSource.mute = !_musicEnabled;
            PlayerPrefs.SetInt(MUSIC_ENABLED_KEY, _musicEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Điều chỉnh âm lượng nhạc nền (0-1)
        /// </summary>
        public void SetMusicVolume(float volume)
        {
            musicVolume = Mathf.Clamp01(volume);
            musicSource.volume = musicVolume;
            PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, musicVolume);
            PlayerPrefs.Save();
        }

        #endregion

        #region Sound Effects Controls

        public enum SfxPlayMode
        {
            OneShot,        // Phát chồng lên nhau (cho phép overlap)
            BlockIfPlaying, // Chặn nếu AudioSource đang phát
            Restart         // Dừng và phát lại từ đầu
        }

        /// <summary>
        /// Phát SFX với AudioSource và AudioClip từ bên ngoài truyền vào
        /// </summary>
        public void PlaySfx(AudioSource source, AudioClip clip, SfxPlayMode mode = SfxPlayMode.OneShot)
        {
            if (!_sfxEnabled || source == null || clip == null) return;

            switch (mode)
            {
                case SfxPlayMode.OneShot:
                    source.PlayOneShot(clip, sfxVolume);
                    break;

                case SfxPlayMode.BlockIfPlaying:
                    if (!source.isPlaying)
                    {
                        source.clip = clip;
                        source.volume = sfxVolume;
                        source.Play();
                    }
                    break;

                case SfxPlayMode.Restart:
                    source.Stop();
                    source.clip = clip;
                    source.volume = sfxVolume;
                    source.Play();
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Phát SFX với volume tùy chỉnh (0-1)
        /// </summary>
        public void PlaySfx(AudioSource source, AudioClip clip, float volumeScale, SfxPlayMode mode = SfxPlayMode.OneShot)
        {
            if (!_sfxEnabled || source == null || clip == null) return;

            float finalVolume = sfxVolume * Mathf.Clamp01(volumeScale);

            switch (mode)
            {
                case SfxPlayMode.OneShot:
                    source.PlayOneShot(clip, finalVolume);
                    break;

                case SfxPlayMode.BlockIfPlaying:
                    if (!source.isPlaying)
                    {
                        source.clip = clip;
                        source.volume = finalVolume;
                        source.Play();
                    }
                    break;

                case SfxPlayMode.Restart:
                    source.Stop();
                    source.clip = clip;
                    source.volume = finalVolume;
                    source.Play();
                    break;
            }
        }

        /// <summary>
        /// Phát SFX tại vị trí 3D trong world (tạo AudioSource tạm thời)
        /// </summary>
        public void PlaySfxAtPoint(AudioClip clip, Vector3 position, float volumeScale = 1f)
        {
            if (!_sfxEnabled || clip == null) return;
            AudioSource.PlayClipAtPoint(clip, position, sfxVolume * Mathf.Clamp01(volumeScale));
        }

        /// <summary>
        /// Bật/tắt sound effects
        /// </summary>
        public void ToggleSfx()
        {
            _sfxEnabled = !_sfxEnabled;
            PlayerPrefs.SetInt(SFX_ENABLED_KEY, _sfxEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Set trạng thái sound effects
        /// </summary>
        public void SetSfxEnabled(bool enabled)
        {
            _sfxEnabled = enabled;
            PlayerPrefs.SetInt(SFX_ENABLED_KEY, _sfxEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Điều chỉnh âm lượng sound effects (0-1)
        /// </summary>
        public void SetSfxVolume(float volume)
        {
            sfxVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(SFX_VOLUME_KEY, sfxVolume);
            PlayerPrefs.Save();
        }

        #endregion

        #region Getters

        public bool IsMusicEnabled() => _musicEnabled;
        public bool IsSfxEnabled() => _sfxEnabled;
        public float GetMusicVolume() => musicVolume;
        public float GetSfxVolume() => sfxVolume;
        public bool IsMusicPlaying() => musicSource.isPlaying;

        #endregion
    }
}
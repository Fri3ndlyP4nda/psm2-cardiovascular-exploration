using System;
using Cardio.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cardio.UI
{
    /// <summary>
    /// Shared settings panel used by both the main menu and the pause menu.
    ///
    /// Options are stored in PlayerPrefs rather than in the save file, because
    /// they belong to the machine, not to the player profile - a lab PC should
    /// keep its own volume and resolution regardless of who signs in.
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        [Header("Controls")]
        [SerializeField] private Slider masterVolumeSlider;
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private Toggle fullscreenToggle;
        [SerializeField] private Toggle invertYToggle;
        [SerializeField] private Button backButton;
        [SerializeField] private Button resetProgressButton;

        [Header("Labels")]
        [SerializeField] private TMP_Text volumeValueLabel;
        [SerializeField] private TMP_Text sensitivityValueLabel;
        [SerializeField] private TMP_Text savePathLabel;

        [Header("Ranges")]
        [SerializeField] private float minSensitivity = 60f;
        [SerializeField] private float maxSensitivity = 500f;

        private Action _onClose;

        private void Awake()
        {
            if (masterVolumeSlider != null) masterVolumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            if (sensitivitySlider != null) sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
            if (fullscreenToggle != null) fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
            if (invertYToggle != null) invertYToggle.onValueChanged.AddListener(OnInvertYChanged);
            if (backButton != null) backButton.onClick.AddListener(Close);
            if (resetProgressButton != null) resetProgressButton.onClick.AddListener(OnResetProgress);
        }

        /// <summary>Applies stored options at startup, even if the panel is never opened.</summary>
        public static void ApplySavedSettings()
        {
            AudioListener.volume = PlayerPrefs.GetFloat(GameConstants.PrefMasterVolume, 0.8f);

            bool fullscreen = PlayerPrefs.GetInt(GameConstants.PrefFullscreen, Screen.fullScreen ? 1 : 0) == 1;
            if (Screen.fullScreen != fullscreen) Screen.fullScreen = fullscreen;
        }

        public void Open(Action onClose = null)
        {
            _onClose = onClose;
            gameObject.SetActive(true);
            LoadValuesIntoControls();
        }

        public void Close()
        {
            PlayerPrefs.Save();
            gameObject.SetActive(false);
            _onClose?.Invoke();
        }

        private void LoadValuesIntoControls()
        {
            if (masterVolumeSlider != null)
            {
                masterVolumeSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(GameConstants.PrefMasterVolume, 0.8f));
                UpdateVolumeLabel(masterVolumeSlider.value);
            }

            if (sensitivitySlider != null)
            {
                float stored = PlayerPrefs.GetFloat(GameConstants.PrefMouseSensitivity, 220f);
                sensitivitySlider.minValue = minSensitivity;
                sensitivitySlider.maxValue = maxSensitivity;
                sensitivitySlider.SetValueWithoutNotify(Mathf.Clamp(stored, minSensitivity, maxSensitivity));
                UpdateSensitivityLabel(sensitivitySlider.value);
            }

            if (fullscreenToggle != null)
                fullscreenToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(GameConstants.PrefFullscreen, Screen.fullScreen ? 1 : 0) == 1);

            if (invertYToggle != null)
                invertYToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(GameConstants.PrefInvertY, 0) == 1);

            if (savePathLabel != null)
                savePathLabel.text = GameManager.Instance?.Save != null ? $"Save file: {GameManager.Instance.Save.SavePath}" : string.Empty;
        }

        private void OnVolumeChanged(float value)
        {
            AudioListener.volume = value;
            PlayerPrefs.SetFloat(GameConstants.PrefMasterVolume, value);
            UpdateVolumeLabel(value);
        }

        private void OnSensitivityChanged(float value)
        {
            PlayerPrefs.SetFloat(GameConstants.PrefMouseSensitivity, value);
            UpdateSensitivityLabel(value);

            // Apply live so the player can feel the change while adjusting.
            var rig = FindAnyObjectByType<Cardio.Player.OrbitCameraRig>();
            if (rig != null) rig.LoadPreferences();
        }

        private void OnFullscreenChanged(bool value)
        {
            Screen.fullScreen = value;
            PlayerPrefs.SetInt(GameConstants.PrefFullscreen, value ? 1 : 0);
        }

        private void OnInvertYChanged(bool value)
        {
            PlayerPrefs.SetInt(GameConstants.PrefInvertY, value ? 1 : 0);
            var rig = FindAnyObjectByType<Cardio.Player.OrbitCameraRig>();
            if (rig != null) rig.LoadPreferences();
        }

        private void OnResetProgress()
        {
            GameManager.Instance?.Save?.ResetProgress();
            Debug.Log("[SettingsPanel] Local progress reset.");
        }

        private void UpdateVolumeLabel(float value)
        {
            if (volumeValueLabel != null) volumeValueLabel.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        private void UpdateSensitivityLabel(float value)
        {
            if (sensitivityValueLabel != null) sensitivityValueLabel.text = Mathf.RoundToInt(value).ToString();
        }
    }
}

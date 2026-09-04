using Musornulsya.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Musornulsya.UI
{
    /// <summary>
    /// Панель настроек. Одна и та же в лобби и в игре: открывается кнопкой,
    /// закрывается кнопкой или щелчком по затемнению вокруг.
    /// </summary>
    public class SettingsPanelUI : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private Button _openButton;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _backdropButton;
        [SerializeField] private Slider _volumeSlider;
        [SerializeField] private Text _volumeValueText;

        private void Awake()
        {
            // Ставим сохранённую громкость: AudioListener пересоздаётся
            // вместе с камерой в каждой сцене и сбрасывается в максимум.
            GameSettings.Apply();

            if (_openButton != null) _openButton.onClick.AddListener(Show);
            if (_closeButton != null) _closeButton.onClick.AddListener(Hide);
            if (_backdropButton != null) _backdropButton.onClick.AddListener(Hide);

            if (_volumeSlider != null)
            {
                _volumeSlider.minValue = 0f;
                _volumeSlider.maxValue = 1f;
                _volumeSlider.SetValueWithoutNotify(GameSettings.Volume);
                _volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            }

            UpdateVolumeLabel(GameSettings.Volume);

            if (_root != null) _root.SetActive(false);
        }

        public bool IsVisible => _root != null && _root.activeSelf;

        public void Show()
        {
            if (_root == null) return;

            // Значение могло измениться в другой сцене — перечитываем.
            if (_volumeSlider != null)
                _volumeSlider.SetValueWithoutNotify(GameSettings.Volume);

            UpdateVolumeLabel(GameSettings.Volume);
            _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void OnVolumeChanged(float value)
        {
            GameSettings.Volume = value;
            UpdateVolumeLabel(value);
        }

        private void UpdateVolumeLabel(float value)
        {
            if (_volumeValueText != null)
                _volumeValueText.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }
    }
}

using UnityEngine;

namespace Musornulsya.Core
{
    /// <summary>
    /// Настройки игрока, живущие между сессиями.
    ///
    /// Громкость применяется через AudioListener.volume — он глобальный,
    /// поэтому отдельных микшеров и ссылок на источники звука не нужно.
    /// </summary>
    public static class GameSettings
    {
        private const string VolumeKey = "musornulsya_volume";
        private const float DefaultVolume = 0.7f;

        private static float _volume = -1f;

        public static float Volume
        {
            get
            {
                if (_volume < 0f)
                    _volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, DefaultVolume));

                return _volume;
            }
            set
            {
                _volume = Mathf.Clamp01(value);

                PlayerPrefs.SetFloat(VolumeKey, _volume);
                PlayerPrefs.Save();

                Apply();
            }
        }

        /// <summary>
        /// Ставит сохранённую громкость. Вызывается при старте каждой сцены:
        /// AudioListener создаётся заново вместе с камерой и сбрасывается в 1.
        /// </summary>
        public static void Apply()
        {
            AudioListener.volume = Volume;
        }
    }
}

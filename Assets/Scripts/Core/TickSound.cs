using UnityEngine;

namespace Musornulsya.Core
{
    /// <summary>
    /// Тиканье часов на обратном отсчёте. Звук синтезируется в коде, а не
    /// грузится файлом: так в проекте не нужен аудиоасset, а WebGL-сборка
    /// не тяжелеет ни на байт.
    /// </summary>
    public static class TickSound
    {
        private const int SampleRate = 44100;

        private static AudioClip _tick;
        private static AudioClip _tock;

        /// <summary>Чередуем два тона — «тик-так» звучит живее одного повтора.</summary>
        public static AudioClip Tick => _tick != null ? _tick : (_tick = Create("Tick", 1250f));
        public static AudioClip Tock => _tock != null ? _tock : (_tock = Create("Tock", 950f));

        /// <summary>
        /// Короткий щелчок: синусоида с резко спадающей громкостью.
        /// Затухание обязательно — без него слышен неприятный «клик» обрыва.
        /// </summary>
        private static AudioClip Create(string name, float frequency)
        {
            const float duration = 0.05f;
            var count = Mathf.RoundToInt(SampleRate * duration);
            var samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                var t = (float)i / SampleRate;
                var envelope = Mathf.Exp(-38f * t);   // резкий спад, как у щелчка
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.5f;
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}

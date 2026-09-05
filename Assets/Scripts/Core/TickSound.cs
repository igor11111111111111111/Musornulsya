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
        private static AudioClip _correct;
        private static AudioClip _wrong;

        /// <summary>Чередуем два тона — «тик-так» звучит живее одного повтора.</summary>
        public static AudioClip Tick => _tick != null ? _tick : (_tick = Create("Tick", 1250f));
        public static AudioClip Tock => _tock != null ? _tock : (_tock = Create("Tock", 950f));

        /// <summary>Верный ответ: короткая восходящая трель.</summary>
        public static AudioClip Correct => _correct != null
            ? _correct
            : (_correct = CreateArpeggio("Correct", new[] { 660f, 880f, 1320f }, 0.09f));

        /// <summary>Неверный ответ: нисходящие низкие тона.</summary>
        public static AudioClip Wrong => _wrong != null
            ? _wrong
            : (_wrong = CreateArpeggio("Wrong", new[] { 320f, 240f }, 0.13f));

        /// <summary>
        /// Последовательность тонов подряд. Каждый затухает к своему концу,
        /// иначе на стыке слышен щелчок обрыва волны.
        /// </summary>
        private static AudioClip CreateArpeggio(string name, float[] frequencies, float noteDuration)
        {
            var perNote = Mathf.RoundToInt(SampleRate * noteDuration);
            var samples = new float[perNote * frequencies.Length];

            for (int n = 0; n < frequencies.Length; n++)
            {
                for (int i = 0; i < perNote; i++)
                {
                    var t = (float)i / SampleRate;

                    // Плавный вход и спад: убирает щелчки на границах нот.
                    var progress = (float)i / perNote;
                    var envelope = Mathf.Sin(progress * Mathf.PI);

                    samples[n * perNote + i] =
                        Mathf.Sin(2f * Mathf.PI * frequencies[n] * t) * envelope * 0.35f;
                }
            }

            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

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

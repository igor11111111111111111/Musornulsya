using UnityEngine;

namespace Musornulsya.Core
{
    /// <summary>
    /// Шрифт интерфейса, общий для кода, который создаёт текст в рантайме.
    ///
    /// Встроенный LegacyRuntime не годится: в нём нет кириллических глифов,
    /// и в WebGL-сборке весь русский текст пропадал — оставались только
    /// латиница и знаки препинания.
    /// </summary>
    public static class UiFont
    {
        private const string ResourcePath = "Fonts/Inter-Regular";

        private static Font _cached;

        public static Font Get()
        {
            if (_cached != null) return _cached;

            _cached = Resources.Load<Font>(ResourcePath);

            if (_cached == null)
            {
                Debug.LogError(
                    $"[UiFont] Не найден Resources/{ResourcePath}.ttf — " +
                    "русский текст не отобразится.");

                _cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return _cached;
        }
    }
}

using UnityEngine;

namespace Musornulsya.Core
{
    /// <summary>
    /// Спрайты интерфейса: скруглённый прямоугольник с вертикальным
    /// градиентом и круг для ручки слайдера.
    ///
    /// Текстуры не создаются в рантайме, а лежат готовыми PNG в Resources:
    /// сгенерированная в памяти текстура не попадает в сохранённую сцену —
    /// спрайт остаётся, а пикселей нет, и рисуется прямоугольник.
    /// Печёт их меню «Musornulsya > Испечь спрайты UI».
    /// </summary>
    public static class UiSprites
    {
        /// <summary>Радиус скругления в пикселях исходной текстуры.</summary>
        public const int Radius = 16;

        /// <summary>Сторона текстуры: два угла плюс запас под середину.</summary>
        public const int Size = Radius * 2 + 8;

        public const string RoundedName = "UiRounded";
        public const string RoundedSoftName = "UiRoundedSoft";
        public const string CircleName = "UiCircle";

        private const string ResourceDir = "UiSprites/";

        private static Sprite _rounded;
        private static Sprite _roundedSoft;
        private static Sprite _circle;

        /// <summary>Кнопки: заметный градиент сверху вниз.</summary>
        public static Sprite Rounded => _rounded != null
            ? _rounded
            : (_rounded = Load(RoundedName));

        /// <summary>Панели и поля ввода: градиент едва заметен.</summary>
        public static Sprite RoundedSoft => _roundedSoft != null
            ? _roundedSoft
            : (_roundedSoft = Load(RoundedSoftName));

        /// <summary>Ручка слайдера.</summary>
        public static Sprite Circle => _circle != null
            ? _circle
            : (_circle = Load(CircleName));

        private static Sprite Load(string name)
        {
            var sprite = Resources.Load<Sprite>(ResourceDir + name);

            if (sprite == null)
            {
                Debug.LogError(
                    $"[UiSprites] Не найден Resources/{ResourceDir}{name}.png — " +
                    "запусти «Musornulsya > Испечь спрайты UI».");
            }

            return sprite;
        }

        // ---- Генерация: вызывается только редакторным запекателем ----

        /// <summary>
        /// Скруглённый прямоугольник. Градиент задан яркостью пикселей:
        /// Image умножает её на свой цвет, поэтому одна текстура
        /// обслуживает кнопки любого оттенка.
        /// </summary>
        public static Texture2D BuildRoundedTexture(float topLift, float bottomDrop)
        {
            var tex = NewTexture(Size, Size);

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var alpha = CornerAlpha(x, y);

                var t = (float)y / (Size - 1);   // 0 внизу, 1 вверху
                var shade = 1f + Mathf.Lerp(-bottomDrop, topLift, t);

                tex.SetPixel(x, y, new Color(shade, shade, shade, alpha));
            }

            tex.Apply();
            return tex;
        }

        public static Texture2D BuildCircleTexture()
        {
            const int size = 64;
            var tex = NewTexture(size, size);

            var centre = (size - 1) * 0.5f;
            var radius = centre - 1f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var dx = x - centre;
                var dy = y - centre;
                var d = Mathf.Sqrt(dx * dx + dy * dy);

                // 1 пиксель сглаживания по краю, иначе окружность рваная
                var alpha = Mathf.Clamp01(radius - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }

            tex.Apply();
            return tex;
        }

        /// <summary>Прозрачность пикселя с учётом скруглённых углов.</summary>
        private static float CornerAlpha(int x, int y)
        {
            // Ближайший центр скругления по каждой оси.
            var cx = Mathf.Clamp(x, Radius, Size - 1 - Radius);
            var cy = Mathf.Clamp(y, Radius, Size - 1 - Radius);

            var dx = x - cx;
            var dy = y - cy;

            if (dx == 0 && dy == 0) return 1f;   // прямая часть

            var distance = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(Radius - distance);
        }

        private static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }
    }
}

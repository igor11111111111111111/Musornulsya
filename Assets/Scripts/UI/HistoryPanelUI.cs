using System.Collections.Generic;
using Musornulsya.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Musornulsya.UI
{
    /// <summary>
    /// Таблица результатов: игроки по вертикали, раунды по горизонтали,
    /// на пересечении — очки цветом. Справа за разделителем итог.
    /// Служит и историей по ходу игры, и финальным табло.
    /// </summary>
    public class HistoryPanelUI : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _winnerText;
        [SerializeField] private RectTransform _gridParent;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _restartButton;
        [SerializeField] private Button _exitButton;

        private static readonly Color ZeroColor = new Color(0.62f, 0.64f, 0.68f);
        private static readonly Color OneColor = new Color(0.58f, 0.86f, 0.62f);
        private static readonly Color TwoColor = new Color(0.36f, 0.95f, 0.45f);
        private static readonly Color HeaderColor = new Color(0.72f, 0.75f, 0.82f);

        /// <summary>Сколько символов имени влезает в колонку игрока.</summary>
        private const int MaxNameLength = 18;

        private readonly List<GameObject> _cells = new List<GameObject>();
        private Font _font;
        private ScrollRect _scroll;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _closeButton.onClick.AddListener(Hide);
            _restartButton.onClick.AddListener(() =>
            {
                GameRoom.Instance?.RestartGame();
                Hide();
            });
            _exitButton.onClick.AddListener(() => RoomConnector.Instance?.Leave());

            _root.SetActive(false);
        }

        public bool IsVisible => _root.activeSelf;

        public void Hide() => _root.SetActive(false);

        /// <summary>
        /// Показывает таблицу. finalView переключает её в режим итогов:
        /// имя победителя и кнопки новой игры вместо кнопки «Закрыть».
        /// </summary>
        public void Show(IReadOnlyList<PlayerState> players, int totalRounds, bool finalView, bool isHost)
        {
            _root.SetActive(true);
            _titleText.text = finalView ? "Итоги игры" : "История раундов";

            _closeButton.gameObject.SetActive(!finalView);
            _restartButton.gameObject.SetActive(finalView && isHost);
            _exitButton.gameObject.SetActive(finalView);

            var sorted = new List<PlayerState>(players);
            sorted.Sort((a, b) => b.Score.CompareTo(a.Score));

            _winnerText.gameObject.SetActive(finalView);
            if (finalView)
            {
                _winnerText.text = sorted.Count > 0
                    ? $"Победитель: {sorted[0].PlayerName.Value} — {sorted[0].Score}"
                    : "Никто не играл";
            }

            BuildGrid(sorted, Mathf.Max(totalRounds, 1));
            StartCoroutine(ScrollToCurrentRound(totalRounds));
        }

        /// <summary>
        /// Подматывает таблицу к текущему раунду. Ждём кадр: до перестроения
        /// layout размеры содержимого ещё нулевые и скролл не сработал бы.
        /// </summary>
        private System.Collections.IEnumerator ScrollToCurrentRound(int totalRounds)
        {
            yield return null;

            if (_scroll == null)
                _scroll = _gridParent.GetComponentInParent<ScrollRect>();

            if (_scroll == null || totalRounds <= 1) yield break;

            var room = GameRoom.Instance;
            var current = room != null ? room.RoundNumber : 0;

            _scroll.horizontalNormalizedPosition = Mathf.Clamp01((float)current / totalRounds);
            _scroll.verticalNormalizedPosition = 1f;
        }

        private void BuildGrid(List<PlayerState> players, int rounds)
        {
            foreach (var cell in _cells) Destroy(cell);
            _cells.Clear();

            // Шапка
            var header = AddRow();
            AddCell(header, "Игрок", HeaderColor, NameColumnWidth, bold: true, alignLeft: true);
            for (int r = 1; r <= rounds; r++)
                AddCell(header, r.ToString(), HeaderColor, RoundColumnWidth, bold: true);
            AddCell(header, "", HeaderColor, SeparatorWidth);
            AddCell(header, "Итого", HeaderColor, TotalColumnWidth, bold: true);

            foreach (var p in players)
            {
                if (p == null) continue;

                var row = AddRow();

                var name = p.PlayerName.Value;
                if (p.IsBot) name += " [бот]";

                // Режем длинное имя: иначе оно наползало на колонку итогов.
                if (name.Length > MaxNameLength)
                    name = name.Substring(0, MaxNameLength - 1) + "…";

                AddCell(row, name, Color.white, NameColumnWidth, alignLeft: true);

                for (int r = 0; r < rounds; r++)
                {
                    var value = r < p.RoundScores.Length ? p.RoundScores[r] : 0;
                    AddCell(row, value.ToString(), ColorForScore(value), RoundColumnWidth,
                        bold: value > 0);
                }

                AddCell(row, "│", new Color(1f, 1f, 1f, 0.25f), SeparatorWidth);
                AddCell(row, p.Score.ToString(), Color.white, TotalColumnWidth, bold: true);
            }
        }

        private const float NameColumnWidth = 210f;
        private const float RoundColumnWidth = 52f;
        private const float SeparatorWidth = 16f;
        private const float TotalColumnWidth = 74f;
        private const float RowHeight = 34f;

        private RectTransform AddRow()
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(_gridParent, false);

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;

            _cells.Add(go);
            return go.GetComponent<RectTransform>();
        }

        private static Color ColorForScore(int value) => value switch
        {
            2 => TwoColor,
            1 => OneColor,
            _ => ZeroColor,
        };

        private void AddCell(RectTransform row, string text, Color color, float width,
            bool bold = false, bool alignLeft = false)
        {
            var go = new GameObject("Cell", typeof(RectTransform));
            go.transform.SetParent(row, false);

            var label = go.AddComponent<Text>();
            label.text = text;
            label.font = _font;
            label.fontSize = 17;
            label.color = color;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.alignment = alignLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = 0;
        }
    }
}

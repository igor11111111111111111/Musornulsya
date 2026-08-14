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

        private readonly List<GameObject> _cells = new List<GameObject>();
        private Font _font;

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
        }

        private void BuildGrid(List<PlayerState> players, int rounds)
        {
            foreach (var cell in _cells) Destroy(cell);
            _cells.Clear();

            var grid = _gridParent.GetComponent<GridLayoutGroup>();
            // Колонки: имя + раунды + разделитель + итог.
            grid.constraintCount = rounds + 3;

            // Шапка
            AddCell("Игрок", HeaderColor, bold: true, alignLeft: true);
            for (int r = 1; r <= rounds; r++)
                AddCell(r.ToString(), HeaderColor, bold: true);
            AddCell("", HeaderColor);
            AddCell("Итого", HeaderColor, bold: true);

            foreach (var p in players)
            {
                if (p == null) continue;

                var name = p.PlayerName.Value;
                if (p.IsBot) name += " [бот]";
                AddCell(name, Color.white, alignLeft: true);

                for (int r = 0; r < rounds; r++)
                {
                    var value = r < p.RoundScores.Length ? p.RoundScores[r] : 0;
                    AddCell(value.ToString(), ColorForScore(value), bold: value > 0);
                }

                AddCell("│", new Color(1f, 1f, 1f, 0.25f));
                AddCell(p.Score.ToString(), Color.white, bold: true);
            }
        }

        private static Color ColorForScore(int value) => value switch
        {
            2 => TwoColor,
            1 => OneColor,
            _ => ZeroColor,
        };

        private void AddCell(string text, Color color, bool bold = false, bool alignLeft = false)
        {
            var go = new GameObject("Cell", typeof(RectTransform));
            go.transform.SetParent(_gridParent, false);

            var label = go.AddComponent<Text>();
            label.text = text;
            label.font = _font;
            label.fontSize = 18;
            label.color = color;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.alignment = alignLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;

            _cells.Add(go);
        }
    }
}

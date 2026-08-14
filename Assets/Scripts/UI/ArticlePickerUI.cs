using System;
using System.Collections.Generic;
using Musornulsya.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Musornulsya.UI
{
    /// <summary>
    /// Полноэкранный выбор статьи для ведущего: слева скролл статей,
    /// справа части выбранной статьи с формулировками.
    /// Игроки этот экран не видят.
    /// </summary>
    public class ArticlePickerUI : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private InputField _searchInput;
        [SerializeField] private RectTransform _articleListParent;
        [SerializeField] private RectTransform _partListParent;
        [SerializeField] private Text _selectedArticleText;
        [SerializeField] private Button _closeButton;

        private readonly List<GameObject> _articleButtons = new List<GameObject>();
        private readonly List<GameObject> _partButtons = new List<GameObject>();

        private Font _font;
        private Action<ArticleRef> _onPicked;
        private string _selectedNumber;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _closeButton.onClick.AddListener(Hide);
            _searchInput.onValueChanged.AddListener(_ => RebuildArticleList());
            _root.SetActive(false);
        }

        public bool IsVisible => _root.activeSelf;

        public void Hide() => _root.SetActive(false);

        public void Show(Action<ArticleRef> onPicked)
        {
            _onPicked = onPicked;
            _root.SetActive(true);
            _searchInput.text = "";
            _selectedNumber = null;
            _selectedArticleText.text = "Выбери статью слева";

            RebuildArticleList();
            ClearParts();
        }

        private void RebuildArticleList()
        {
            foreach (var go in _articleButtons) Destroy(go);
            _articleButtons.Clear();

            if (ArticleDatabase.Instance == null) return;

            var query = _searchInput.text;
            var seen = new HashSet<string>();

            foreach (var a in ArticleDatabase.Instance.AllArticles)
            {
                if (!seen.Add(a.number)) continue;   // статья одна, частей много

                if (!string.IsNullOrWhiteSpace(query))
                {
                    var q = query.Trim().ToLowerInvariant();
                    if (!a.number.Contains(q) && !a.title.ToLowerInvariant().Contains(q))
                        continue;
                }

                var number = a.number;
                var used = ArticleDatabase.Instance.IsArticleFullyUsed(number);

                AddButton(_articleListParent, number, a.title, _articleButtons, used,
                    () => SelectArticle(number));
            }
        }

        private void SelectArticle(string number)
        {
            _selectedNumber = number;
            ClearParts();

            if (ArticleDatabase.Instance == null) return;

            foreach (var a in ArticleDatabase.Instance.AllArticles)
            {
                if (a.number != number) continue;

                _selectedArticleText.text = $"Ст. {a.number} — {a.title}";

                var picked = a;
                var used = ArticleDatabase.Instance.IsUsed(a.Key);

                AddButton(_partListParent, $"ч. {a.part}", Shorten(a.text), _partButtons, used,
                    () =>
                    {
                        _onPicked?.Invoke(picked);
                        Hide();
                    });
            }
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= 90 ? text : text.Substring(0, 90) + "…";
        }

        private void ClearParts()
        {
            foreach (var go in _partButtons) Destroy(go);
            _partButtons.Clear();
        }

        /// <summary>
        /// Элемент списка: слева плашка с номером (она же заменяет иконку —
        /// у статей УК нет осмысленных изображений, а цветной блок с цифрой
        /// одинаково хорошо цепляет взгляд), справа название или формулировка.
        /// Уже разыгранные помечаются приглушённым цветом и пометкой «✓ было».
        /// </summary>
        private void AddButton(RectTransform parent, string badge, string label,
            List<GameObject> registry, bool used, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Item", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = used
                ? new Color(0.14f, 0.15f, 0.18f)
                : new Color(0.18f, 0.19f, 0.24f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 56;
            le.flexibleWidth = 1;

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 14, 8, 8);
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            // Плашка с номером
            var badgeGo = new GameObject("Badge", typeof(RectTransform));
            badgeGo.transform.SetParent(go.transform, false);
            badgeGo.AddComponent<Image>().color = used
                ? new Color(0.24f, 0.25f, 0.3f)
                : new Color(0.36f, 0.55f, 0.92f);

            // minWidth обязателен: без него layout сжимал плашку,
            // когда названию не хватало места, и цифра обрезалась.
            var badgeLe = badgeGo.AddComponent<LayoutElement>();
            badgeLe.minWidth = 78;
            badgeLe.preferredWidth = 78;
            badgeLe.flexibleWidth = 0;
            badgeLe.minHeight = 34;

            // Текст номера кладём БЕЗ растяжки по краям плашки: при растяжке
            // Wrap переносил цифры по символам, и номер обрезался.
            // Здесь текст сам занимает нужную ширину и центрируется.
            var badgeTextGo = new GameObject("Text", typeof(RectTransform));
            badgeTextGo.transform.SetParent(badgeGo.transform, false);

            var badgeText = badgeTextGo.AddComponent<Text>();
            badgeText.text = badge;
            badgeText.font = _font;
            badgeText.fontSize = 17;
            badgeText.fontStyle = FontStyle.Bold;
            badgeText.alignment = TextAnchor.MiddleCenter;
            badgeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            badgeText.verticalOverflow = VerticalWrapMode.Overflow;
            badgeText.color = used ? new Color(0.6f, 0.62f, 0.68f) : Color.white;

            var badgeTextRt = badgeText.rectTransform;
            badgeTextRt.anchorMin = new Vector2(0.5f, 0.5f);
            badgeTextRt.anchorMax = new Vector2(0.5f, 0.5f);
            badgeTextRt.pivot = new Vector2(0.5f, 0.5f);
            badgeTextRt.anchoredPosition = Vector2.zero;
            badgeTextRt.sizeDelta = new Vector2(74, 30);

            // Название или формулировка
            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);

            // Сжимается текст, а не плашка с номером.
            var textLe = textGo.AddComponent<LayoutElement>();
            textLe.minWidth = 0;
            textLe.flexibleWidth = 1;

            var text = CreateLabel(textGo.transform, used ? $"{label}   ✓ было" : label,
                15, TextAnchor.MiddleLeft);
            text.color = used
                ? new Color(0.55f, 0.57f, 0.62f)
                : new Color(0.93f, 0.94f, 0.96f);

            registry.Add(go);
        }

        private Text CreateLabel(Transform parent, string content, int fontSize, TextAnchor anchor,
            float horizontalPadding = 6f)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(horizontalPadding, 2);
            rt.offsetMax = new Vector2(-horizontalPadding, -2);

            return text;
        }
    }
}

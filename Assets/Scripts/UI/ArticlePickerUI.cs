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
                AddButton(_articleListParent, $"Ст. {a.number} — {a.title}", _articleButtons,
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
                AddButton(_partListParent, $"ч. {a.part} — {Shorten(a.text)}", _partButtons,
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

        private void AddButton(RectTransform parent, string label, List<GameObject> registry,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Item", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.18f, 0.19f, 0.24f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 46;
            le.flexibleWidth = 1;

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);

            var text = textGo.AddComponent<Text>();
            text.text = label;
            text.font = _font;
            text.fontSize = 16;
            text.color = new Color(0.93f, 0.94f, 0.96f);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(12, 2);
            rt.offsetMax = new Vector2(-12, -2);

            registry.Add(go);
        }
    }
}

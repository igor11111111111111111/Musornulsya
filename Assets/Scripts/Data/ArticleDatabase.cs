using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Musornulsya.Data
{
    /// <summary>
    /// Загружает УК из Resources/uk_rf.json и раздаёт статьи ведущему.
    /// Живёт между сценами, к сети отношения не имеет.
    /// </summary>
    public class ArticleDatabase : MonoBehaviour
    {
        public static ArticleDatabase Instance { get; private set; }

        private List<ArticleRef> _all = new List<ArticleRef>();
        private readonly HashSet<string> _used = new HashSet<string>();

        public int TotalCount => _all.Count;
        public int UnusedCount => _all.Count - _used.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Load();
        }

        private void Load()
        {
            var asset = Resources.Load<TextAsset>("uk_rf");
            if (asset == null)
            {
                Debug.LogError("[ArticleDatabase] Resources/uk_rf.json не найден");
                return;
            }

            var parsed = JsonUtility.FromJson<ArticleDatabaseJson>(asset.text);
            if (parsed?.articles == null)
            {
                Debug.LogError("[ArticleDatabase] не удалось разобрать uk_rf.json");
                return;
            }

            // Разворачиваем статьи в плоский список «статья + часть»
            _all = parsed.articles
                .Where(a => a.parts != null)
                .SelectMany(a => a.parts.Select(p => new ArticleRef
                {
                    number = a.number,
                    part = p.part,
                    title = a.title,
                    text = p.text,
                }))
                .ToList();

            Debug.Log($"[ArticleDatabase] загружено {_all.Count} статей-частей");
        }

        /// <summary>Случайная статья, ещё не выпадавшая в этой сессии.</summary>
        public bool TryGetRandomUnused(out ArticleRef result)
        {
            var pool = _all.Where(a => !_used.Contains(a.Key)).ToList();
            if (pool.Count == 0)
            {
                result = default;
                return false;
            }

            result = pool[Random.Range(0, pool.Count)];
            _used.Add(result.Key);
            return true;
        }

        /// <summary>Поиск по номеру или названию — для ведущего, который выбирает сам.</summary>
        public List<ArticleRef> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<ArticleRef>();

            query = query.Trim().ToLowerInvariant();
            return _all
                .Where(a => a.number.Contains(query) || a.title.ToLowerInvariant().Contains(query))
                .ToList();
        }

        /// <summary>Найти конкретную статью+часть по ключу "158_2".</summary>
        public bool TryGetByKey(string key, out ArticleRef result)
        {
            foreach (var a in _all)
            {
                if (a.Key == key)
                {
                    result = a;
                    return true;
                }
            }

            result = default;
            return false;
        }

        public void MarkUsed(string key) => _used.Add(key);

        public void ResetUsed() => _used.Clear();
    }
}

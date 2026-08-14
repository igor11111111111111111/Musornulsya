using System;
using System.Collections.Generic;

namespace Musornulsya.Data
{
    [Serializable]
    public class ArticlePart
    {
        public string part;

        /// <summary>Квалифицирующие признаки именно этой части.</summary>
        public string signs;
    }

    [Serializable]
    public class Article
    {
        public string number;
        public string title;

        /// <summary>
        /// Общая диспозиция статьи — то, что относится ко всем частям.
        /// Раньше она ошибочно лежала в тексте части 1.
        /// </summary>
        public string description;

        public List<ArticlePart> parts;
    }

    [Serializable]
    public class ArticleDatabaseJson
    {
        public List<Article> articles;
    }

    /// <summary>Конкретная статья с конкретной частью — то, что выпадает ведущему.</summary>
    public struct ArticleRef
    {
        public string number;
        public string part;
        public string title;

        /// <summary>Общая диспозиция статьи.</summary>
        public string description;

        /// <summary>Признаки этой части.</summary>
        public string signs;

        public bool IsValid => !string.IsNullOrEmpty(number);

        public string ShortLabel => $"Ст. {number} ч. {part}";
        public string FullLabel => $"Ст. {number} ч. {part} — {title}";
        public string Key => $"{number}_{part}";

        /// <summary>
        /// Справка для ведущего: общая диспозиция и признаки этой части.
        /// Наказание не показываем — объясняют деяние, а не срок.
        /// </summary>
        public string FullText
        {
            get
            {
                var text = description ?? "";

                if (!string.IsNullOrEmpty(signs))
                    text += $"\n\nПризнаки: {signs}";

                return text;
            }
        }
    }
}

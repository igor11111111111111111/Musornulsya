using System;
using System.Collections.Generic;

namespace Musornulsya.Data
{
    [Serializable]
    public class ArticlePart
    {
        public string part;
        public string text;
    }

    [Serializable]
    public class Article
    {
        public string number;
        public string title;
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
        public string text;

        public bool IsValid => !string.IsNullOrEmpty(number);

        public string ShortLabel => $"Ст. {number} ч. {part}";
        public string FullLabel => $"Ст. {number} ч. {part} — {title}";
        public string Key => $"{number}_{part}";
    }
}

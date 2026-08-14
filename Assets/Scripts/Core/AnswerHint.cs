using System.Text.RegularExpressions;
using Musornulsya.Data;

namespace Musornulsya.Core
{
    /// <summary>
    /// Подсказка глазу ведущего — вытаскивает из свободного текста номер статьи и части.
    /// Это НЕ автопроверка: последнее слово всегда за ведущим, подсветка только помогает.
    /// Понимает "158 ч.2", "ст. 158 часть 2", "158.2", "158 2".
    /// </summary>
    public static class AnswerHint
    {
        private static readonly Regex NumbersRegex = new Regex(@"\d+", RegexOptions.Compiled);

        public enum MatchLevel
        {
            None,       // ничего не совпало
            Article,    // угадана статья, но не часть
            Full,       // статья и часть
        }

        public static MatchLevel Evaluate(string answer, ArticleRef target)
        {
            if (string.IsNullOrWhiteSpace(answer) || !target.IsValid)
                return MatchLevel.None;

            var matches = NumbersRegex.Matches(answer);
            if (matches.Count == 0)
                return MatchLevel.None;

            var first = matches[0].Value;
            if (first != target.number)
                return MatchLevel.None;

            if (matches.Count >= 2 && matches[1].Value == target.part)
                return MatchLevel.Full;

            return MatchLevel.Article;
        }
    }
}

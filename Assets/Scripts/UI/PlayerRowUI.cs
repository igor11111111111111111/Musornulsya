using Musornulsya.Core;
using Musornulsya.Data;
using Musornulsya.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Musornulsya.UI
{
    /// <summary>Одна строка в таблице ведущего: имя, ответ, кнопки баллов.</summary>
    public class PlayerRowUI : MonoBehaviour
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _answerText;
        [SerializeField] private Text _scoreText;
        [SerializeField] private Button _plus1;
        [SerializeField] private Button _plus2;
        [SerializeField] private GameObject _awardGroup;

        private PlayerState _player;

        private void Awake()
        {
            _plus1.onClick.AddListener(() => Award(1));
            _plus2.onClick.AddListener(() => Award(2));
        }

        private void Award(int points)
        {
            if (_player != null)
                GameRoom.Instance?.AwardPoints(_player, points);
        }

        public void Bind(PlayerState player, bool showAnswer, bool canAward, ArticleRef target)
        {
            _player = player;
            if (player == null) return;

            var name = player.PlayerName.Value;
            _nameText.text = player.IsConnected ? name : $"{name} (не в сети)";
            _nameText.color = player.IsConnected ? Color.white : new Color(1f, 1f, 1f, 0.4f);

            _scoreText.text = player.Score.ToString();

            if (!showAnswer)
            {
                // До Reveal ведущий видит только факт ответа, не текст —
                // так же, как остальные игроки.
                _answerText.text = player.HasAnswered ? "— ответил —" : "...";
                _answerText.color = new Color(1f, 1f, 1f, 0.5f);
            }
            else if (!player.HasAnswered)
            {
                _answerText.text = "(не ответил)";
                _answerText.color = new Color(1f, 1f, 1f, 0.4f);
            }
            else
            {
                _answerText.text = player.Answer.Value;

                // Подсветка — подсказка глазу, а не автопроверка.
                switch (AnswerHint.Evaluate(player.Answer.Value, target))
                {
                    case AnswerHint.MatchLevel.Full:
                        _answerText.color = new Color(0.45f, 0.92f, 0.5f);
                        break;
                    case AnswerHint.MatchLevel.Article:
                        _answerText.color = new Color(0.98f, 0.83f, 0.35f);
                        break;
                    default:
                        _answerText.color = Color.white;
                        break;
                }
            }

            _awardGroup.SetActive(canAward && showAnswer);
        }
    }
}

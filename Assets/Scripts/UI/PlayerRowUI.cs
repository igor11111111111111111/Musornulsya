using Musornulsya.Data;
using Musornulsya.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Musornulsya.UI
{
    /// <summary>
    /// Строка таблицы у ведущего: имя, ответ, начисленные очки.
    /// Баллы считает автомат; ведущий может оспорить и выставить вручную.
    /// </summary>
    public class PlayerRowUI : MonoBehaviour
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _answerText;
        [SerializeField] private Text _scoreText;
        [SerializeField] private Button _plus1;
        [SerializeField] private Button _plus2;
        [SerializeField] private Button _disputeButton;
        [SerializeField] private GameObject _awardGroup;

        private static readonly Color FullMatch = new Color(0.45f, 0.92f, 0.5f);
        private static readonly Color PartMatch = new Color(0.98f, 0.83f, 0.35f);
        private static readonly Color Dimmed = new Color(1f, 1f, 1f, 0.4f);

        private PlayerState _player;

        private void Awake()
        {
            _plus1.onClick.AddListener(() => Award(1));
            _plus2.onClick.AddListener(() => Award(2));
            _disputeButton.onClick.AddListener(Dispute);
        }

        private void Award(int points)
        {
            if (_player != null)
                GameRoom.Instance?.AwardPoints(_player, points);
        }

        private void Dispute()
        {
            if (_player != null)
                GameRoom.Instance?.DisputeScore(_player);
        }

        public void Bind(PlayerState player, bool showAnswer, bool isHost, ArticleRef target)
        {
            _player = player;
            if (player == null) return;

            var name = player.PlayerName.Value;
            if (player.IsBot) name += " [бот]";

            _nameText.text = player.IsConnected ? name : $"{name} (не в сети)";
            _nameText.color = player.IsConnected ? Color.white : Dimmed;

            _scoreText.text = player.Score.ToString();

            BindAnswer(player, showAnswer, target);

            // Кнопки нужны только ведущему и только после раскрытия ответов.
            // Пока подсчёт не оспорен, показываем одну кнопку «Оспорить»;
            // после неё — ручное начисление.
            var canJudge = isHost && showAnswer;
            _awardGroup.SetActive(canJudge);

            if (canJudge)
            {
                _disputeButton.gameObject.SetActive(!player.ScoreOverridden);
                _plus1.gameObject.SetActive(player.ScoreOverridden);
                _plus2.gameObject.SetActive(player.ScoreOverridden);
            }
        }

        private void BindAnswer(PlayerState player, bool showAnswer, ArticleRef target)
        {
            if (!showAnswer)
            {
                // До Reveal виден только факт ответа — иначе можно списать.
                _answerText.text = player.HasAnswered ? "— ответил —" : "...";
                _answerText.color = new Color(1f, 1f, 1f, 0.5f);
                return;
            }

            if (!player.HasAnswered)
            {
                _answerText.text = "(не ответил)";
                _answerText.color = Dimmed;
                return;
            }

            _answerText.text = player.AnswerLabel;

            // Цвет отражает то, что насчитал автомат.
            var points = GameRoom.ScoreAnswer(player, target.number, target.part);
            _answerText.color = points switch
            {
                2 => FullMatch,
                1 => PartMatch,
                _ => Color.white,
            };
        }
    }
}

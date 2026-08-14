using System.Collections.Generic;
using Musornulsya.Core;
using Musornulsya.Data;
using Musornulsya.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Musornulsya.UI
{
    /// <summary>
    /// Главный экран игры. Один и тот же для всех, но панель ведущего
    /// показывается только тому, кто сейчас ведущий.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        [Header("Общее")]
        [SerializeField] private Text _roomCodeText;
        [SerializeField] private Text _roundText;
        [SerializeField] private Text _phaseText;
        [SerializeField] private Button _leaveButton;

        [Header("Панель ведущего")]
        [SerializeField] private GameObject _hostPanel;
        [SerializeField] private Text _articleLabel;
        [SerializeField] private Button _randomButton;
        [SerializeField] private Button _toggleTextButton;
        [SerializeField] private GameObject _articleTextGroup;
        [SerializeField] private Text _articleText;
        [SerializeField] private Button _startRoundButton;
        [SerializeField] private Button _revealButton;
        [SerializeField] private Button _nextRoundButton;

        [Header("Панель игрока")]
        [SerializeField] private GameObject _playerPanel;
        [SerializeField] private InputField _answerInput;
        [SerializeField] private Button _submitButton;
        [SerializeField] private Text _submitStatus;
        [SerializeField] private Text _revealedArticleText;

        [Header("Таблица")]
        [SerializeField] private Transform _rowsParent;

        /// <summary>
        /// Префаб строки грузится из Resources, а не назначается ссылкой в сцене:
        /// сериализованная ссылка на него не переживала пересборку сцены
        /// и молча оставалась пустой.
        /// </summary>
        private const string RowPrefabPath = "PlayerRow";

        private PlayerRowUI _rowPrefab;

        private readonly List<PlayerRowUI> _rows = new List<PlayerRowUI>();
        private ArticleRef _currentArticle;
        private GameRoom _room;
        private bool _subscribed;

        private void Start()
        {
            _rowPrefab = Resources.Load<PlayerRowUI>(RowPrefabPath);
            if (_rowPrefab == null)
            {
                Debug.LogError(
                    $"[GameUI] Не найден Resources/{RowPrefabPath}.prefab — " +
                    "таблица игроков будет пустой.");
            }

            _roomCodeText.text = RoomConnector.Instance != null
                ? $"Код: {RoomConnector.Instance.RoomCode}"
                : "";

            _randomButton.onClick.AddListener(OnRandomArticle);
            _toggleTextButton.onClick.AddListener(OnToggleText);
            _startRoundButton.onClick.AddListener(OnStartRound);
            _revealButton.onClick.AddListener(OnReveal);
            _nextRoundButton.onClick.AddListener(OnStartRound);
            _submitButton.onClick.AddListener(OnSubmit);
            _leaveButton.onClick.AddListener(() => RoomConnector.Instance?.Leave());

            _articleTextGroup.SetActive(false);
            _hostPanel.SetActive(false);
            _playerPanel.SetActive(false);
        }

        private void Update()
        {
            // GameRoom появляется асинхронно: у ведущего — сразу, у остальных
            // только когда объект комнаты доедет по сети.
            if (_room == null)
            {
                _room = GameRoom.Instance;

                // Подстраховка: Instance выставляется в Spawned(), но если этот
                // кадр случился раньше — ищем объект в сцене, иначе клиент
                // остался бы с пустым экраном навсегда.
                if (_room == null)
                    _room = FindAnyObjectByType<GameRoom>();

                if (_room != null && !_subscribed)
                {
                    _room.Changed += Refresh;
                    _subscribed = true;
                    Refresh();
                }
                return;
            }

            // Дешевле, чем городить события на каждое поле PlayerState.
            Refresh();
        }

        private void OnDestroy()
        {
            if (_room != null && _subscribed)
                _room.Changed -= Refresh;
        }

        private void Refresh()
        {
            // Object.IsValid обязателен: при выходе из комнаты объект деспавнится,
            // а читать [Networked]-свойства у деспавненного объекта нельзя —
            // Fusion бросает InvalidOperationException на каждом кадре.
            if (_room == null || _room.Object == null || !_room.Object.IsValid) return;

            var isHost = _room.IsLocalHost;
            var phase = _room.Phase;
            var revealed = phase == RoundPhase.Reveal;

            _hostPanel.SetActive(isHost);
            _playerPanel.SetActive(!isHost);

            // Имя в шапке — сразу видно, какое окно за кого играет.
            var who = LocalPlayerIdentity.PlayerName;
            var stage = _room.RoundNumber > 0 ? $"Раунд {_room.RoundNumber}" : "Лобби";
            _roundText.text = string.IsNullOrEmpty(who) ? stage : $"{stage} — {who}";
            _phaseText.text = phase switch
            {
                RoundPhase.Lobby => "Ждём начала",
                RoundPhase.Answering => "Слушаем ведущего и отвечаем",
                RoundPhase.Reveal => "Ответы открыты",
                _ => "",
            };

            if (isHost) RefreshHost(phase, revealed);
            else RefreshPlayer(phase, revealed);

            RefreshRows(revealed, isHost);
        }

        private void RefreshHost(RoundPhase phase, bool revealed)
        {
            _articleLabel.text = _currentArticle.IsValid
                ? _currentArticle.FullLabel
                : "Нажми «Случайная статья»";

            _articleText.text = _currentArticle.IsValid ? _currentArticle.text : "";

            _randomButton.interactable = phase != RoundPhase.Answering;
            _startRoundButton.gameObject.SetActive(phase == RoundPhase.Lobby);
            _startRoundButton.interactable = _currentArticle.IsValid;

            _revealButton.gameObject.SetActive(phase == RoundPhase.Answering);
            _nextRoundButton.gameObject.SetActive(revealed);
            _nextRoundButton.interactable = true;
        }

        private void RefreshPlayer(RoundPhase phase, bool revealed)
        {
            var answering = phase == RoundPhase.Answering;
            var me = _room.LocalPlayerState;
            var answered = me != null && me.HasAnswered;

            _answerInput.interactable = answering && !answered;
            _submitButton.interactable = answering && !answered
                                          && !string.IsNullOrWhiteSpace(_answerInput.text);

            _submitStatus.text = phase switch
            {
                RoundPhase.Answering when answered => "Ответ принят",
                RoundPhase.Answering => "Пиши статью и часть",
                RoundPhase.Lobby => "Ждём, пока ведущий начнёт",
                RoundPhase.Reveal => "Раунд окончен — ждём следующий",
                _ => "",
            };

            if (revealed && ArticleDatabase.Instance != null
                && ArticleDatabase.Instance.TryGetByKey(_room.RevealedArticleKey.Value, out var a))
            {
                _revealedArticleText.text = $"Правильный ответ: {a.FullLabel}";
                _revealedArticleText.gameObject.SetActive(true);
            }
            else
            {
                _revealedArticleText.gameObject.SetActive(false);
            }
        }

        private void RefreshRows(bool revealed, bool isHost)
        {
            // Отфильтровываем уничтоженные объекты: сортировка по ним
            // бросила бы исключение и оборвала весь Refresh.
            //
            // Заодно пропускаем объекты без имени — это заготовки, которые
            // Fusion успел создать до того, как ведущий заполнил поля.
            // В таблице они выглядели как пустая строка «(не в сети)».
            var players = new List<PlayerState>();
            foreach (var p in GameRoom.Players)
            {
                if (p == null || p.Object == null || !p.Object.IsValid) continue;
                if (string.IsNullOrEmpty(p.PlayerName.Value)) continue;

                // Ведущий загадывает статью, а не отвечает — в таблице ответов
                // ему делать нечего, иначе он висел там с «(не ответил)».
                // Ботов это не касается: у них Owner пустой, как и у ведущего
                // до подключения, поэтому проверяем флаг отдельно.
                if (!p.IsBot && p.Owner == _room.CurrentHostRef) continue;

                players.Add(p);
            }

            // В Reveal сортируем по очкам, иначе по порядку входа —
            // чтобы строки не прыгали, пока игроки отвечают.
            if (revealed)
                players.Sort((a, b) => b.Score.CompareTo(a.Score));
            else
                players.Sort((a, b) => a.JoinOrder.CompareTo(b.JoinOrder));

            if (_rowPrefab == null) return;   // ошибку уже написали в Start

            while (_rows.Count < players.Count)
                _rows.Add(Instantiate(_rowPrefab, _rowsParent));

            for (int i = 0; i < _rows.Count; i++)
            {
                var active = i < players.Count;
                _rows[i].gameObject.SetActive(active);
                if (active)
                    _rows[i].Bind(players[i], revealed, isHost, _currentArticle);
            }
        }

        // ---- Действия ведущего ----

        private void OnRandomArticle()
        {
            if (ArticleDatabase.Instance == null) return;

            if (ArticleDatabase.Instance.TryGetRandomUnused(out var article))
                _currentArticle = article;
            else
                _articleLabel.text = "Статьи закончились";
        }

        private void OnToggleText()
        {
            _articleTextGroup.SetActive(!_articleTextGroup.activeSelf);
        }

        private void OnStartRound()
        {
            if (!_currentArticle.IsValid) OnRandomArticle();
            _articleTextGroup.SetActive(false);

            // Статью передаём для ботов — они отвечают сразу после старта.
            _room?.StartRound(_currentArticle.number, _currentArticle.part);
        }

        private void OnReveal()
        {
            _room?.RevealAnswers(_currentArticle.Key);
        }

        // ---- Действие игрока ----

        private void OnSubmit()
        {
            var me = _room?.LocalPlayerState;
            if (me == null) return;

            me.RPC_SubmitAnswer(_answerInput.text.Trim());
            _answerInput.text = "";
        }
    }
}

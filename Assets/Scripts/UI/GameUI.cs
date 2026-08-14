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
        [SerializeField] private Text _timerText;
        [SerializeField] private Button _historyButton;
        [SerializeField] private Button _leaveButton;

        [Header("Настройка игры")]
        [SerializeField] private GameObject _setupPanel;
        [SerializeField] private InputField _totalRoundsInput;
        [SerializeField] private Button _confirmSetupButton;

        [Header("Панель ведущего")]
        [SerializeField] private GameObject _hostPanel;
        [SerializeField] private Text _articleLabel;
        [SerializeField] private Text _articleText;
        [SerializeField] private Button _randomButton;
        [SerializeField] private Button _pickButton;
        [SerializeField] private Dropdown _durationDropdown;
        [SerializeField] private Button _startRoundButton;
        [SerializeField] private Button _nextRoundButton;

        [Header("Панель игрока")]
        [SerializeField] private GameObject _playerPanel;
        [SerializeField] private InputField _articleInput;
        [SerializeField] private InputField _partInput;
        [SerializeField] private Button _submitButton;
        [SerializeField] private Text _submitStatus;
        [SerializeField] private Text _revealedArticleText;

        [Header("Таблица")]
        [SerializeField] private Transform _rowsParent;

        [Header("Экраны")]
        [SerializeField] private HistoryPanelUI _historyPanel;
        [SerializeField] private ArticlePickerUI _articlePicker;

        /// <summary>
        /// Префаб строки грузится из Resources, а не назначается ссылкой в сцене:
        /// сериализованная ссылка на него не переживала пересборку сцены
        /// и молча оставалась пустой.
        /// </summary>
        private const string RowPrefabPath = "PlayerRow";

        /// <summary>Варианты длительности раунда, секунды.</summary>
        private static readonly int[] Durations = { 15, 30, 60, 90, 120 };
        private const int DefaultDurationIndex = 2;   // 60 секунд

        private PlayerRowUI _rowPrefab;
        private readonly List<PlayerRowUI> _rows = new List<PlayerRowUI>();
        private ArticleRef _currentArticle;
        private GameRoom _room;
        private bool _subscribed;
        private bool _finalShown;

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

            BuildDurationDropdown();

            _randomButton.onClick.AddListener(OnRandomArticle);
            _pickButton.onClick.AddListener(OnPickArticle);
            _startRoundButton.onClick.AddListener(OnStartRound);
            _nextRoundButton.onClick.AddListener(OnNextRound);
            _submitButton.onClick.AddListener(OnSubmit);
            _confirmSetupButton.onClick.AddListener(OnConfirmSetup);
            _historyButton.onClick.AddListener(OnToggleHistory);
            _leaveButton.onClick.AddListener(() => RoomConnector.Instance?.Leave());

            _totalRoundsInput.text = "10";
            _totalRoundsInput.contentType = InputField.ContentType.IntegerNumber;
            _articleInput.contentType = InputField.ContentType.IntegerNumber;
            _partInput.contentType = InputField.ContentType.IntegerNumber;

            _hostPanel.SetActive(false);
            _playerPanel.SetActive(false);
            _setupPanel.SetActive(false);
        }

        private void BuildDurationDropdown()
        {
            _durationDropdown.ClearOptions();

            var options = new List<string>();
            foreach (var d in Durations) options.Add($"{d} сек");

            _durationDropdown.AddOptions(options);
            _durationDropdown.value = DefaultDurationIndex;
        }

        private int SelectedDuration => Durations[Mathf.Clamp(_durationDropdown.value, 0, Durations.Length - 1)];

        /// <summary>
        /// Загаданная статья для подсветки ответов. В обычной игре её выбирает
        /// ведущий вручную, в режиме автопилота — код.
        /// </summary>
        private ArticleRef TargetArticle =>
            _room != null && _room.AutoHostMode ? _room.AutoHostArticle : _currentArticle;

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

            // В режиме автопилота владелец комнаты играет как обычный игрок:
            // раунды ведёт код, а он отвечает наравне с ботами.
            var isHost = _room.IsLocalHostPlaying;
            var phase = _room.Phase;
            var revealed = phase == RoundPhase.Reveal;
            var finished = phase == RoundPhase.Finished;

            // Пока число раундов не задано, ведущий видит только настройку.
            var needsSetup = isHost && _room.TotalRounds <= 0;
            _setupPanel.SetActive(needsSetup);

            _hostPanel.SetActive(isHost && !needsSetup && !finished);
            _playerPanel.SetActive(!isHost && !finished);

            RefreshHeader(phase, finished);

            if (isHost) RefreshHost(phase, revealed);
            else RefreshPlayer(phase, revealed);

            RefreshRows(revealed || finished, isHost);
            RefreshFinal(finished, isHost);
        }

        private void RefreshHeader(RoundPhase phase, bool finished)
        {
            // Имя в шапке — сразу видно, какое окно за кого играет.
            var who = LocalPlayerIdentity.PlayerName;
            var stage = _room.RoundNumber > 0 ? $"Раунд {_room.RoundNumber}" : "Лобби";
            _roundText.text = string.IsNullOrEmpty(who) ? stage : $"{stage} — {who}";

            _phaseText.text = phase switch
            {
                RoundPhase.Lobby => "Ждём начала",
                RoundPhase.Answering => "Слушаем ведущего и отвечаем",
                RoundPhase.Reveal => "Ответы открыты",
                RoundPhase.Finished => "Игра окончена",
                _ => "",
            };

            // Счётчик раундов рядом с кодом комнаты.
            var code = RoomConnector.Instance != null ? RoomConnector.Instance.RoomCode : "";
            var counter = _room.TotalRounds > 0
                ? $"{_room.RoundNumber}/{_room.TotalRounds}"
                : "";
            _roomCodeText.text = string.IsNullOrEmpty(counter)
                ? $"Код: {code}"
                : $"{counter}     Код: {code}";

            // Обратный отсчёт виден всем.
            if (phase == RoundPhase.Answering)
            {
                var left = _room.SecondsLeft;
                _timerText.text = $"{left} сек";
                _timerText.color = left <= 10
                    ? new Color(0.95f, 0.45f, 0.4f)
                    : new Color(0.93f, 0.94f, 0.96f);
            }
            else
            {
                _timerText.text = "";
            }

            _historyButton.gameObject.SetActive(!finished);
        }

        private void RefreshHost(RoundPhase phase, bool revealed)
        {
            _articleLabel.text = _currentArticle.IsValid
                ? _currentArticle.FullLabel
                : "Выбери статью";

            // Формулировка теперь всегда на виду — отдельной кнопки нет.
            _articleText.text = _currentArticle.IsValid ? _currentArticle.text : "";

            var answering = phase == RoundPhase.Answering;

            _randomButton.interactable = !answering;
            _pickButton.interactable = !answering;
            _durationDropdown.interactable = !answering;

            _startRoundButton.gameObject.SetActive(!revealed);
            _startRoundButton.interactable = _currentArticle.IsValid && !answering;

            _nextRoundButton.gameObject.SetActive(revealed);
        }

        private void RefreshPlayer(RoundPhase phase, bool revealed)
        {
            var answering = phase == RoundPhase.Answering;
            var me = _room.LocalPlayerState;
            var answered = me != null && me.HasAnswered;

            var canAnswer = answering && !answered;
            _articleInput.interactable = canAnswer;
            _partInput.interactable = canAnswer;
            _submitButton.interactable = canAnswer && IsAnswerValid();

            _submitStatus.text = phase switch
            {
                RoundPhase.Answering when answered => "Ответ принят",
                RoundPhase.Answering => "Введи номер статьи и часть",
                RoundPhase.Lobby => "Ждём, пока ведущий начнёт",
                RoundPhase.Reveal => "Раунд окончен — ждём следующий",
                _ => "",
            };

            if (revealed && !string.IsNullOrEmpty(_room.RevealedArticleNumber.Value))
            {
                var number = _room.RevealedArticleNumber.Value;
                var part = _room.RevealedArticlePart.Value;

                var title = "";
                if (ArticleDatabase.Instance != null
                    && ArticleDatabase.Instance.TryGetByKey($"{number}_{part}", out var a))
                {
                    title = $" — {a.title}";
                }

                _revealedArticleText.text = $"Правильный ответ: Ст. {number} ч. {part}{title}";
                _revealedArticleText.gameObject.SetActive(true);
            }
            else
            {
                _revealedArticleText.gameObject.SetActive(false);
            }
        }

        /// <summary>Номер статьи обязателен, часть — тоже; оба должны быть числами.</summary>
        private bool IsAnswerValid()
        {
            return int.TryParse(_articleInput.text, out var article) && article > 0
                   && int.TryParse(_partInput.text, out var part) && part > 0;
        }

        private void RefreshRows(bool revealed, bool isHost)
        {
            var players = CollectPlayers();

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
                    _rows[i].Bind(players[i], revealed, isHost, TargetArticle);
            }
        }

        /// <summary>Живые отвечающие: без мусорных объектов и без ведущего.</summary>
        private List<PlayerState> CollectPlayers()
        {
            var players = new List<PlayerState>();

            foreach (var p in GameRoom.Players)
            {
                if (p == null || p.Object == null || !p.Object.IsValid) continue;
                if (string.IsNullOrEmpty(p.PlayerName.Value)) continue;

                // Ведущий загадывает статью, а не отвечает — в таблице ответов
                // ему делать нечего, иначе он висел там с «(не ответил)».
                // Ботов это не касается: у них Owner пустой, как и у ведущего
                // до подключения, поэтому проверяем флаг отдельно.
                //
                // В режиме автопилота исключать некого: ведущего изображает код,
                // а владелец комнаты отвечает как обычный игрок.
                if (!_room.AutoHostMode && !p.IsBot && p.Owner == _room.CurrentHostRef) continue;

                players.Add(p);
            }

            return players;
        }

        private void RefreshFinal(bool finished, bool isHost)
        {
            if (!finished)
            {
                _finalShown = false;
                return;
            }

            // Показываем итоги один раз, иначе панель перестраивалась бы каждый кадр.
            if (_finalShown) return;
            _finalShown = true;

            _historyPanel.Show(CollectPlayers(), _room.TotalRounds, finalView: true, isHost: isHost);
        }

        // ---- Действия ведущего ----

        private void OnConfirmSetup()
        {
            if (!int.TryParse(_totalRoundsInput.text, out var rounds) || rounds < 1)
                rounds = 10;

            _room?.ConfigureGame(rounds);
        }

        private void OnRandomArticle()
        {
            if (ArticleDatabase.Instance == null) return;

            if (ArticleDatabase.Instance.TryGetRandomUnused(out var article))
                _currentArticle = article;
            else
                _articleLabel.text = "Статьи закончились";
        }

        private void OnPickArticle()
        {
            _articlePicker.Show(article =>
            {
                _currentArticle = article;
                ArticleDatabase.Instance?.MarkUsed(article.Key);
            });
        }

        private void OnStartRound()
        {
            if (!_currentArticle.IsValid) OnRandomArticle();
            if (!_currentArticle.IsValid) return;

            _room?.StartRound(_currentArticle.number, _currentArticle.part, SelectedDuration);
        }

        private void OnNextRound()
        {
            if (_room == null) return;

            // Последний раунд — показываем итоги вместо нового раунда.
            if (_room.TotalRounds > 0 && _room.RoundNumber >= _room.TotalRounds)
            {
                _room.NextRoundOrFinish();
                return;
            }

            OnRandomArticle();
            OnStartRound();
        }

        private void OnToggleHistory()
        {
            if (_historyPanel.IsVisible)
            {
                _historyPanel.Hide();
                return;
            }

            _historyPanel.Show(CollectPlayers(), _room.TotalRounds, finalView: false,
                isHost: _room.IsLocalHost);
        }

        // ---- Действие игрока ----

        private void OnSubmit()
        {
            var me = _room?.LocalPlayerState;
            if (me == null || !IsAnswerValid()) return;

            me.RPC_SubmitAnswer(_articleInput.text.Trim(), _partInput.text.Trim());

            _articleInput.text = "";
            _partInput.text = "";
        }
    }
}

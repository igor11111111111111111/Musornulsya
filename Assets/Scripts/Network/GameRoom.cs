using System.Collections.Generic;
using System.Linq;
using Fusion;
using Musornulsya.Core;
using UnityEngine;

namespace Musornulsya.Network
{
    public enum RoundPhase
    {
        Lobby,      // ждём игроков, ведущий ещё не начал
        Answering,  // идёт обратный отсчёт, игроки пишут
        Reveal,     // ответы открыты, баллы посчитаны
        Finished,   // раунды кончились, показываем победителя
    }

    /// <summary>
    /// Центральный объект комнаты. State Authority у того, кто создал комнату,
    /// при его отвале Fusion в Shared Mode мигрирует авторитет автоматически.
    ///
    /// Важно различать:
    ///   HostPersistentId — кто ведущий «по жизни». НЕ меняется при отвале, ждёт возвращения.
    ///   CurrentHostRef   — кто исполняет обязанности прямо сейчас.
    /// </summary>
    public class GameRoom : NetworkBehaviour, IPlayerJoined, IPlayerLeft
    {
        public static GameRoom Instance { get; private set; }

        [SerializeField] private NetworkPrefabRef _playerStatePrefab;

        // ---- Реестр PlayerState, поддерживается локально на каждом клиенте ----
        private static readonly List<PlayerState> _players = new List<PlayerState>();

        /// <summary>Личности, для которых спавн уже запущен, но объект ещё не появился.</summary>
        private readonly HashSet<string> _pendingSpawns = new HashSet<string>();
        public static IReadOnlyList<PlayerState> Players => _players;

        public static void RegisterPlayer(PlayerState p)
        {
            // Объект доехал — отметка о незавершённом спавне больше не нужна,
            // иначе вернувшийся игрок не смог бы подать заявку повторно.
            if (p != null && Instance != null)
                Instance._pendingSpawns.Remove(p.PersistentId.Value);

            // Список статический и переживает смену сцены, поэтому вычищаем
            // уничтоженные объекты от прошлой сессии — иначе в таблице
            // остались бы призраки после выхода и повторного входа.
            _players.RemoveAll(x => x == null);

            if (!_players.Contains(p)) _players.Add(p);
            Instance?.NotifyChanged();
        }

        public static void UnregisterPlayer(PlayerState p)
        {
            _players.Remove(p);
            _players.RemoveAll(x => x == null);
            Instance?.NotifyChanged();
        }

        // ---- Сетевое состояние ----

        [Networked] public NetworkString<_64> HostPersistentId { get; set; }
        [Networked] public PlayerRef CurrentHostRef { get; set; }

        [Networked, OnChangedRender(nameof(NotifyChanged))]
        public RoundPhase Phase { get; set; }

        [Networked, OnChangedRender(nameof(NotifyChanged))]
        public int RoundNumber { get; set; }

        /// <summary>Сколько всего раундов в партии. Задаёт ведущий перед стартом.</summary>
        [Networked, OnChangedRender(nameof(NotifyChanged))]
        public int TotalRounds { get; set; }

        /// <summary>Длительность раунда в секундах — выбирается ведущим.</summary>
        [Networked] public int RoundDuration { get; set; }

        /// <summary>
        /// Таймер раунда. Fusion сам синхронизирует остаток у всех клиентов,
        /// поэтому обратный отсчёт совпадает на каждом экране.
        /// </summary>
        [Networked] public TickTimer RoundTimer { get; set; }

        /// <summary>Загаданная статья. В сеть уходит только при Reveal.</summary>
        [Networked, OnChangedRender(nameof(NotifyChanged))]
        public NetworkString<_16> RevealedArticleNumber { get; set; }

        [Networked, OnChangedRender(nameof(NotifyChanged))]
        public NetworkString<_16> RevealedArticlePart { get; set; }


        [Networked] public int NextJoinOrder { get; set; }

        /// <summary>
        /// Отладка: ведущего изображает автопилот, а живой участник играет
        /// как обычный игрок. Владельцем комнаты он остаётся технически —
        /// без реального клиента раунды запускать некому.
        /// </summary>
        [Networked, OnChangedRender(nameof(NotifyChanged))]
        public bool AutoHostMode { get; set; }

        /// <summary>Ведущий-автопилот сам ведёт игру — панель ведущего не нужна.</summary>
        public bool IsLocalHostPlaying => IsLocalHost && !AutoHostMode;

        /// <summary>Ведущий ли локальный клиент.</summary>
        public bool IsLocalHost => Runner != null && Runner.LocalPlayer == CurrentHostRef;

        /// <summary>Событие для UI — что-то в комнате изменилось.</summary>
        public event System.Action Changed;

        public void NotifyChanged() => Changed?.Invoke();

        public override void Spawned()
        {
            Instance = this;

            // Тот, кто заспавнил комнату, — первый ведущий.
            if (Object.HasStateAuthority && string.IsNullOrEmpty(HostPersistentId.Value))
            {
                HostPersistentId = LocalPlayerIdentity.PersistentId;
                CurrentHostRef = Runner.LocalPlayer;
                Phase = RoundPhase.Lobby;
                RoundNumber = 0;
            }

            NotifyChanged();
        }

        /// <summary>
        /// Заявка на вход повторяется, пока ведущий её не подтвердит.
        /// Одного вызова из Spawned() мало: у присоединившегося клиента объект
        /// комнаты уже есть, но связь с её владельцем может быть ещё не готова,
        /// и такой RPC молча теряется — игрок не появлялся в таблице.
        /// </summary>
        /// <summary>
        /// Обычный Update, а не FixedUpdateNetwork: последний вызывается только
        /// для объектов, которые клиент симулирует. У присоединившегося игрока
        /// GameRoom принадлежит ведущему и не симулируется — заявка на вход
        /// оттуда не уходила никогда, и игрок не появлялся в таблице.
        /// </summary>
        private void Update()
        {
            if (Runner == null || !Runner.IsRunning) return;

            // После деспавна RPC отправлять нельзя.
            if (Object == null || !Object.IsValid) return;

            TickBotSpawn();
            TickBotAnswers();
            TickRoundTimer();
            TickAutoHost();

            if (_joinConfirmed) return;

            // Заявка дошла, когда в комнате есть PlayerState, закреплённый именно
            // за нашим PlayerRef. Сверяться только по PersistentId недостаточно:
            // при совпадении личностей чужой объект был бы принят за свой.
            foreach (var p in _players)
            {
                if (p != null && p.Owner == Runner.LocalPlayer)
                {
                    _joinConfirmed = true;
                    return;
                }
            }

            _retryTimer -= Time.deltaTime;
            if (_retryTimer > 0f) return;
            _retryTimer = JoinRetryInterval;

            RPC_RequestJoin(
                LocalPlayerIdentity.PersistentId,
                LocalPlayerIdentity.PlayerName,
                Runner.LocalPlayer);
        }

        private bool _joinConfirmed;
        private float _retryTimer;

        /// <summary>Пауза между повторами заявки, секунды.</summary>
        private const float JoinRetryInterval = 0.5f;

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
            _players.Clear();

            // Сбрасываем всё, что копилось за сессию: иначе при следующем входе
            // боты досоздавались поверх старых, а разыгранные статьи оставались
            // помеченными в новой игре.
            _botsToSpawn = 0;
            _botsSpawned = 0;
            _botPlan.Clear();
            _pendingSpawns.Clear();
            _joinConfirmed = false;

            Data.ArticleDatabase.Instance?.ResetUsed();
        }

        // ---- Вход и переподключение ----

        /// <summary>
        /// Клиент представляется ведущему. Если PlayerState с таким PersistentId уже есть —
        /// подхватываем его вместе с очками, иначе создаём новый.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestJoin(NetworkString<_64> persistentId, NetworkString<_32> playerName, PlayerRef who)
        {
            var existing = FindByPersistentId(persistentId.Value);

            if (existing != null)
            {
                // Игрок вернулся — очки на месте, обновляем только связь.
                existing.Owner = who;
                existing.IsConnected = true;
                if (!string.IsNullOrEmpty(playerName.Value))
                    existing.PlayerName = playerName;
            }
            else
            {
                // Отсекаем повторные заявки: клиент шлёт их раз в полсекунды,
                // а спавн может быть отложенным — без этой отметки на одного
                // игрока успевало создаться несколько PlayerState.
                if (!_pendingSpawns.Add(persistentId.Value)) return;

                var joinOrder = NextJoinOrder;
                NextJoinOrder = joinOrder + 1;

                // Поля заполняем в onBeforeSpawned — до того, как объект станет
                // видимым остальным. Иначе клиенты успевали увидеть заготовку
                // без имени, и она висела в таблице строкой «(не в сети)».
                Runner.Spawn(
                    _playerStatePrefab,
                    Vector3.zero,
                    Quaternion.identity,
                    Object.StateAuthority,
                    (runner, spawned) =>
                    {
                        var s = spawned.GetComponent<PlayerState>();
                        s.PersistentId = persistentId;
                        s.PlayerName = playerName;
                        s.Owner = who;
                        s.IsConnected = true;
                        s.Score = 0;
                        s.JoinOrder = joinOrder;
                    });

                Debug.Log($"[GameRoom] Игрок принят: {playerName.Value} ({who})");
            }

            // Настоящий ведущий вернулся — забирает роль обратно у и.о.
            if (persistentId.Value == HostPersistentId.Value)
                CurrentHostRef = who;

            NotifyChanged();
        }

        public void PlayerJoined(PlayerRef player)
        {
            NotifyChanged();
        }

        public void PlayerLeft(PlayerRef player)
        {
            if (!Object.HasStateAuthority) return;

            // Помечаем как отключённого — объект и очки остаются.
            var state = _players.FirstOrDefault(p => p != null && p.Owner == player);
            if (state != null)
                state.IsConnected = false;

            // Ведущий ушёл — передаём обязанности старейшему из оставшихся.
            // HostPersistentId НЕ трогаем: он ждёт возвращения настоящего ведущего.
            if (player == CurrentHostRef)
            {
                var stand_in = _players
                    .Where(p => p != null && p.IsConnected)
                    .OrderBy(p => p.JoinOrder)
                    .FirstOrDefault();

                if (stand_in != null)
                    CurrentHostRef = stand_in.Owner;
            }

            NotifyChanged();
        }

        // ---- Отладочные боты ----

        /// <summary>
        /// Включает режим, где ведущего изображает автопилот, а живой участник
        /// играет как обычный игрок: со своим полем ввода и начислением баллов.
        /// </summary>
        public void EnableAutoHost(int totalRounds, int roundDuration)
        {
            if (!IsLocalHost) return;

            AutoHostMode = true;
            TotalRounds = Mathf.Max(1, totalRounds);
            RoundDuration = roundDuration;

            _autoHostDelay = AutoHostPauseSeconds;
            NotifyChanged();
        }

        /// <summary>Пауза между автоматическими раундами, секунды.</summary>
        private const float AutoHostPauseSeconds = 4f;

        private float _autoHostDelay;

        /// <summary>
        /// Автопилот ведущего: выбирает статью, запускает раунд, после показа
        /// ответов выжидает паузу и идёт дальше. Живой участник в это время
        /// отвечает наравне с ботами.
        /// </summary>
        private void TickAutoHost()
        {
            if (!AutoHostMode || !IsLocalHost) return;
            if (Phase == RoundPhase.Finished) return;

            // Раунд идёт — ждём его завершения по таймеру или по ответам.
            if (Phase == RoundPhase.Answering) return;

            _autoHostDelay -= Time.deltaTime;
            if (_autoHostDelay > 0f) return;

            _autoHostDelay = AutoHostPauseSeconds;

            // Раунды кончились — показываем итоги.
            if (TotalRounds > 0 && RoundNumber >= TotalRounds)
            {
                RPC_FinishGame();
                return;
            }

            if (Data.ArticleDatabase.Instance == null) return;
            if (!Data.ArticleDatabase.Instance.TryGetRandomUnused(out var article)) return;

            AutoHostArticle = article;
            StartRound(article.number, article.part, RoundDuration);
        }

        /// <summary>Что загадал автопилот — нужно UI, чтобы подсветить ответы.</summary>
        public Data.ArticleRef AutoHostArticle { get; private set; }

        /// <summary>
        /// Добавляет фейковых игроков, чтобы гонять флоу игры в одиночку.
        /// Сеть они не проверяют — только логику раундов и вёрстку таблицы.
        /// </summary>
        public void AddDebugBots(int count)
        {
            if (!IsLocalHost) return;

            // Ставим в очередь, а не спавним разом: Fusion возвращает null,
            // пока не готов выдать префаб, и вся пачка терялась —
            // в комнате оказывался только первый бот.
            _botsToSpawn += count;
        }

        private int _botsToSpawn;

        /// <summary>Досоздаёт ботов из очереди, по одному за кадр.</summary>
        private void TickBotSpawn()
        {
            if (_botsToSpawn <= 0 || !IsLocalHost) return;

            var joinOrder = NextJoinOrder;

            // Имя говорит, какой исход подсчёта проверяет этот бот —
            // так ошибку в баллах видно прямо в таблице.
            var botName = (_botsSpawned % 4) switch
            {
                0 => "Бот всё верно",
                1 => "Бот статья",
                2 => "Бот часть",
                _ => "Бот мимо",
            };

            // Счётчики двигаем ДО спавна и повторов не делаем.
            // Runner.Spawn возвращает null, даже когда объект успешно поставлен
            // в очередь отложенного создания (EnqueueIncompleteSynchronousSpawns).
            // Прежний код считал это неудачей и спавнил бота заново —
            // первый бот дублировался лишние разы.
            NextJoinOrder = joinOrder + 1;
            _botsSpawned++;
            _botsToSpawn--;

            Runner.Spawn(
                _playerStatePrefab,
                Vector3.zero,
                Quaternion.identity,
                Object.StateAuthority,
                (runner, spawned) =>
                {
                    var s = spawned.GetComponent<PlayerState>();
                    s.PersistentId = $"bot_{joinOrder}";
                    s.PlayerName = botName;
                    s.Owner = PlayerRef.None;   // за ботом нет клиента
                    s.IsConnected = true;
                    s.IsBot = true;
                    s.Score = 0;
                    s.JoinOrder = joinOrder;
                });

            NotifyChanged();
        }

        private int _botsSpawned;

        /// <summary>
        /// Боты отвечают за себя сами — по одному на каждый исход подсчёта:
        ///   1 — всё верно                 → 2 балла
        ///   2 — верна только статья       → 1 балл
        ///   3 — верна только часть        → 0 баллов (часть без статьи не считается)
        ///   4 — всё мимо                  → 0 баллов
        /// Третий бот проверяет неочевидное правило: часть засчитывается
        /// только вместе с угаданной статьёй.
        /// </summary>
        private void AnswerForBots(string articleNumber, string articlePart)
        {
            _botPlan.Clear();

            var index = 0;
            foreach (var p in _players)
            {
                if (p == null || !p.IsBot) continue;

                string answerArticle;
                string answerPart;

                switch (index % 4)
                {
                    case 0:   // всё верно
                        answerArticle = articleNumber;
                        answerPart = articlePart;
                        break;

                    case 1:   // статья верна, часть нет
                        answerArticle = articleNumber;
                        answerPart = articlePart == "1" ? "2" : "1";
                        break;

                    case 2:   // часть верна, статья нет
                        answerArticle = "999";
                        answerPart = articlePart;
                        break;

                    default:  // всё мимо
                        answerArticle = "228";
                        answerPart = articlePart == "1" ? "3" : "1";
                        break;
                }

                // Отвечают не разом, а на 1, 2, 3, 4 секунде: так видно,
                // как таблица заполняется по ходу раунда, и остаётся время
                // ответить самому.
                _botPlan.Add(new BotAnswer
                {
                    Player = p,
                    Article = answerArticle,
                    Part = answerPart,
                    DelaySeconds = index + 1,
                });

                index++;
            }

            _botTimer = 0f;
        }

        private struct BotAnswer
        {
            public PlayerState Player;
            public string Article;
            public string Part;
            public float DelaySeconds;
        }

        private readonly List<BotAnswer> _botPlan = new List<BotAnswer>();
        private float _botTimer;

        /// <summary>Отправляет ответы ботов, когда подходит их срок.</summary>
        private void TickBotAnswers()
        {
            if (!IsLocalHost || Phase != RoundPhase.Answering || _botPlan.Count == 0) return;

            _botTimer += Time.deltaTime;

            for (int i = _botPlan.Count - 1; i >= 0; i--)
            {
                var plan = _botPlan[i];
                if (_botTimer < plan.DelaySeconds) continue;

                if (plan.Player != null && plan.Player.Object != null && plan.Player.Object.IsValid)
                {
                    plan.Player.AnswerArticle = plan.Article;
                    plan.Player.AnswerPart = plan.Part;
                    plan.Player.HasAnswered = true;
                }

                _botPlan.RemoveAt(i);
                NotifyChanged();
            }
        }

        // ---- Управление раундом (только ведущий) ----

        /// <summary>Ведущий задаёт число раундов перед первой игрой.</summary>
        public void ConfigureGame(int totalRounds)
        {
            if (!IsLocalHost) return;
            RPC_ConfigureGame(totalRounds);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_ConfigureGame(int totalRounds)
        {
            TotalRounds = Mathf.Max(1, totalRounds);
            NotifyChanged();
        }

        /// <summary>
        /// Запускает раунд с обратным отсчётом. Статья нужна, чтобы посчитать
        /// баллы при завершении и чтобы за ботов ответить сразу.
        /// В сеть она уходит только на этапе Reveal.
        /// </summary>
        public void StartRound(string articleNumber, string articlePart, int durationSeconds)
        {
            if (!IsLocalHost) return;

            RPC_StartRound(articleNumber, articlePart, durationSeconds);
            AnswerForBots(articleNumber, articlePart);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_StartRound(NetworkString<_16> articleNumber, NetworkString<_16> articlePart,
            int durationSeconds)
        {
            RoundNumber++;
            Phase = RoundPhase.Answering;
            RoundDuration = durationSeconds;
            RoundTimer = TickTimer.CreateFromSeconds(Runner, durationSeconds);

            // Загаданное держим у себя: раскроем в Reveal.
            _pendingArticleNumber = articleNumber.Value;
            _pendingArticlePart = articlePart.Value;

            RevealedArticleNumber = "";
            RevealedArticlePart = "";

            foreach (var p in _players)
            {
                if (p == null) continue;
                p.AnswerArticle = "";
                p.AnswerPart = "";
                p.HasAnswered = false;
                p.ScoreOverridden = false;
            }

            NotifyChanged();
        }

        private string _pendingArticleNumber;
        private string _pendingArticlePart;

        /// <summary>Секунд до конца раунда, для отображения.</summary>
        public int SecondsLeft
        {
            get
            {
                if (Phase != RoundPhase.Answering) return 0;
                var remaining = RoundTimer.RemainingTime(Runner);
                return remaining.HasValue ? Mathf.CeilToInt(remaining.Value) : 0;
            }
        }

        /// <summary>
        /// Следит за концом раунда: время вышло или все ответили.
        /// Работает только у ведущего — он владеет состоянием комнаты.
        /// </summary>
        private void TickRoundTimer()
        {
            if (!IsLocalHost || Phase != RoundPhase.Answering) return;

            var timeUp = RoundTimer.Expired(Runner);
            if (timeUp || EveryoneAnswered())
                FinishRound();
        }

        private bool EveryoneAnswered()
        {
            var anyPlayer = false;

            foreach (var p in _players)
            {
                if (p == null || !p.IsConnected) continue;

                // Ведущий не отвечает — кроме режима автопилота, где он играет
                // наравне со всеми. Без этой оговорки раунд заканчивался бы
                // мгновенно: боты отвечают сразу, а живого никто не ждал.
                if (!AutoHostMode && !p.IsBot && p.Owner == CurrentHostRef) continue;

                anyPlayer = true;
                if (!p.HasAnswered) return false;
            }

            return anyPlayer;
        }

        /// <summary>
        /// Завершает раунд: раскрывает статью и начисляет баллы автоматически —
        /// по +1 за угаданную статью и за угаданную часть.
        /// </summary>
        private void FinishRound()
        {
            RPC_Reveal(_pendingArticleNumber ?? "", _pendingArticlePart ?? "");
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Reveal(NetworkString<_16> articleNumber, NetworkString<_16> articlePart)
        {
            RevealedArticleNumber = articleNumber;
            RevealedArticlePart = articlePart;
            Phase = RoundPhase.Reveal;
            RoundTimer = default;

            // Не успевшие боты молчат — как живой игрок, не уложившийся в время.
            _botPlan.Clear();

            var roundIndex = RoundNumber - 1;

            foreach (var p in _players)
            {
                if (p == null) continue;

                var points = ScoreAnswer(p, articleNumber.Value, articlePart.Value);

                p.Score += points;
                if (roundIndex >= 0 && roundIndex < p.RoundScores.Length)
                    p.RoundScores.Set(roundIndex, points);
            }

            NotifyChanged();
        }

        /// <summary>+1 за верную статью, ещё +1 за верную часть.</summary>
        public static int ScoreAnswer(PlayerState player, string articleNumber, string articlePart)
        {
            if (player == null || !player.HasAnswered) return 0;

            var points = 0;
            if (player.AnswerArticle.Value == articleNumber) points++;
            if (points > 0 && player.AnswerPart.Value == articlePart) points++;

            return points;
        }

        /// <summary>Ведущий переходит к следующему раунду или завершает игру.</summary>
        public void NextRoundOrFinish()
        {
            if (!IsLocalHost) return;

            if (TotalRounds > 0 && RoundNumber >= TotalRounds)
                RPC_FinishGame();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_FinishGame()
        {
            Phase = RoundPhase.Finished;
            NotifyChanged();
        }

        /// <summary>Новая игра с теми же игроками: очки и раунды с нуля.</summary>
        public void RestartGame()
        {
            if (!IsLocalHost) return;
            RPC_RestartGame();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RestartGame()
        {
            RoundNumber = 0;
            Phase = RoundPhase.Lobby;
            RevealedArticleNumber = "";
            RevealedArticlePart = "";
            RoundTimer = default;

            foreach (var p in _players)
            {
                if (p == null) continue;

                p.Score = 0;
                p.AnswerArticle = "";
                p.AnswerPart = "";
                p.HasAnswered = false;
                p.ScoreOverridden = false;

                for (int i = 0; i < p.RoundScores.Length; i++)
                    p.RoundScores.Set(i, 0);
            }

            NotifyChanged();
        }

        /// <summary>
        /// Ведущий оспаривает автоматический подсчёт: очки за раунд обнуляются,
        /// и появляются кнопки ручного начисления.
        /// </summary>
        public void DisputeScore(PlayerState player)
        {
            if (!IsLocalHost || player == null) return;
            RPC_Dispute(player.Object.Id);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Dispute(NetworkId playerObjectId)
        {
            var obj = Runner.FindObject(playerObjectId);
            if (obj == null || !obj.TryGetComponent<PlayerState>(out var state)) return;

            var roundIndex = RoundNumber - 1;
            if (roundIndex < 0 || roundIndex >= state.RoundScores.Length) return;

            // Снимаем то, что начислил автомат, и ждём решения ведущего.
            state.Score -= state.RoundScores[roundIndex];
            state.RoundScores.Set(roundIndex, 0);
            state.ScoreOverridden = true;

            NotifyChanged();
        }

        /// <summary>Ручное начисление после оспаривания.</summary>
        public void AwardPoints(PlayerState player, int points)
        {
            if (!IsLocalHost || player == null) return;
            RPC_Award(player.Object.Id, points);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Award(NetworkId playerObjectId, int points)
        {
            var obj = Runner.FindObject(playerObjectId);
            if (obj == null || !obj.TryGetComponent<PlayerState>(out var state)) return;

            var roundIndex = RoundNumber - 1;
            if (roundIndex < 0 || roundIndex >= state.RoundScores.Length) return;

            // Заменяем прошлое значение за этот раунд, а не добавляем поверх:
            // иначе повторное нажатие накручивало бы счёт.
            state.Score += points - state.RoundScores[roundIndex];
            state.RoundScores.Set(roundIndex, points);

            NotifyChanged();
        }

        // ---- Локальные помощники ----

        public PlayerState FindByPersistentId(string persistentId)
        {
            return _players.FirstOrDefault(p => p != null && p.PersistentId.Value == persistentId);
        }

        /// <summary>
        /// Свой PlayerState ищем по PlayerRef, а не по PersistentId: ссылка на
        /// текущее подключение однозначна, тогда как личности могут совпасть
        /// (например, редактор и билд на одной машине при отладке).
        /// </summary>
        public PlayerState LocalPlayerState =>
            Runner == null
                ? null
                : _players.FirstOrDefault(p => p != null && p.Owner == Runner.LocalPlayer);

        public IEnumerable<PlayerState> SortedByScore =>
            _players.Where(p => p != null).OrderByDescending(p => p.Score).ThenBy(p => p.JoinOrder);
    }
}

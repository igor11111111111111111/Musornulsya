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
        Answering,  // ведущий объясняет голосом, игроки пишут
        Reveal,     // ответы открыты, ведущий раздаёт баллы
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
        public static IReadOnlyList<PlayerState> Players => _players;

        public static void RegisterPlayer(PlayerState p)
        {
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

        /// <summary>Ключ текущей статьи ("158_2"). Заполняется только при Reveal —
        /// до этого игроки не должны знать ответ, а сетевое поле видно всем.</summary>
        [Networked, OnChangedRender(nameof(NotifyChanged))]
        public NetworkString<_16> RevealedArticleKey { get; set; }

        [Networked] public int NextJoinOrder { get; set; }

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
            if (_joinConfirmed || Runner == null || !Runner.IsRunning) return;

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
                var obj = Runner.Spawn(_playerStatePrefab, Vector3.zero, Quaternion.identity, Object.StateAuthority);
                if (obj == null)
                {
                    Debug.LogError(
                        "[GameRoom] Не удалось заспавнить PlayerState — " +
                        "проверь Player State Prefab в префабе GameRoom.");
                    return;
                }

                var state = obj.GetComponent<PlayerState>();
                state.PersistentId = persistentId;
                state.PlayerName = playerName;
                state.Owner = who;
                state.IsConnected = true;
                state.Score = 0;
                state.JoinOrder = NextJoinOrder++;

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

        // ---- Управление раундом (только ведущий) ----

        public void StartRound()
        {
            if (!IsLocalHost) return;
            RPC_StartRound();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_StartRound()
        {
            RoundNumber++;
            Phase = RoundPhase.Answering;
            RevealedArticleKey = "";

            foreach (var p in _players)
            {
                if (p == null) continue;
                p.Answer = "";
                p.HasAnswered = false;
            }

            NotifyChanged();
        }

        /// <summary>Ведущий открывает ответы и раскрывает загаданную статью.</summary>
        public void RevealAnswers(string articleKey)
        {
            if (!IsLocalHost) return;
            RPC_Reveal(articleKey);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Reveal(NetworkString<_16> articleKey)
        {
            RevealedArticleKey = articleKey;
            Phase = RoundPhase.Reveal;
            NotifyChanged();
        }

        /// <summary>Ведущий начисляет игроку баллы.</summary>
        public void AwardPoints(PlayerState player, int points)
        {
            if (!IsLocalHost || player == null) return;
            RPC_Award(player.Object.Id, points);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Award(NetworkId playerObjectId, int points)
        {
            var obj = Runner.FindObject(playerObjectId);
            if (obj != null && obj.TryGetComponent<PlayerState>(out var state))
            {
                state.Score += points;
                NotifyChanged();
            }
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

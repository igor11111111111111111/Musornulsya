using Fusion;
using UnityEngine;

namespace Musornulsya.Network
{
    /// <summary>
    /// Состояние одного игрока. Спавнится ВЕДУЩИМ (владельцем GameRoom), а не самим игроком —
    /// иначе Fusion удалил бы объект при отвале владельца и очки бы обнулились.
    /// Игрок пишет в свои поля через RPC.
    /// </summary>
    public class PlayerState : NetworkBehaviour
    {
        /// <summary>Устойчивый GUID — по нему опознаём вернувшегося игрока.</summary>
        [Networked] public NetworkString<_64> PersistentId { get; set; }

        [Networked] public NetworkString<_32> PlayerName { get; set; }

        /// <summary>Номер статьи, введённый игроком. Виден остальным только после Reveal.</summary>
        [Networked] public NetworkString<_16> AnswerArticle { get; set; }

        /// <summary>Номер части. Отдельным полем, чтобы считать баллы автоматически.</summary>
        [Networked] public NetworkString<_16> AnswerPart { get; set; }

        /// <summary>
        /// Очки по раундам: [0] — первый раунд и так далее.
        /// Нужны для таблицы истории и итогового табло.
        /// </summary>
        [Networked, Capacity(32)] public NetworkArray<int> RoundScores => default;

        /// <summary>Ведущий оспорил автоматический подсчёт — считает вручную.</summary>
        [Networked] public bool ScoreOverridden { get; set; }

        [Networked] public int Score { get; set; }

        [Networked] public bool HasAnswered { get; set; }

        /// <summary>false — игрок отвалился. Объект остаётся, очки сохраняются.</summary>
        [Networked] public bool IsConnected { get; set; }

        /// <summary>Текущий PlayerRef. Меняется при переподключении.</summary>
        [Networked] public PlayerRef Owner { get; set; }

        /// <summary>Порядок входа — по нему выбираем и.о. ведущего, если настоящий отвалился.</summary>
        [Networked] public int JoinOrder { get; set; }

        /// <summary>
        /// Фейковый игрок для отладки: отвечает сам, никакого клиента за ним нет.
        /// В таблице помечается, чтобы не спутать с живым.
        /// </summary>
        [Networked] public bool IsBot { get; set; }

        public bool IsLocal => Runner != null && Owner == Runner.LocalPlayer;

        public override void Spawned()
        {
            GameRoom.RegisterPlayer(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            GameRoom.UnregisterPlayer(this);
        }

        // ---- RPC: игрок → ведущий (State Authority у ведущего, поэтому пишем через него) ----

        /// <summary>Игрок отправляет ответ. Принимает ведущий.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SubmitAnswer(NetworkString<_16> article, NetworkString<_16> part)
        {
            AnswerArticle = article;
            AnswerPart = part;
            HasAnswered = true;
        }

        /// <summary>Ответ целиком, для показа в таблице после Reveal.</summary>
        public string AnswerLabel
        {
            get
            {
                if (!HasAnswered) return "";

                var article = AnswerArticle.Value;
                var part = AnswerPart.Value;

                if (string.IsNullOrEmpty(article)) return "";
                return string.IsNullOrEmpty(part) ? article : $"{article} ч.{part}";
            }
        }

        // Баллы начисляются через GameRoom.RPC_Award — там же, где живёт
        // остальная логика ведущего.
    }
}

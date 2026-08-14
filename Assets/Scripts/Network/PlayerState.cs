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

        /// <summary>Ответ в текущем раунде. Виден остальным только после Reveal.</summary>
        [Networked] public NetworkString<_64> Answer { get; set; }

        [Networked] public int Score { get; set; }

        [Networked] public bool HasAnswered { get; set; }

        /// <summary>false — игрок отвалился. Объект остаётся, очки сохраняются.</summary>
        [Networked] public bool IsConnected { get; set; }

        /// <summary>Текущий PlayerRef. Меняется при переподключении.</summary>
        [Networked] public PlayerRef Owner { get; set; }

        /// <summary>Порядок входа — по нему выбираем и.о. ведущего, если настоящий отвалился.</summary>
        [Networked] public int JoinOrder { get; set; }

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
        public void RPC_SubmitAnswer(NetworkString<_64> answer)
        {
            Answer = answer;
            HasAnswered = true;
        }

        /// <summary>Ведущий начисляет баллы.</summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.StateAuthority)]
        public void RPC_AddScore(int points)
        {
            Score += points;
        }
    }
}

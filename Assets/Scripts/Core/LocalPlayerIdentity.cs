using System;
using UnityEngine;

namespace Musornulsya.Core
{
    /// <summary>
    /// Устойчивая личность игрока — переживает разрыв связи и перезапуск приложения.
    /// PlayerRef при переподключении меняется, поэтому опознаём игрока по этому GUID.
    ///
    /// WebGL: PlayerPrefs пишется в IndexedDB. Переживает перезагрузку вкладки,
    /// но НЕ работает в приватном окне — там игрок каждый раз новый.
    /// </summary>
    public static class LocalPlayerIdentity
    {
        private const string GuidKey = "musornulsya_player_guid";
        private const string NameKey = "musornulsya_player_name";

        private static string _cachedId;

        public static string PersistentId
        {
            get
            {
                if (!string.IsNullOrEmpty(_cachedId))
                    return _cachedId;

                if (!PlayerPrefs.HasKey(GuidKey))
                {
                    PlayerPrefs.SetString(GuidKey, Guid.NewGuid().ToString("N"));
                    PlayerPrefs.Save();
                }

                _cachedId = PlayerPrefs.GetString(GuidKey);
                return _cachedId;
            }
        }

        /// <summary>Имя запоминается между сессиями, чтобы не вводить каждый раз.</summary>
        public static string PlayerName
        {
            get => PlayerPrefs.GetString(NameKey, "");
            set
            {
                PlayerPrefs.SetString(NameKey, value);
                PlayerPrefs.Save();
            }
        }
    }
}

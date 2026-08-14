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
        /// <summary>Имя тоже разделено, иначе при тесте на одной машине
        /// оба клиента подставляли бы одно и то же.</summary>
        private static string NameKey =>
#if UNITY_EDITOR
            "musornulsya_player_name_editor";
#else
            "musornulsya_player_name";
#endif

        /// <summary>
        /// Ключ GUID. В редакторе он свой: редактор и собранный билд на одной
        /// машине делят один файл PlayerPrefs, и с общим ключом оба клиента
        /// получали одинаковую личность — второй считал PlayerState первого
        /// своим и не отправлял заявку на вход.
        /// У разных игроков на разных машинах такой проблемы нет.
        /// </summary>
        private static string GuidKey =>
#if UNITY_EDITOR
            "musornulsya_player_guid_editor";
#else
            "musornulsya_player_guid";
#endif

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

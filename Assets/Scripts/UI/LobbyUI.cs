using Musornulsya.Core;
using Musornulsya.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Musornulsya.UI
{
    /// <summary>Экран входа: имя + создать/присоединиться.</summary>
    public class LobbyUI : MonoBehaviour
    {
        [SerializeField] private InputField _nameInput;
        [SerializeField] private InputField _codeInput;
        [SerializeField] private Button _createButton;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Text _statusText;
        [SerializeField] private Button _debugButton;

        /// <summary>
        /// По одному боту на каждый исход подсчёта: всё верно, только статья,
        /// только часть, всё мимо.
        /// </summary>
        private const int DebugBotCount = 4;

        private void Start()
        {
            _nameInput.text = LocalPlayerIdentity.PlayerName;
            _statusText.text = "";

            _createButton.onClick.AddListener(OnCreate);
            _joinButton.onClick.AddListener(OnJoin);

            if (_debugButton != null)
                _debugButton.onClick.AddListener(OnDebugStart);

            if (RoomConnector.Instance != null)
            {
                RoomConnector.Instance.Failed += OnFailed;
                RoomConnector.Instance.ReturnedToLobby += OnReturnedToLobby;

                // Ошибка могла случиться, пока этой сцены не существовало —
                // показываем её при возврате в лобби.
                if (!string.IsNullOrEmpty(RoomConnector.Instance.LastError))
                {
                    _statusText.text = RoomConnector.Instance.LastError;
                    RoomConnector.Instance.ConsumeLastError();
                }
            }
        }

        private void OnDestroy()
        {
            if (RoomConnector.Instance != null)
            {
                RoomConnector.Instance.Failed -= OnFailed;
                RoomConnector.Instance.ReturnedToLobby -= OnReturnedToLobby;
            }
        }

        /// <summary>
        /// Сцена лобби не перезагружается — она всё это время лежит под игрой,
        /// поэтому Start() при возврате не сработает и статус надо чистить руками.
        /// Иначе висело «Создаём комнату...» от прошлой попытки.
        /// </summary>
        private void OnReturnedToLobby()
        {
            _statusText.text = "";
            _codeInput.text = "";
        }

        private void Update()
        {
            var hasName = !string.IsNullOrWhiteSpace(_nameInput.text);
            var busy = RoomConnector.Instance != null && RoomConnector.Instance.IsBusy;

            _createButton.interactable = hasName && !busy;
            _joinButton.interactable = hasName && !busy
                                       && !string.IsNullOrWhiteSpace(_codeInput.text);
        }

        private void OnCreate()
        {
            _statusText.text = "Создаём комнату...";
            RoomConnector.Instance.CreateRoom(_nameInput.text.Trim());
        }

        private void OnJoin()
        {
            _statusText.text = "Подключаемся...";
            RoomConnector.Instance.JoinRoom(_codeInput.text, _nameInput.text.Trim());
        }

        private void OnFailed(string message)
        {
            _statusText.text = message;
        }

        /// <summary>
        /// Быстрый старт для отладки: создаёт комнату и сажает ботов,
        /// чтобы гонять флоу игры без второго клиента.
        /// Сеть при этом не проверяется — боты живут на стороне ведущего.
        /// </summary>
        private void OnDebugStart()
        {
            var name = string.IsNullOrWhiteSpace(_nameInput.text)
                ? "Ведущий"
                : _nameInput.text.Trim();

            _statusText.text = "Отладка: создаём комнату с ботами...";
            RoomConnector.Instance.CreateRoom(name);
            StartCoroutine(AddBotsWhenReady());
        }

        private System.Collections.IEnumerator AddBotsWhenReady()
        {
            // Комната появляется не мгновенно: сперва подключение,
            // затем загрузка сцены, только потом спавн GameRoom.
            var timeout = 15f;
            while (timeout > 0f)
            {
                var room = GameRoom.Instance;
                if (room != null && room.Object != null && room.Object.IsValid && room.IsLocalHost)
                {
                    room.AddDebugBots(DebugBotCount);
                    yield break;
                }

                timeout -= Time.deltaTime;
                yield return null;
            }

            Debug.LogWarning("[LobbyUI] Комната так и не появилась — боты не добавлены.");
        }
    }
}

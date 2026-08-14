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

        /// <summary>Свой код комнаты. Пустое поле — код сгенерируется сам.</summary>
        [SerializeField] private InputField _hostCodeInput;
        [SerializeField] private Button _createButton;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Text _statusText;
        [SerializeField] private Button _debugButton;
        [SerializeField] private Button _debugPlayerButton;

        /// <summary>
        /// По одному боту на каждый исход подсчёта: всё верно, только статья,
        /// только часть, всё мимо.
        /// </summary>
        private const int DebugBotCount = 4;

        /// <summary>Параметры партии в режиме отладки за игрока.</summary>
        private const int DebugRounds = 5;
        private const int DebugRoundDuration = 10;

        private void Start()
        {
            _nameInput.text = LocalPlayerIdentity.PlayerName;
            _statusText.text = "";

            _createButton.onClick.AddListener(OnCreate);
            _joinButton.onClick.AddListener(OnJoin);

            if (_debugButton != null)
                _debugButton.onClick.AddListener(OnDebugStart);

            if (_debugPlayerButton != null)
                _debugPlayerButton.onClick.AddListener(OnDebugAsPlayer);

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
            _entering = false;
            _statusText.text = "";
            _codeInput.text = "";
        }

        private void Update()
        {
            // Кнопки остаются активными даже без имени: иначе непонятно,
            // почему ничего не происходит. Причину объясняем при нажатии.
            //
            // А вот пока идёт подключение — блокируем всю панель целиком,
            // включая поля и отладочные кнопки: второй запуск поверх первого
            // поднял бы ещё один NetworkRunner.
            // Собственный флаг, а не только IsBusy коннектора: тот снимается
            // сразу после StartGame, а отладочный запуск продолжается в корутине,
            // и за это время можно было нажать вторую кнопку.
            var busy = _entering
                       || (RoomConnector.Instance != null && RoomConnector.Instance.IsBusy);

            _createButton.interactable = !busy;
            _joinButton.interactable = !busy;
            _nameInput.interactable = !busy;
            _codeInput.interactable = !busy;
            if (_hostCodeInput != null) _hostCodeInput.interactable = !busy;

            if (_debugButton != null) _debugButton.interactable = !busy;
            if (_debugPlayerButton != null) _debugPlayerButton.interactable = !busy;
        }

        /// <summary>Идёт вход в комнату — панель заблокирована целиком.</summary>
        private bool _entering;

        /// <summary>Имя обязательно — без него игрока не отличить в таблице.</summary>
        private bool HasName()
        {
            if (!string.IsNullOrWhiteSpace(_nameInput.text)) return true;

            _statusText.text = "Сначала введи своё имя";
            return false;
        }

        private void OnCreate()
        {
            if (!HasName()) return;

            _entering = true;
            _statusText.text = "Создаём комнату...";

            // Пустое поле — Photon получит сгенерированный код.
            var customCode = _hostCodeInput != null ? _hostCodeInput.text : null;
            RoomConnector.Instance.CreateRoom(_nameInput.text.Trim(), customCode);
        }

        private void OnJoin()
        {
            if (!HasName()) return;

            if (string.IsNullOrWhiteSpace(_codeInput.text))
            {
                _statusText.text = "Введи код комнаты";
                return;
            }

            _entering = true;
            _statusText.text = "Подключаемся...";
            RoomConnector.Instance.JoinRoom(_codeInput.text, _nameInput.text.Trim());
        }

        private void OnFailed(string message)
        {
            // Вход не удался — возвращаем панель в рабочее состояние.
            _entering = false;
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

            _entering = true;
            _statusText.text = "Отладка: создаём комнату с ботами...";
            RoomConnector.Instance.CreateRoom(name);
            StartCoroutine(AddBotsWhenReady(autoHost: false));
        }

        /// <summary>
        /// Отладка со стороны игрока: раунды ведёт автопилот, а мы отвечаем
        /// наравне с ботами. Комнатой всё равно владеем мы — без реального
        /// клиента запускать раунды было бы некому.
        /// </summary>
        private void OnDebugAsPlayer()
        {
            var name = string.IsNullOrWhiteSpace(_nameInput.text)
                ? "Игрок"
                : _nameInput.text.Trim();

            _entering = true;
            _statusText.text = "Отладка: играем за игрока...";
            RoomConnector.Instance.CreateRoom(name);
            StartCoroutine(AddBotsWhenReady(autoHost: true));
        }

        private System.Collections.IEnumerator AddBotsWhenReady(bool autoHost)
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

                    if (autoHost)
                        room.EnableAutoHost(DebugRounds, DebugRoundDuration);

                    yield break;
                }

                timeout -= Time.deltaTime;
                yield return null;
            }

            // Не дождались — разблокируем панель, иначе она осталась бы мёртвой.
            _entering = false;
            _statusText.text = "Не удалось запустить отладку";
            Debug.LogWarning("[LobbyUI] Комната так и не появилась — боты не добавлены.");
        }
    }
}

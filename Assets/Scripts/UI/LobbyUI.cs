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

        private void Start()
        {
            _nameInput.text = LocalPlayerIdentity.PlayerName;
            _statusText.text = "";

            _createButton.onClick.AddListener(OnCreate);
            _joinButton.onClick.AddListener(OnJoin);

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
    }
}

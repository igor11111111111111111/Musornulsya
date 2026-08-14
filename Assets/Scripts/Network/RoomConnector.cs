using System;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using Musornulsya.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Musornulsya.Network
{
    /// <summary>
    /// Поднимает NetworkRunner и заводит комнату в Shared Mode.
    /// Живёт между сценами: лобби → игра.
    /// </summary>
    public class RoomConnector : MonoBehaviour
    {
        public static RoomConnector Instance { get; private set; }

        [SerializeField] private NetworkPrefabRef _gameRoomPrefab;
        [SerializeField] private string _gameSceneName = "Game";

        public NetworkRunner Runner { get; private set; }
        public string RoomCode { get; private set; }
        public bool IsBusy { get; private set; }

        public event Action<string> Failed;

        /// <summary>
        /// Текст последней ошибки. Нужен потому, что событие Failed срабатывает
        /// до возврата в лобби — подписчик к этому моменту уничтожен вместе
        /// со старой сценой, и сообщение бы потерялось.
        /// </summary>
        public string LastError { get; private set; }

        public void ConsumeLastError() => LastError = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public async void CreateRoom(string playerName)
        {
            var code = GenerateRoomCode();
            await Connect(code, playerName, createIfMissing: true);
        }

        public async void JoinRoom(string code, string playerName)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                Failed?.Invoke("Введи код комнаты");
                return;
            }

            await Connect(code.Trim().ToUpperInvariant(), playerName, createIfMissing: false);
        }

        private async Task Connect(string code, string playerName, bool createIfMissing)
        {
            if (IsBusy) return;

            if (string.IsNullOrEmpty(PhotonAppSettings.Global.AppSettings.AppIdFusion))
            {
                Failed?.Invoke("Не задан Photon App ID.\nTools > Fusion > Fusion Hub");
                return;
            }

            IsBusy = true;
            LocalPlayerIdentity.PlayerName = playerName;
            RoomCode = code;

            var runnerObject = new GameObject("NetworkRunner");
            DontDestroyOnLoad(runnerObject);
            Runner = runnerObject.AddComponent<NetworkRunner>();
            Runner.ProvideInput = false;

            // Подключаемся, НЕ уходя из лобби. Раньше сцена грузилась заранее,
            // и при неверном коде игрок успевал увидеть пустой игровой экран,
            // прежде чем его выбрасывало обратно с ошибкой.
            var result = await Runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = code,
                Scene = default,
                PlayerCount = 6,

                // Присоединяющийся не должен создавать комнату: без этого Fusion
                // на несуществующий код молча заводил новую, и игрок оказывался
                // один в пустой комнате вместо сообщения об ошибке.
                EnableClientSessionCreation = createIfMissing,
            });

            if (!result.Ok)
            {
                IsBusy = false;

                var message = createIfMissing
                    ? $"Не удалось создать комнату:\n{result.ShutdownReason}"
                    : "Комната не найдена.\nПроверь код.";

                LastError = message;
                Failed?.Invoke(message);
                Destroy(runnerObject);
                return;   // остаёмся в лобби — уходить было некуда
            }

            // Комната есть — только теперь показываем игровой экран.
            await SceneManager.LoadSceneAsync(_gameSceneName);
            IsBusy = false;

            // Объект комнаты спавнит только создатель. Присоединившиеся получают
            // его по сети — Fusion реплицирует спавн всем в Shared Mode.
            if (createIfMissing)
                Runner.Spawn(_gameRoomPrefab, Vector3.zero, Quaternion.identity, Runner.LocalPlayer);
        }

        public void Leave()
        {
            if (Runner != null)
            {
                Runner.Shutdown();
                Destroy(Runner.gameObject);
                Runner = null;
            }

            // Иначе сообщение от прошлой попытки всплыло бы в лобби как новое.
            LastError = null;
            RoomCode = null;

            SceneManager.LoadScene("Lobby");
        }

        private static string GenerateRoomCode()
        {
            // Без похожих символов (0/O, 1/I) — код диктуют в Discord.
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = new char[5];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = alphabet[UnityEngine.Random.Range(0, alphabet.Length)];
            return new string(chars);
        }
    }
}

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

        /// <summary>
        /// Индекс игровой сцены в Build Settings. Берём по пути, а не числом,
        /// чтобы порядок сцен в списке можно было менять безнаказанно.
        /// </summary>
        private int GameSceneBuildIndex =>
            SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{_gameSceneName}.unity");

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

            // В WebGL исключение из async-метода не всплывает в консоль браузера
            // само — там видно только «Uncaught undefined». Логируем вручную,
            // иначе причину падения не установить.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Debug.LogError($"[Необработанное исключение] {e.ExceptionObject}");

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Debug.LogError($"[Ошибка в async-задаче] {e.Exception}");
                e.SetObserved();
            };
        }

        /// <summary>
        /// Код комнаты — это SessionName в Photon, произвольная строка на нашей
        /// стороне. Пустой код означает «сгенерируй сам».
        /// </summary>
        public async void CreateRoom(string playerName, string customCode = null)
        {
            var code = string.IsNullOrWhiteSpace(customCode)
                ? GenerateRoomCode()
                : customCode.Trim().ToUpperInvariant();

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
            try
            {
                await ConnectInternal(code, playerName, createIfMissing);
            }
            catch (Exception e)
            {
                // Без этого исключение из async-метода пропадало бесследно,
                // и игрок навсегда оставался на экране «Создаём комнату…».
                var message = $"Ошибка подключения:\n{e.Message}";

                LastError = message;
                Failed?.Invoke(message);
                IsBusy = false;

                Debug.LogError($"[RoomConnector] {e}");
            }
        }

        private async Task ConnectInternal(string code, string playerName, bool createIfMissing)
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

            // Без менеджера сцен Fusion не загрузит сцену, указанную в StartGameArgs.
            runnerObject.AddComponent<NetworkSceneManagerDefault>();

            // Подключаемся, НЕ уходя из лобби: при неверном коде игрок иначе
            // успевал увидеть пустой игровой экран, прежде чем его выбрасывало
            // обратно с ошибкой.
            //
            // Сцену при этом указываем явно. Передать Scene = default нельзя —
            // Fusion тогда пишет «no network scene will be loaded» и не
            // синхронизирует объекты в ней: PlayerState не спавнился, а лог
            // заполнялся AssertException про рассинхрон тиков.
            // Additive, а не Single: игровая сцена ДОБАВЛЯЕТСЯ к лобби.
            // При Single Unity выгружает сцену, в которой живёт раннер, и связь
            // рвётся — лог заполняется AssertException про рассинхрон тиков,
            // а объекты комнаты не доезжают. Так же сделано в штатном меню Fusion.
            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(SceneRef.FromIndex(GameSceneBuildIndex), LoadSceneMode.Additive);

            var result = await Runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = code,
                Scene = sceneInfo,
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

            // Свой код мог оказаться занятым: Fusion в этом случае молча
            // подключает нас к чужой комнате вторым игроком, а мы ждали роли
            // ведущего. Отсекаем это сразу.
            if (createIfMissing && Runner.SessionInfo != null && Runner.SessionInfo.PlayerCount > 1)
            {
                const string message = "Комната с таким кодом уже занята.\nВыбери другой.";

                LastError = message;
                Failed?.Invoke(message);

                Runner.Shutdown();
                Destroy(runnerObject);
                Runner = null;
                IsBusy = false;
                return;
            }

            // Лобби загружено additive-режимом рядом с игрой, поэтому его
            // интерфейс надо спрятать вручную, иначе он просвечивал бы сквозь
            // игровой экран.
            SetLobbyVisible(false);

            IsBusy = false;

            // Дальше ждём готовности сцены в корутине, а не через Task.Yield:
            // в WebGL один поток, и цикл ожидания внутри async-метода
            // блокировал главный цикл — игра зависала на экране лобби.
            if (createIfMissing)
                StartCoroutine(SpawnRoomWhenSceneReady());
        }

        /// <summary>
        /// Спавнит объект комнаты, когда менеджер сцен освободится. Пока он занят
        /// загрузкой, провайдер объектов Fusion отказывается выдавать префабы
        /// (NetworkObjectProviderDefault.DelayIfSceneManagerIsBusy).
        /// </summary>
        private System.Collections.IEnumerator SpawnRoomWhenSceneReady()
        {
            var timeout = 20f;

            while (timeout > 0f)
            {
                if (Runner == null) yield break;   // успели выйти

                if (Runner.SceneManager == null || !Runner.SceneManager.IsBusy)
                {
                    Runner.Spawn(_gameRoomPrefab, Vector3.zero, Quaternion.identity,
                        Runner.LocalPlayer);
                    yield break;
                }

                timeout -= Time.deltaTime;
                yield return null;
            }

            var message = "Комната не создалась — сцена не загрузилась вовремя.";
            LastError = message;
            Failed?.Invoke(message);
            Debug.LogError($"[RoomConnector] {message}");
        }

        /// <summary>Сработало возвращение в лобби — интерфейс пора сбросить.</summary>
        public event Action ReturnedToLobby;

        public void Leave()
        {
            if (Runner != null)
            {
                Runner.Shutdown();
                Destroy(Runner.gameObject);
                Runner = null;
            }

            // Сетевую сцену выгружаем сами: после Shutdown она может остаться
            // загруженной, и игровой экран продолжал висеть поверх лобби.
            var gameScene = SceneManager.GetSceneByName(_gameSceneName);
            if (gameScene.IsValid() && gameScene.isLoaded)
                SceneManager.UnloadSceneAsync(gameScene);

            // Иначе сообщение от прошлой попытки всплыло бы в лобби как новое.
            LastError = null;
            RoomCode = null;

            // Лобби никуда не девалось — оно всё это время лежало под игрой.
            SetLobbyVisible(true);
            ReturnedToLobby?.Invoke();
        }

        /// <summary>
        /// Показывает или прячет интерфейс лобби. Сцена лобби остаётся
        /// загруженной, пока идёт игра: сетевая сцена добавляется поверх неё,
        /// а не заменяет её.
        /// </summary>
        private static void SetLobbyVisible(bool visible)
        {
            var lobbyScene = SceneManager.GetSceneByName("Lobby");
            if (!lobbyScene.IsValid() || !lobbyScene.isLoaded) return;

            foreach (var root in lobbyScene.GetRootGameObjects())
            {
                // Прячем только интерфейс. Камера и EventSystem из лобби
                // обслуживают и игровую сцену — она грузится поверх и своих
                // не имеет, иначе получались бы дубликаты.
                if (root.GetComponent<Canvas>() != null)
                    root.SetActive(visible);
            }
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

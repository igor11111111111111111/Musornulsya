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
            await Connect(code, playerName, isCreator: true);
        }

        public async void JoinRoom(string code, string playerName)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                Failed?.Invoke("Введи код комнаты");
                return;
            }

            await Connect(code.Trim().ToUpperInvariant(), playerName, isCreator: false);
        }

        private async Task Connect(string code, string playerName, bool isCreator)
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

            // Сцена игры грузится до старта раннера, чтобы объекты комнаты
            // спавнились уже в ней.
            await SceneManager.LoadSceneAsync(_gameSceneName);

            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex));

            var result = await Runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = code,
                Scene = sceneInfo,
                PlayerCount = 6,
            });

            IsBusy = false;

            if (!result.Ok)
            {
                Failed?.Invoke($"Не удалось подключиться:\n{result.ShutdownReason}");
                Destroy(runnerObject);
                SceneManager.LoadScene("Lobby");
                return;
            }

            // Комнату спавнит только создатель. Присоединившиеся получат её по сети.
            if (isCreator)
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

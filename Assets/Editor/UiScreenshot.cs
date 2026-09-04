#if UNITY_EDITOR
using System.IO;
using Musornulsya.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using IOPath = System.IO.Path;

namespace Musornulsya.EditorTools
{
    /// <summary>
    /// Снимает экраны интерфейса в PNG без запуска Play mode: канвас
    /// рендерится в RenderTexture. Нужен, чтобы сверять оформление,
    /// не собирая билд и не кликая руками.
    ///
    /// Меню: Musornulsya > Снять скриншоты.
    /// </summary>
    public static class UiScreenshot
    {
        private const int Width = 1600;
        private const int Height = 900;

        [MenuItem("Musornulsya/Снять скриншоты")]
        public static void Capture()
        {
            var outDir = System.Environment.GetEnvironmentVariable("SHOT_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = "Screenshots";
            Directory.CreateDirectory(outDir);

            CaptureLobby(outDir);
            CaptureGame(outDir);

            Debug.Log("Скриншоты готовы: " + IOPath.GetFullPath(outDir));
        }

        private static void CaptureLobby(string outDir)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity", OpenSceneMode.Single);

            var settings = Object.FindFirstObjectByType<SettingsPanelUI>(FindObjectsInactive.Include);
            SetPanel(settings, false);
            Shot(IOPath.Combine(outDir, "1_lobby.png"));

            // Панель настроек поверх лобби.
            SetPanel(settings, true);
            Shot(IOPath.Combine(outDir, "2_lobby_settings.png"));
            SetPanel(settings, false);
        }

        private static void CaptureGame(string outDir)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);

            var host = FindByName("HostPanel");
            var player = FindByName("PlayerPanel");
            var setup = FindByName("SetupPanel");
            var picker = Object.FindFirstObjectByType<ArticlePickerUI>(FindObjectsInactive.Include);
            var history = Object.FindFirstObjectByType<HistoryPanelUI>(FindObjectsInactive.Include);
            var settings = Object.FindFirstObjectByType<SettingsPanelUI>(FindObjectsInactive.Include);

            SetPanel(picker, false);
            SetPanel(history, false);
            SetPanel(settings, false);

            // Экран ведущего
            Show(setup, false);
            Show(host, true);
            Show(player, false);
            Shot(IOPath.Combine(outDir, "3_game_host.png"));

            // Экран игрока
            Show(host, false);
            Show(player, true);
            Shot(IOPath.Combine(outDir, "4_game_player.png"));

            // Настройка числа раундов
            Show(player, false);
            Show(setup, true);
            Shot(IOPath.Combine(outDir, "5_game_setup.png"));

            // Оверлеи
            Show(setup, false);
            Show(host, true);

            SetPanel(picker, true);
            Shot(IOPath.Combine(outDir, "6_picker.png"));
            SetPanel(picker, false);

            SetPanel(history, true);
            Shot(IOPath.Combine(outDir, "7_history.png"));
            SetPanel(history, false);

            SetPanel(settings, true);
            Shot(IOPath.Combine(outDir, "8_game_settings.png"));
            SetPanel(settings, false);
        }

        /// <summary>Панели прячут себя через поле _root, а не сам объект.</summary>
        private static void SetPanel(Component panel, bool visible)
        {
            if (panel == null) return;

            var so = new SerializedObject(panel);
            var root = so.FindProperty("_root");

            if (root != null && root.objectReferenceValue is GameObject go)
                go.SetActive(visible);
            else
                panel.gameObject.SetActive(visible);
        }

        private static void Show(GameObject go, bool visible)
        {
            if (go != null) go.SetActive(visible);
        }

        private static GameObject FindByName(string name)
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go.name == name) return go;
            }

            return null;
        }

        private static void Shot(string path)
        {
            var canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError("[UiScreenshot] Канвас не найден.");
                return;
            }

            var camGo = new GameObject("ShotCam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Height / 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.11f, 0.12f, 0.15f);
            cam.transform.position = new Vector3(0f, 0f, -1000f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 5000f;

            var prevMode = canvas.renderMode;
            var prevCam = canvas.worldCamera;

            // ScreenSpaceOverlay игнорирует камеру, поэтому на время съёмки
            // переключаем канвас на режим с камерой.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 100f;

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
            };
            cam.targetTexture = rt;

            Canvas.ForceUpdateCanvases();
            cam.Render();

            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;

            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();

            RenderTexture.active = prevActive;
            File.WriteAllBytes(path, tex.EncodeToPNG());

            cam.targetTexture = null;
            canvas.renderMode = prevMode;
            canvas.worldCamera = prevCam;

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);

            Debug.Log("Снято: " + path);
        }
    }
}
#endif

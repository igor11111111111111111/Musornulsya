#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Fusion;
using Musornulsya.Data;
using Musornulsya.Network;
using Musornulsya.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Musornulsya.EditorTools
{
    /// <summary>
    /// Собирает сцены Lobby и Game целиком из кода.
    /// Меню: Musornulsya > Собрать сцены.
    ///
    /// Идемпотентно — можно запускать повторно, сцены перезаписываются.
    /// </summary>
    public static class SceneBuilder
    {
        private const string ScenesDir = "Assets/Scenes";
        private const string PrefabsDir = "Assets/Prefabs";

        /// <summary>
        /// Префаб строки таблицы лежит в Resources и грузится по имени в рантайме —
        /// так надёжнее, чем ссылка в сцене, которая терялась при пересборке.
        /// </summary>
        private const string ResourcesDir = "Assets/Resources";
        private const string LobbyPath = ScenesDir + "/Lobby.unity";
        private const string GamePath = ScenesDir + "/Game.unity";

        private static readonly Color BgColor = new Color(0.11f, 0.12f, 0.15f);
        private static readonly Color PanelColor = new Color(0.16f, 0.17f, 0.21f);
        private static readonly Color AccentColor = new Color(0.36f, 0.55f, 0.92f);
        private static readonly Color TextColor = new Color(0.93f, 0.94f, 0.96f);

        [MenuItem("Musornulsya/Собрать сцены")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(ScenesDir);
            Directory.CreateDirectory(PrefabsDir);
            Directory.CreateDirectory(ResourcesDir);

            BuildPlayerRowPrefab();
            var playerStatePrefab = BuildPlayerStatePrefab();
            var gameRoomPrefab = BuildGameRoomPrefab(playerStatePrefab);

            // Префабы должны быть в базе до того, как сцены начнут на них ссылаться.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            BuildGameScene(); 
            BuildLobbyScene(gameRoomPrefab);

            RegisterScenesInBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorSceneManager.OpenScene(LobbyPath);
            Debug.Log("[SceneBuilder] Сцены собраны: Lobby.unity и Game.unity");
        }

        // ---------- Префабы ----------

        /// <summary>
        /// Снимает DestroyWhenStateAuthorityLeaves.
        /// По умолчанию Fusion удаляет объект, когда владелец вышел из комнаты, — это
        /// обнулило бы очки отвалившегося игрока и распустило комнату при вылете ведущего.
        /// Нам нужно обратное: состояние переживает разрыв и ждёт возвращения.
        /// </summary>
        private static void KeepAliveWhenOwnerLeaves(NetworkObject netObj)
        {
            netObj.Flags &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
            EditorUtility.SetDirty(netObj);
        }

        private static GameObject BuildPlayerStatePrefab()
        {
            var go = new GameObject("PlayerState");
            var netObj = go.AddComponent<NetworkObject>();
            go.AddComponent<PlayerState>();

            KeepAliveWhenOwnerLeaves(netObj);

            var path = PrefabsDir + "/PlayerState.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject BuildGameRoomPrefab(GameObject playerStatePrefab)
        {
            var go = new GameObject("GameRoom");
            var netObj = go.AddComponent<NetworkObject>();
            var room = go.AddComponent<GameRoom>();

            KeepAliveWhenOwnerLeaves(netObj);

            var so = new SerializedObject(room);
            var prop = so.FindProperty("_playerStatePrefab");
            AssignNetworkPrefabRef(prop, playerStatePrefab);
            so.ApplyModifiedPropertiesWithoutUndo();

            var path = PrefabsDir + "/GameRoom.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>
        /// NetworkPrefabRef хранит GUID ассета в NetworkObjectGuid — это не строка,
        /// а фиксированный буфер из двух long. Пишем так же, как собственный
        /// инспектор Fusion (NetworkObjectGuidDrawer.SetValue).
        /// </summary>
        private static unsafe void AssignNetworkPrefabRef(SerializedProperty prop, GameObject prefab)
        {
            if (prop == null || prefab == null)
            {
                Debug.LogError(
                    "[SceneBuilder] Нечего записывать в NetworkPrefabRef " +
                    $"(поле: {(prop == null ? "нет" : prop.name)}, " +
                    $"префаб: {(prefab == null ? "нет" : prefab.name)}).");
                return;
            }

            var assetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));

            if (!NetworkObjectGuid.TryParse(assetGuid, out var guid))
            {
                Debug.LogWarning(
                    $"[SceneBuilder] Не удалось разобрать GUID префаба {prefab.name}. " +
                    "Назначь его в инспекторе вручную.");
                return;
            }

            var raw = prop.FindPropertyRelative(nameof(NetworkObjectGuid.RawGuidValue));
            if (raw == null)
            {
                Debug.LogWarning(
                    $"[SceneBuilder] Не найдено поле RawGuidValue для {prefab.name}. " +
                    "Назначь префаб в инспекторе вручную.");
                return;
            }

            raw.GetFixedBufferElementAtIndex(0).longValue = guid.RawGuidValue[0];
            raw.GetFixedBufferElementAtIndex(1).longValue = guid.RawGuidValue[1];
        }

        private static void BuildPlayerRowPrefab()
        {
            var root = CreateUIObject("PlayerRow", null, out var rt);
            rt.sizeDelta = new Vector2(0, 52);

            var bg = root.AddComponent<Image>();
            bg.color = PanelColor;

            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 6, 6);
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var nameText = CreateText(root.transform, "Name", "Игрок", 20, TextAnchor.MiddleLeft);
            SetLayout(nameText.gameObject, preferredWidth: 170, flexibleWidth: 0);

            var answerText = CreateText(root.transform, "Answer", "...", 20, TextAnchor.MiddleLeft);
            SetLayout(answerText.gameObject, preferredWidth: 240, flexibleWidth: 1);

            var scoreText = CreateText(root.transform, "Score", "0", 22, TextAnchor.MiddleCenter);
            scoreText.fontStyle = FontStyle.Bold;
            SetLayout(scoreText.gameObject, preferredWidth: 56, flexibleWidth: 0);

            // Группа кнопок начисления
            var awardGroup = CreateUIObject("AwardGroup", root.transform, out var awardRt);
            var awardLayout = awardGroup.AddComponent<HorizontalLayoutGroup>();
            awardLayout.spacing = 6;
            awardLayout.childForceExpandWidth = false;
            awardLayout.childForceExpandHeight = true;
            awardLayout.childControlWidth = true;
            awardLayout.childControlHeight = true;
            SetLayout(awardGroup, preferredWidth: 140, flexibleWidth: 0);

            // Высоту задаём явно: без неё layout ужимал кнопку так,
            // что подпись обрезалась и оставался пустой прямоугольник.
            var plus1 = CreateButton(awardGroup.transform, "Plus1", "+1", AccentColor);
            SetLayout(plus1.gameObject, preferredWidth: 58, preferredHeight: 38, flexibleWidth: 0);

            var plus2 = CreateButton(awardGroup.transform, "Plus2", "+2", AccentColor);
            SetLayout(plus2.gameObject, preferredWidth: 58, preferredHeight: 38, flexibleWidth: 0);

            // Показывается вместо +1/+2, пока ведущий не оспорил автоподсчёт.
            var disputeButton = CreateButton(awardGroup.transform, "DisputeButton", "Оспорить",
                new Color(0.5f, 0.33f, 0.2f));
            SetLayout(disputeButton.gameObject, preferredWidth: 122, preferredHeight: 38, flexibleWidth: 0);

            var row = root.AddComponent<PlayerRowUI>();
            var so = new SerializedObject(row);
            so.FindProperty("_nameText").objectReferenceValue = nameText;
            so.FindProperty("_answerText").objectReferenceValue = answerText;
            so.FindProperty("_scoreText").objectReferenceValue = scoreText;
            so.FindProperty("_plus1").objectReferenceValue = plus1;
            so.FindProperty("_plus2").objectReferenceValue = plus2;
            so.FindProperty("_disputeButton").objectReferenceValue = disputeButton;
            so.FindProperty("_awardGroup").objectReferenceValue = awardGroup;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Кладём в Resources — GameUI грузит его оттуда по имени.
            var path = ResourcesDir + "/PlayerRow.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        // ---------- Сцена лобби ----------

        private static void BuildLobbyScene(GameObject gameRoomPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateEventSystem();

            // Общие менеджеры живут в лобби и переезжают между сценами.
            var db = new GameObject("ArticleDatabase");
            db.AddComponent<ArticleDatabase>();

            var connectorGo = new GameObject("RoomConnector");
            var connector = connectorGo.AddComponent<RoomConnector>();
            var connectorSo = new SerializedObject(connector);
            AssignNetworkPrefabRef(connectorSo.FindProperty("_gameRoomPrefab"), gameRoomPrefab);
            connectorSo.FindProperty("_gameSceneName").stringValue = "Game";
            connectorSo.ApplyModifiedPropertiesWithoutUndo();

            var canvas = CreateCanvas("LobbyCanvas");
            CreateBackground(canvas.transform);

            // Центральная колонка
            var panel = CreateUIObject("Panel", canvas.transform, out var panelRt);
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(520, 500);
            panelRt.anchoredPosition = Vector2.zero;

            var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.spacing = 14;
            panelLayout.padding = new RectOffset(0, 0, 0, 0);
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childAlignment = TextAnchor.UpperCenter;

            var title = CreateText(panel.transform, "Title", "МУСОРНУЛСЯ", 44, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            SetLayout(title.gameObject, preferredHeight: 64);

            var subtitle = CreateText(panel.transform, "Subtitle",
                "Ведущий объясняет статью голосом в Discord,\nостальные пишут номер и часть",
                17, TextAnchor.MiddleCenter);
            subtitle.color = new Color(0.65f, 0.68f, 0.75f);
            SetLayout(subtitle.gameObject, preferredHeight: 52);

            // Имя — отдельно и крупнее остальных полей: это первое,
            // что нужно заполнить, и без него вход невозможен.
            var nameInput = CreateInputField(panel.transform, "NameInput", "Твоё имя");
            nameInput.textComponent.fontSize = 24;
            if (nameInput.placeholder is Text namePlaceholder)
                namePlaceholder.fontSize = 24;
            SetLayout(nameInput.gameObject, preferredHeight: 68);

            // Код общий для обеих кнопок: при создании его можно оставить пустым
            // и получить сгенерированный, при подключении он обязателен.
            var codeInput = CreateInputField(panel.transform, "CodeInput",
                "Код комнаты — или оставь пустым");
            codeInput.characterLimit = 5;
            SetLayout(codeInput.gameObject, preferredHeight: 56);

            var createButton = CreateButton(panel.transform, "CreateButton",
                "Создать игру  (ты ведущий)", AccentColor);
            SetLayout(createButton.gameObject, preferredHeight: 56);

            var divider = CreateText(panel.transform, "Divider", "— или —", 15, TextAnchor.MiddleCenter);
            divider.color = new Color(0.5f, 0.53f, 0.6f);
            SetLayout(divider.gameObject, preferredHeight: 30);

            var joinButton = CreateButton(panel.transform, "JoinButton",
                "Присоединиться  (ты игрок)", new Color(0.28f, 0.31f, 0.38f));
            SetLayout(joinButton.gameObject, preferredHeight: 56);

            var status = CreateText(panel.transform, "Status", "", 17, TextAnchor.MiddleCenter);
            status.color = new Color(0.95f, 0.6f, 0.55f);
            SetLayout(status.gameObject, preferredHeight: 50);

            // Отладка живёт в правом нижнем углу мелким шрифтом —
            // рабочий сценарий она не должна перетягивать на себя.
            var debugRow = CreateUIObject("DebugRow", canvas.transform, out var debugRt);
            debugRt.anchorMin = new Vector2(1, 0);
            debugRt.anchorMax = new Vector2(1, 0);
            debugRt.pivot = new Vector2(1, 0);
            debugRt.anchoredPosition = new Vector2(-16, 16);
            debugRt.sizeDelta = new Vector2(320, 30);

            var debugLayout = debugRow.AddComponent<HorizontalLayoutGroup>();
            debugLayout.spacing = 8;
            debugLayout.childForceExpandWidth = false;
            debugLayout.childForceExpandHeight = true;
            debugLayout.childControlWidth = true;
            debugLayout.childControlHeight = true;

            var debugButton = CreateSmallButton(debugRow.transform, "DebugButton",
                "Отладка: я ведущий", new Color(0.35f, 0.24f, 0.12f));
            SetLayout(debugButton.gameObject, preferredWidth: 156, flexibleWidth: 0);

            var debugPlayerButton = CreateSmallButton(debugRow.transform, "DebugPlayerButton",
                "Отладка: я игрок", new Color(0.3f, 0.21f, 0.34f));
            SetLayout(debugPlayerButton.gameObject, preferredWidth: 140, flexibleWidth: 0);

            var lobbyGo = new GameObject("LobbyUI");
            var lobby = lobbyGo.AddComponent<LobbyUI>();
            var so = new SerializedObject(lobby);
            so.FindProperty("_nameInput").objectReferenceValue = nameInput;
            so.FindProperty("_codeInput").objectReferenceValue = codeInput;
            so.FindProperty("_createButton").objectReferenceValue = createButton;
            so.FindProperty("_joinButton").objectReferenceValue = joinButton;
            so.FindProperty("_statusText").objectReferenceValue = status;
            so.FindProperty("_debugButton").objectReferenceValue = debugButton;
            so.FindProperty("_debugPlayerButton").objectReferenceValue = debugPlayerButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, LobbyPath);
        }

        /// <summary>Компактная кнопка для служебных действий.</summary>
        private static Button CreateSmallButton(Transform parent, string name, string label, Color color)
        {
            var button = CreateButton(parent, name, label, color);

            var text = button.GetComponentInChildren<Text>();
            text.fontSize = 12;
            text.color = new Color(0.8f, 0.82f, 0.86f);

            return button;
        }

        // ---------- Сцена игры ----------

        // ---------- Сцена игры ----------

        private static void BuildGameScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Ни камеры, ни EventSystem: игровая сцена грузится additive-режимом
            // поверх лобби, и они уже есть там. Дубликаты давали предупреждения
            // «There are 2 event systems in the scene» на каждом кадре.

            var canvas = CreateCanvas("GameCanvas");
            CreateBackground(canvas.transform);

            // ---- Верхняя шапка ----
            var header = CreateUIObject("Header", canvas.transform, out var headerRt);
            headerRt.anchorMin = new Vector2(0, 1);
            headerRt.anchorMax = new Vector2(1, 1);
            headerRt.pivot = new Vector2(0.5f, 1);
            headerRt.sizeDelta = new Vector2(0, 64);
            headerRt.anchoredPosition = Vector2.zero;

            header.AddComponent<Image>().color = PanelColor;

            var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
            headerLayout.padding = new RectOffset(20, 20, 8, 8);
            headerLayout.spacing = 14;
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = true;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;

            var roundText = CreateText(header.transform, "RoundText", "Лобби", 22, TextAnchor.MiddleLeft);
            roundText.fontStyle = FontStyle.Bold;
            SetLayout(roundText.gameObject, preferredWidth: 250, flexibleWidth: 0);

            var phaseText = CreateText(header.transform, "PhaseText", "", 17, TextAnchor.MiddleLeft);
            phaseText.color = new Color(0.65f, 0.68f, 0.75f);
            SetLayout(phaseText.gameObject, preferredWidth: 260, flexibleWidth: 1);

            var historyButton = CreateButton(header.transform, "HistoryButton", "История",
                new Color(0.28f, 0.31f, 0.38f));
            SetLayout(historyButton.gameObject, preferredWidth: 120, preferredHeight: 40, flexibleWidth: 0);

            var timerText = CreateText(header.transform, "TimerText", "", 26, TextAnchor.MiddleCenter);
            timerText.fontStyle = FontStyle.Bold;
            SetLayout(timerText.gameObject, preferredWidth: 110, flexibleWidth: 0);

            var roomCodeText = CreateText(header.transform, "RoomCode", "Код: —", 19, TextAnchor.MiddleRight);
            SetLayout(roomCodeText.gameObject, preferredWidth: 230, flexibleWidth: 0);

            var leaveButton = CreateButton(header.transform, "LeaveButton", "Выйти",
                new Color(0.35f, 0.24f, 0.26f));
            SetLayout(leaveButton.gameObject, preferredWidth: 100, preferredHeight: 40, flexibleWidth: 0);

            // ---- Настройка игры (только у ведущего, до первого раунда) ----
            var setupPanel = CreateUIObject("SetupPanel", canvas.transform, out var setupRt);
            setupRt.anchorMin = new Vector2(0, 1);
            setupRt.anchorMax = new Vector2(1, 1);
            setupRt.pivot = new Vector2(0.5f, 1);
            setupRt.sizeDelta = new Vector2(-40, 92);
            setupRt.anchoredPosition = new Vector2(0, -78);

            setupPanel.AddComponent<Image>().color = PanelColor;

            var setupLayout = setupPanel.AddComponent<HorizontalLayoutGroup>();
            setupLayout.padding = new RectOffset(18, 18, 16, 16);
            setupLayout.spacing = 12;
            setupLayout.childAlignment = TextAnchor.MiddleLeft;
            setupLayout.childForceExpandWidth = false;
            setupLayout.childForceExpandHeight = true;
            setupLayout.childControlWidth = true;
            setupLayout.childControlHeight = true;

            var setupLabel = CreateText(setupPanel.transform, "SetupLabel",
                "Сколько раундов играем?", 20, TextAnchor.MiddleLeft);
            SetLayout(setupLabel.gameObject, preferredWidth: 280, flexibleWidth: 0);

            var totalRoundsInput = CreateInputField(setupPanel.transform, "TotalRoundsInput", "10");
            SetLayout(totalRoundsInput.gameObject, preferredWidth: 120, flexibleWidth: 0);

            var confirmSetupButton = CreateButton(setupPanel.transform, "ConfirmSetupButton",
                "Начать игру", new Color(0.25f, 0.6f, 0.38f));
            SetLayout(confirmSetupButton.gameObject, preferredWidth: 200, flexibleWidth: 0);

            // ---- Панель ведущего ----
            var hostPanel = CreateUIObject("HostPanel", canvas.transform, out var hostRt);
            hostRt.anchorMin = new Vector2(0, 1);
            hostRt.anchorMax = new Vector2(1, 1);
            hostRt.pivot = new Vector2(0.5f, 1);
            hostRt.sizeDelta = new Vector2(-40, 246);
            hostRt.anchoredPosition = new Vector2(0, -78);

            hostPanel.AddComponent<Image>().color = PanelColor;

            var hostLayout = hostPanel.AddComponent<VerticalLayoutGroup>();
            hostLayout.padding = new RectOffset(18, 18, 14, 14);
            hostLayout.spacing = 8;
            hostLayout.childForceExpandWidth = true;
            hostLayout.childForceExpandHeight = false;
            hostLayout.childControlWidth = true;
            hostLayout.childControlHeight = true;

            var articleLabel = CreateText(hostPanel.transform, "ArticleLabel",
                "Выбери статью", 26, TextAnchor.MiddleLeft);
            articleLabel.fontStyle = FontStyle.Bold;
            SetLayout(articleLabel.gameObject, preferredHeight: 34);

            // Формулировка всегда на виду и прокручивается: у некоторых статей
            // перечень признаков занимает больше тысячи символов (ст. 105 ч. 2),
            // и в фиксированный блок он не помещался.
            var articleTextBg = CreateUIObject("ArticleTextBg", hostPanel.transform, out _);
            articleTextBg.AddComponent<Image>().color = new Color(0.09f, 0.1f, 0.13f);
            SetLayout(articleTextBg, preferredHeight: 112);

            var articleScroll = articleTextBg.AddComponent<ScrollRect>();
            articleScroll.horizontal = false;
            articleScroll.movementType = ScrollRect.MovementType.Clamped;
            articleScroll.scrollSensitivity = 24f;

            var articleViewport = CreateUIObject("Viewport", articleTextBg.transform,
                out var articleViewportRt);
            articleViewportRt.anchorMin = Vector2.zero;
            articleViewportRt.anchorMax = Vector2.one;
            articleViewportRt.offsetMin = new Vector2(12, 8);
            articleViewportRt.offsetMax = new Vector2(-12, -8);
            articleViewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            articleViewport.AddComponent<Mask>().showMaskGraphic = false;
            articleScroll.viewport = articleViewportRt;

            var articleContent = CreateUIObject("Content", articleViewport.transform,
                out var articleContentRt);
            articleContentRt.anchorMin = new Vector2(0, 1);
            articleContentRt.anchorMax = new Vector2(1, 1);
            articleContentRt.pivot = new Vector2(0.5f, 1);
            articleContentRt.offsetMin = new Vector2(0, articleContentRt.offsetMin.y);
            articleContentRt.offsetMax = new Vector2(0, articleContentRt.offsetMax.y);
            articleScroll.content = articleContentRt;

            var acLayout = articleContent.AddComponent<VerticalLayoutGroup>();
            acLayout.childForceExpandWidth = true;
            acLayout.childForceExpandHeight = false;
            acLayout.childControlWidth = true;
            acLayout.childControlHeight = true;

            var acFitter = articleContent.AddComponent<ContentSizeFitter>();
            acFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var articleText = CreateText(articleContent.transform, "ArticleText", "", 14,
                TextAnchor.UpperLeft);
            articleText.color = new Color(0.72f, 0.75f, 0.8f);
            articleText.verticalOverflow = VerticalWrapMode.Overflow;

            // Высоту текст считает сам — иначе длинная формулировка
            // не растянула бы содержимое и прокрутка не появилась бы.
            articleText.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var hostButtons = CreateUIObject("HostButtons", hostPanel.transform, out _);
            var hbLayout = hostButtons.AddComponent<HorizontalLayoutGroup>();
            hbLayout.spacing = 10;
            hbLayout.childForceExpandWidth = false;
            hbLayout.childForceExpandHeight = true;
            hbLayout.childControlWidth = true;
            hbLayout.childControlHeight = true;
            SetLayout(hostButtons, preferredHeight: 48);

            var randomButton = CreateButton(hostButtons.transform, "RandomButton",
                "Случайная статья", AccentColor);
            SetLayout(randomButton.gameObject, preferredWidth: 200, flexibleWidth: 0);

            var pickButton = CreateButton(hostButtons.transform, "PickButton",
                "Выбрать статью", new Color(0.28f, 0.31f, 0.38f));
            SetLayout(pickButton.gameObject, preferredWidth: 190, flexibleWidth: 0);

            var durationDropdown = CreateDropdown(hostButtons.transform, "DurationDropdown");
            SetLayout(durationDropdown.gameObject, preferredWidth: 150, flexibleWidth: 0);

            var startRoundButton = CreateButton(hostButtons.transform, "StartRoundButton",
                "Начать раунд", new Color(0.25f, 0.6f, 0.38f));
            SetLayout(startRoundButton.gameObject, preferredWidth: 190, flexibleWidth: 0);

            var nextRoundButton = CreateButton(hostButtons.transform, "NextRoundButton",
                "Следующий раунд", new Color(0.25f, 0.6f, 0.38f));
            SetLayout(nextRoundButton.gameObject, preferredWidth: 210, flexibleWidth: 0);

            // ---- Панель игрока ----
            var playerPanel = CreateUIObject("PlayerPanel", canvas.transform, out var ppRt);
            ppRt.anchorMin = new Vector2(0, 1);
            ppRt.anchorMax = new Vector2(1, 1);
            ppRt.pivot = new Vector2(0.5f, 1);
            ppRt.sizeDelta = new Vector2(-40, 250);
            ppRt.anchoredPosition = new Vector2(0, -78);

            playerPanel.AddComponent<Image>().color = PanelColor;

            var ppLayout = playerPanel.AddComponent<VerticalLayoutGroup>();
            ppLayout.padding = new RectOffset(18, 18, 14, 14);
            ppLayout.spacing = 10;
            ppLayout.childForceExpandWidth = true;
            ppLayout.childForceExpandHeight = false;
            ppLayout.childControlWidth = true;
            ppLayout.childControlHeight = true;

            var answerRow = CreateUIObject("AnswerRow", playerPanel.transform, out _);
            var arLayout = answerRow.AddComponent<HorizontalLayoutGroup>();
            arLayout.spacing = 10;
            arLayout.childAlignment = TextAnchor.MiddleLeft;
            arLayout.childForceExpandWidth = false;

            // Не растягиваем по высоте: иначе поля ввода вытягивались во всю
            // высоту панели и выглядели пустыми прямоугольниками.
            arLayout.childForceExpandHeight = false;
            arLayout.childControlWidth = true;
            arLayout.childControlHeight = true;
            SetLayout(answerRow, preferredHeight: 56);

            var articleCaption = CreateText(answerRow.transform, "ArticleCaption", "Статья",
                18, TextAnchor.MiddleRight);
            articleCaption.color = new Color(0.65f, 0.68f, 0.75f);
            SetLayout(articleCaption.gameObject, preferredWidth: 80, preferredHeight: 52,
                flexibleWidth: 0);

            // Подсказки нейтральные: конкретные числа выглядели как уже
            // введённый ответ, и было непонятно, вводил их игрок или нет.
            var articleInput = CreateInputField(answerRow.transform, "ArticleInput", "номер");
            SetLayout(articleInput.gameObject, preferredWidth: 190, preferredHeight: 52,
                flexibleWidth: 0);

            var partCaption = CreateText(answerRow.transform, "PartCaption", "Часть",
                18, TextAnchor.MiddleRight);
            partCaption.color = new Color(0.65f, 0.68f, 0.75f);
            SetLayout(partCaption.gameObject, preferredWidth: 70, preferredHeight: 52,
                flexibleWidth: 0);

            var partInput = CreateInputField(answerRow.transform, "PartInput", "часть");
            SetLayout(partInput.gameObject, preferredWidth: 120, preferredHeight: 52,
                flexibleWidth: 0);

            var submitButton = CreateButton(answerRow.transform, "SubmitButton",
                "Отправить", AccentColor);
            SetLayout(submitButton.gameObject, preferredWidth: 180, preferredHeight: 52,
                flexibleWidth: 1);

            var submitStatus = CreateText(playerPanel.transform, "SubmitStatus",
                "Ждём, пока ведущий начнёт", 18, TextAnchor.MiddleLeft);
            submitStatus.color = new Color(0.65f, 0.68f, 0.75f);
            SetLayout(submitStatus.gameObject, preferredHeight: 28);

            // Правильный ответ прокручивается: у некоторых статей перечень
            // признаков занимает больше тысячи символов.
            var revealedBg = CreateUIObject("RevealedBg", playerPanel.transform, out _);
            SetLayout(revealedBg, preferredHeight: 108);

            var revealedScroll = revealedBg.AddComponent<ScrollRect>();
            revealedScroll.horizontal = false;
            revealedScroll.movementType = ScrollRect.MovementType.Clamped;
            revealedScroll.scrollSensitivity = 24f;

            var revealedViewport = CreateUIObject("Viewport", revealedBg.transform,
                out var revealedViewportRt);
            revealedViewportRt.anchorMin = Vector2.zero;
            revealedViewportRt.anchorMax = Vector2.one;
            revealedViewportRt.offsetMin = Vector2.zero;
            revealedViewportRt.offsetMax = Vector2.zero;
            revealedViewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            revealedViewport.AddComponent<Mask>().showMaskGraphic = false;
            revealedScroll.viewport = revealedViewportRt;

            var revealedContent = CreateUIObject("Content", revealedViewport.transform,
                out var revealedContentRt);
            revealedContentRt.anchorMin = new Vector2(0, 1);
            revealedContentRt.anchorMax = new Vector2(1, 1);
            revealedContentRt.pivot = new Vector2(0.5f, 1);
            revealedContentRt.offsetMin = new Vector2(0, revealedContentRt.offsetMin.y);
            revealedContentRt.offsetMax = new Vector2(0, revealedContentRt.offsetMax.y);
            revealedScroll.content = revealedContentRt;

            var rcLayout = revealedContent.AddComponent<VerticalLayoutGroup>();
            rcLayout.childForceExpandWidth = true;
            rcLayout.childForceExpandHeight = false;
            rcLayout.childControlWidth = true;
            rcLayout.childControlHeight = true;

            revealedContent.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var revealedArticleText = CreateText(revealedContent.transform, "RevealedArticle",
                "", 15, TextAnchor.UpperLeft);
            revealedArticleText.color = new Color(0.45f, 0.92f, 0.5f);
            revealedArticleText.fontStyle = FontStyle.Bold;
            revealedArticleText.verticalOverflow = VerticalWrapMode.Overflow;

            revealedArticleText.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // ---- Таблица игроков ----
            var scrollGo = CreateUIObject("PlayerList", canvas.transform, out var scrollRt);
            scrollRt.anchorMin = new Vector2(0, 0);
            scrollRt.anchorMax = new Vector2(1, 1);
            scrollRt.offsetMin = new Vector2(20, 20);
            scrollRt.offsetMax = new Vector2(-20, -336);

            scrollGo.AddComponent<Image>().color = new Color(0.13f, 0.14f, 0.18f);

            var scrollRect = scrollGo.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = CreateUIObject("Viewport", scrollGo.transform, out var viewportRt);
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            scrollRect.viewport = viewportRt;

            var content = CreateUIObject("Content", viewport.transform, out var contentRt);
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.sizeDelta = new Vector2(0, 0);
            scrollRect.content = contentRt;

            var contentLayout = content.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(8, 8, 8, 8);
            contentLayout.spacing = 6;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ---- Оверлеи ----
            var historyPanel = BuildHistoryPanel(canvas.transform);
            var articlePicker = BuildArticlePicker(canvas.transform);

            // ---- Связываем GameUI ----
            var gameUiGo = new GameObject("GameUI");
            var gameUi = gameUiGo.AddComponent<GameUI>();
            var so = new SerializedObject(gameUi);

            so.FindProperty("_roomCodeText").objectReferenceValue = roomCodeText;
            so.FindProperty("_roundText").objectReferenceValue = roundText;
            so.FindProperty("_phaseText").objectReferenceValue = phaseText;
            so.FindProperty("_timerText").objectReferenceValue = timerText;
            so.FindProperty("_historyButton").objectReferenceValue = historyButton;
            so.FindProperty("_leaveButton").objectReferenceValue = leaveButton;

            so.FindProperty("_setupPanel").objectReferenceValue = setupPanel;
            so.FindProperty("_totalRoundsInput").objectReferenceValue = totalRoundsInput;
            so.FindProperty("_confirmSetupButton").objectReferenceValue = confirmSetupButton;

            so.FindProperty("_hostPanel").objectReferenceValue = hostPanel;
            so.FindProperty("_articleLabel").objectReferenceValue = articleLabel;
            so.FindProperty("_articleText").objectReferenceValue = articleText;
            so.FindProperty("_randomButton").objectReferenceValue = randomButton;
            so.FindProperty("_pickButton").objectReferenceValue = pickButton;
            so.FindProperty("_durationDropdown").objectReferenceValue = durationDropdown;
            so.FindProperty("_startRoundButton").objectReferenceValue = startRoundButton;
            so.FindProperty("_nextRoundButton").objectReferenceValue = nextRoundButton;

            so.FindProperty("_playerPanel").objectReferenceValue = playerPanel;
            so.FindProperty("_articleInput").objectReferenceValue = articleInput;
            so.FindProperty("_partInput").objectReferenceValue = partInput;
            so.FindProperty("_submitButton").objectReferenceValue = submitButton;
            so.FindProperty("_submitStatus").objectReferenceValue = submitStatus;
            so.FindProperty("_revealedArticleText").objectReferenceValue = revealedArticleText;

            // Префаб строки здесь не назначается: GameUI грузит его
            // из Resources в рантайме.
            so.FindProperty("_rowsParent").objectReferenceValue = contentRt;

            so.FindProperty("_historyPanel").objectReferenceValue = historyPanel;
            so.FindProperty("_articlePicker").objectReferenceValue = articlePicker;

            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, GamePath);
        }

        // ---------- Оверлей истории ----------

        private static HistoryPanelUI BuildHistoryPanel(Transform parent)
        {
            var root = CreateUIObject("HistoryPanel", parent, out var rootRt);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            root.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 0.97f);

            var panel = CreateUIObject("Panel", root.transform, out var panelRt);
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(1000, 600);
            panel.AddComponent<Image>().color = PanelColor;

            var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(24, 24, 20, 20);
            panelLayout.spacing = 14;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;

            var title = CreateText(panel.transform, "Title", "История раундов", 30, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            SetLayout(title.gameObject, preferredHeight: 42);

            var winner = CreateText(panel.transform, "Winner", "", 24, TextAnchor.MiddleCenter);
            winner.color = new Color(0.45f, 0.92f, 0.5f);
            winner.fontStyle = FontStyle.Bold;
            SetLayout(winner.gameObject, preferredHeight: 36);

            // Сетка результатов
            var gridScroll = CreateUIObject("GridScroll", panel.transform, out _);
            gridScroll.AddComponent<Image>().color = new Color(0.13f, 0.14f, 0.18f);
            SetLayout(gridScroll, preferredHeight: 420);

            var gridRect = gridScroll.AddComponent<ScrollRect>();
            gridRect.movementType = ScrollRect.MovementType.Clamped;

            var gridViewport = CreateUIObject("Viewport", gridScroll.transform, out var gridViewportRt);
            gridViewportRt.anchorMin = Vector2.zero;
            gridViewportRt.anchorMax = Vector2.one;
            gridViewportRt.offsetMin = Vector2.zero;
            gridViewportRt.offsetMax = Vector2.zero;
            gridViewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            gridViewport.AddComponent<Mask>().showMaskGraphic = false;
            gridRect.viewport = gridViewportRt;

            // Строки складываем вертикально, а ячейки внутри строки —
            // горизонтально. GridLayoutGroup здесь не годится: он делает
            // все колонки одинаковыми, а колонка с именем должна быть шире.
            var grid = CreateUIObject("Grid", gridViewport.transform, out var gridRt);
            gridRt.anchorMin = new Vector2(0, 1);
            gridRt.anchorMax = new Vector2(0, 1);
            gridRt.pivot = new Vector2(0, 1);
            gridRect.content = gridRt;

            var gridLayout = grid.AddComponent<VerticalLayoutGroup>();
            gridLayout.padding = new RectOffset(12, 12, 12, 12);
            gridLayout.spacing = 4;
            gridLayout.childForceExpandWidth = false;
            gridLayout.childForceExpandHeight = false;
            gridLayout.childControlWidth = true;
            gridLayout.childControlHeight = true;

            var gridFitter = grid.AddComponent<ContentSizeFitter>();
            gridFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            gridFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Кнопки
            var buttons = CreateUIObject("Buttons", panel.transform, out _);
            var btnLayout = buttons.AddComponent<HorizontalLayoutGroup>();
            btnLayout.spacing = 12;
            btnLayout.childAlignment = TextAnchor.MiddleCenter;
            btnLayout.childForceExpandWidth = false;
            btnLayout.childForceExpandHeight = true;
            btnLayout.childControlWidth = true;
            btnLayout.childControlHeight = true;
            SetLayout(buttons, preferredHeight: 52);

            var closeButton = CreateButton(buttons.transform, "CloseButton", "Закрыть",
                new Color(0.28f, 0.31f, 0.38f));
            SetLayout(closeButton.gameObject, preferredWidth: 200, flexibleWidth: 0);

            var restartButton = CreateButton(buttons.transform, "RestartButton", "Начать заново",
                new Color(0.25f, 0.6f, 0.38f));
            SetLayout(restartButton.gameObject, preferredWidth: 240, flexibleWidth: 0);

            var exitButton = CreateButton(buttons.transform, "ExitButton", "Выйти в лобби",
                new Color(0.35f, 0.24f, 0.26f));
            SetLayout(exitButton.gameObject, preferredWidth: 220, flexibleWidth: 0);

            var ui = root.AddComponent<HistoryPanelUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("_root").objectReferenceValue = root;
            so.FindProperty("_titleText").objectReferenceValue = title;
            so.FindProperty("_winnerText").objectReferenceValue = winner;
            so.FindProperty("_gridParent").objectReferenceValue = gridRt;
            so.FindProperty("_closeButton").objectReferenceValue = closeButton;
            so.FindProperty("_restartButton").objectReferenceValue = restartButton;
            so.FindProperty("_exitButton").objectReferenceValue = exitButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            return ui;
        }

        // ---------- Оверлей выбора статьи ----------

        private static ArticlePickerUI BuildArticlePicker(Transform parent)
        {
            var root = CreateUIObject("ArticlePicker", parent, out var rootRt);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            root.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 0.97f);

            var panel = CreateUIObject("Panel", root.transform, out var panelRt);
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(1000, 600);

            // Фон под панелью: без него содержимое лежало прямо на затемнении
            // и выглядело прижатым к краям экрана.
            panel.AddComponent<Image>().color = PanelColor;

            var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(24, 24, 20, 20);
            panelLayout.spacing = 12;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;

            var title = CreateText(panel.transform, "Title", "Выбор статьи", 28, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            SetLayout(title.gameObject, preferredHeight: 38);

            var searchInput = CreateInputField(panel.transform, "SearchInput",
                "Поиск по номеру или названию");
            SetLayout(searchInput.gameObject, preferredHeight: 48);

            // Две колонки: статьи и части
            var columns = CreateUIObject("Columns", panel.transform, out _);
            var colLayout = columns.AddComponent<HorizontalLayoutGroup>();
            colLayout.spacing = 14;
            colLayout.childForceExpandWidth = false;
            colLayout.childForceExpandHeight = true;
            colLayout.childControlWidth = true;
            colLayout.childControlHeight = true;
            SetLayout(columns, preferredHeight: 420);

            var articleList = BuildScrollColumn(columns.transform, "Articles", 400);
            var partsColumn = CreateUIObject("PartsColumn", columns.transform, out _);
            var partsColLayout = partsColumn.AddComponent<VerticalLayoutGroup>();
            partsColLayout.spacing = 8;
            partsColLayout.childForceExpandWidth = true;
            partsColLayout.childForceExpandHeight = false;
            partsColLayout.childControlWidth = true;
            partsColLayout.childControlHeight = true;
            SetLayout(partsColumn, preferredWidth: 580, flexibleWidth: 1);

            var selectedText = CreateText(partsColumn.transform, "SelectedArticle",
                "Выбери статью слева", 17, TextAnchor.UpperLeft);
            selectedText.fontStyle = FontStyle.Bold;
            SetLayout(selectedText.gameObject, preferredHeight: 56);

            var partList = BuildScrollColumn(partsColumn.transform, "Parts", 0);

            var closeButton = CreateButton(panel.transform, "CloseButton", "Отмена",
                new Color(0.35f, 0.24f, 0.26f));
            SetLayout(closeButton.gameObject, preferredHeight: 48);

            var ui = root.AddComponent<ArticlePickerUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("_root").objectReferenceValue = root;
            so.FindProperty("_searchInput").objectReferenceValue = searchInput;
            so.FindProperty("_articleListParent").objectReferenceValue = articleList;
            so.FindProperty("_partListParent").objectReferenceValue = partList;
            so.FindProperty("_selectedArticleText").objectReferenceValue = selectedText;
            so.FindProperty("_closeButton").objectReferenceValue = closeButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            return ui;
        }

        /// <summary>Скроллируемая колонка; возвращает контейнер для элементов.</summary>
        private static RectTransform BuildScrollColumn(Transform parent, string name, float width)
        {
            var scrollGo = CreateUIObject(name, parent, out _);
            scrollGo.AddComponent<Image>().color = new Color(0.13f, 0.14f, 0.18f);

            if (width > 0)
                SetLayout(scrollGo, preferredWidth: width, flexibleWidth: 0);
            else
                SetLayout(scrollGo, preferredHeight: 380, flexibleWidth: 1);

            var scrollRect = scrollGo.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = CreateUIObject("Viewport", scrollGo.transform, out var viewportRt);
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            scrollRect.viewport = viewportRt;

            var content = CreateUIObject("Content", viewport.transform, out var contentRt);
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);

            // Обнуляем горизонтальные отступы: без этого RectTransform
            // сохранял значения от размера при создании (Left/Right = -50),
            // и содержимое вылезало за viewport — плашки обрезались.
            contentRt.offsetMin = new Vector2(0, contentRt.offsetMin.y);
            contentRt.offsetMax = new Vector2(0, contentRt.offsetMax.y);

            scrollRect.content = contentRt;

            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 6;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return contentRt;
        }

        // ---------- Примитивы UI ----------

        private static GameObject CreateUIObject(string name, Transform parent, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            rt = go.GetComponent<RectTransform>();
            if (parent != null)
                rt.SetParent(parent, false);
            return go;
        }

        private static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static void CreateBackground(Transform parent)
        {
            var go = CreateUIObject("Background", parent, out var rt);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.AddComponent<Image>().color = BgColor;
            rt.SetAsFirstSibling();
        }

        private static Text CreateText(Transform parent, string name, string content,
            int fontSize, TextAnchor anchor)
        {
            var go = CreateUIObject(name, parent, out _);
            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = TextColor;
            text.font = DefaultFont;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = false;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, Color color)
        {
            var go = CreateUIObject(name, parent, out _);
            var image = go.AddComponent<Image>();
            image.color = color;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            // Неактивная кнопка гасится сильнее, чем по умолчанию: штатный
            // disabledColor почти не отличался от обычного, и было непонятно,
            // что кнопка не работает.
            var colors = button.colors;
            colors.disabledColor = new Color(0.35f, 0.35f, 0.38f, 0.5f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;

            var text = CreateText(go.transform, "Label", label, 19, TextAnchor.MiddleCenter);
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(6, 2);
            textRt.offsetMax = new Vector2(-6, -2);

            return button;
        }

        /// <summary>
        /// Выпадающий список. Собирается вручную, потому что стандартный
        /// шаблон Dropdown живёт в редакторных ресурсах uGUI и из кода
        /// не создаётся — без шаблона список просто не раскрывался бы.
        /// </summary>
        private static Dropdown CreateDropdown(Transform parent, string name)
        {
            var go = CreateUIObject(name, parent, out _);
            go.AddComponent<Image>().color = new Color(0.09f, 0.1f, 0.13f);

            var dropdown = go.AddComponent<Dropdown>();

            var label = CreateText(go.transform, "Label", "", 18, TextAnchor.MiddleLeft);
            var labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(12, 2);
            labelRt.offsetMax = new Vector2(-24, -2);

            // ---- Шаблон раскрывающегося списка ----
            var template = CreateUIObject("Template", go.transform, out var templateRt);
            templateRt.anchorMin = new Vector2(0, 0);
            templateRt.anchorMax = new Vector2(1, 0);
            templateRt.pivot = new Vector2(0.5f, 1);
            templateRt.anchoredPosition = new Vector2(0, 2);
            templateRt.sizeDelta = new Vector2(0, 160);
            template.AddComponent<Image>().color = new Color(0.16f, 0.17f, 0.21f);

            var templateScroll = template.AddComponent<ScrollRect>();
            templateScroll.horizontal = false;
            templateScroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = CreateUIObject("Viewport", template.transform, out var viewportRt);
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            viewportRt.pivot = new Vector2(0, 1);
            viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            templateScroll.viewport = viewportRt;

            var content = CreateUIObject("Content", viewport.transform, out var contentRt);
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.sizeDelta = new Vector2(0, 40);
            templateScroll.content = contentRt;

            var item = CreateUIObject("Item", content.transform, out var itemRt);
            itemRt.anchorMin = new Vector2(0, 0.5f);
            itemRt.anchorMax = new Vector2(1, 0.5f);
            itemRt.sizeDelta = new Vector2(0, 36);

            var itemToggle = item.AddComponent<Toggle>();

            var itemBg = CreateUIObject("Item Background", item.transform, out var itemBgRt);
            itemBgRt.anchorMin = Vector2.zero;
            itemBgRt.anchorMax = Vector2.one;
            itemBgRt.offsetMin = Vector2.zero;
            itemBgRt.offsetMax = Vector2.zero;
            var itemBgImage = itemBg.AddComponent<Image>();
            itemBgImage.color = new Color(0.22f, 0.24f, 0.3f);

            var itemLabel = CreateText(item.transform, "Item Label", "", 18, TextAnchor.MiddleLeft);
            var itemLabelRt = itemLabel.rectTransform;
            itemLabelRt.anchorMin = Vector2.zero;
            itemLabelRt.anchorMax = Vector2.one;
            itemLabelRt.offsetMin = new Vector2(12, 1);
            itemLabelRt.offsetMax = new Vector2(-12, -1);

            itemToggle.targetGraphic = itemBgImage;
            itemToggle.isOn = true;

            dropdown.template = templateRt;
            dropdown.captionText = label;
            dropdown.itemText = itemLabel;
            dropdown.targetGraphic = go.GetComponent<Image>();

            template.SetActive(false);

            return dropdown;
        }

        private static InputField CreateInputField(Transform parent, string name, string placeholder)
        {
            var go = CreateUIObject(name, parent, out _);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.09f, 0.1f, 0.13f);

            var input = go.AddComponent<InputField>();

            var text = CreateText(go.transform, "Text", "", 20, TextAnchor.MiddleLeft);
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(14, 2);
            textRt.offsetMax = new Vector2(-14, -2);

            var ph = CreateText(go.transform, "Placeholder", placeholder, 20, TextAnchor.MiddleLeft);
            // Заметно тусклее вводимого текста: иначе подсказку принимали
            // за уже введённый ответ.
            ph.color = new Color(0.38f, 0.4f, 0.46f);
            ph.fontStyle = FontStyle.Italic;
            var phRt = ph.rectTransform;
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(14, 2);
            phRt.offsetMax = new Vector2(-14, -2);

            input.textComponent = text;
            input.placeholder = ph;
            input.targetGraphic = image;
            input.lineType = InputField.LineType.SingleLine;

            return input;
        }

        private static void SetLayout(GameObject go, float preferredWidth = -1,
            float preferredHeight = -1, float flexibleWidth = -1)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (preferredWidth >= 0) le.preferredWidth = preferredWidth;
            if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
            if (flexibleWidth >= 0) le.flexibleWidth = flexibleWidth;
        }

        private static Font _cachedFont;

        /// <summary>
        /// Шрифт интерфейса. Берём Inter из Assets/Resources/Fonts, а не
        /// встроенный LegacyRuntime: у встроенного нет кириллических глифов,
        /// и в WebGL-сборке весь русский текст пропадал.
        /// </summary>
        private static Font DefaultFont
        {
            get
            {
                if (_cachedFont != null) return _cachedFont;

                _cachedFont = AssetDatabase.LoadAssetAtPath<Font>(FontPath);

                if (_cachedFont == null)
                {
                    // Запасной вариант, чтобы сборка сцен не падала совсем.
                    // Кириллицы в нём нет — текст будет пустым.
                    _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                    Debug.LogError(
                        $"[SceneBuilder] Не найден {FontPath}. " +
                        "Русский текст в билде не отобразится.");
                }

                return _cachedFont;
            }
        }

        private const string FontPath = "Assets/Resources/Fonts/Inter-Regular.ttf";

        private static void CreateCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BgColor;
            cam.orthographic = true;
        }

        private static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();

            // В проекте активен новый Input System (activeInputHandler: 1),
            // поэтому legacy StandaloneInputModule здесь не работает.
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        private static void RegisterScenesInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(LobbyPath, true),
                new EditorBuildSettingsScene(GamePath, true),
            };

            // Остальные сцены проекта оставляем, но после наших.
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.path != LobbyPath && s.path != GamePath)
                    scenes.Add(s);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif

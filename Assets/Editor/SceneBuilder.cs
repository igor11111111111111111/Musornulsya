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

            var rowPrefab = BuildPlayerRowPrefab();
            var playerStatePrefab = BuildPlayerStatePrefab();
            var gameRoomPrefab = BuildGameRoomPrefab(playerStatePrefab);

            BuildGameScene(rowPrefab);
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
            if (prop == null || prefab == null) return;

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

        private static PlayerRowUI BuildPlayerRowPrefab()
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
            SetLayout(scoreText.gameObject, preferredWidth: 50, flexibleWidth: 0);

            // Группа кнопок начисления
            var awardGroup = CreateUIObject("AwardGroup", root.transform, out var awardRt);
            var awardLayout = awardGroup.AddComponent<HorizontalLayoutGroup>();
            awardLayout.spacing = 6;
            awardLayout.childForceExpandWidth = false;
            awardLayout.childForceExpandHeight = true;
            awardLayout.childControlWidth = true;
            awardLayout.childControlHeight = true;
            SetLayout(awardGroup, preferredWidth: 130, flexibleWidth: 0);

            var plus1 = CreateButton(awardGroup.transform, "Plus1", "+1", AccentColor);
            SetLayout(plus1.gameObject, preferredWidth: 60, flexibleWidth: 0);

            var plus2 = CreateButton(awardGroup.transform, "Plus2", "+2", AccentColor);
            SetLayout(plus2.gameObject, preferredWidth: 60, flexibleWidth: 0);

            var row = root.AddComponent<PlayerRowUI>();
            var so = new SerializedObject(row);
            so.FindProperty("_nameText").objectReferenceValue = nameText;
            so.FindProperty("_answerText").objectReferenceValue = answerText;
            so.FindProperty("_scoreText").objectReferenceValue = scoreText;
            so.FindProperty("_plus1").objectReferenceValue = plus1;
            so.FindProperty("_plus2").objectReferenceValue = plus2;
            so.FindProperty("_awardGroup").objectReferenceValue = awardGroup;
            so.ApplyModifiedPropertiesWithoutUndo();

            var path = PrefabsDir + "/PlayerRow.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            // Грузим компонент именно из ассета: объект, который возвращает
            // SaveAsPrefabAsset, не сериализуется как ссылка на ассет
            // и поле в сцене осталось бы пустым.
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<PlayerRowUI>(path);
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
            panelRt.sizeDelta = new Vector2(460, 470);
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

            var nameInput = CreateInputField(panel.transform, "NameInput", "Твоё имя");
            SetLayout(nameInput.gameObject, preferredHeight: 52);

            var createButton = CreateButton(panel.transform, "CreateButton",
                "Создать игру  (ты ведущий)", AccentColor);
            SetLayout(createButton.gameObject, preferredHeight: 56);

            var divider = CreateText(panel.transform, "Divider", "— или —", 15, TextAnchor.MiddleCenter);
            divider.color = new Color(0.5f, 0.53f, 0.6f);
            SetLayout(divider.gameObject, preferredHeight: 30);

            var codeInput = CreateInputField(panel.transform, "CodeInput", "Код комнаты");
            codeInput.characterLimit = 5;
            SetLayout(codeInput.gameObject, preferredHeight: 52);

            var joinButton = CreateButton(panel.transform, "JoinButton",
                "Присоединиться  (ты игрок)", new Color(0.28f, 0.31f, 0.38f));
            SetLayout(joinButton.gameObject, preferredHeight: 56);

            var status = CreateText(panel.transform, "Status", "", 17, TextAnchor.MiddleCenter);
            status.color = new Color(0.95f, 0.6f, 0.55f);
            SetLayout(status.gameObject, preferredHeight: 50);

            var lobbyGo = new GameObject("LobbyUI");
            var lobby = lobbyGo.AddComponent<LobbyUI>();
            var so = new SerializedObject(lobby);
            so.FindProperty("_nameInput").objectReferenceValue = nameInput;
            so.FindProperty("_codeInput").objectReferenceValue = codeInput;
            so.FindProperty("_createButton").objectReferenceValue = createButton;
            so.FindProperty("_joinButton").objectReferenceValue = joinButton;
            so.FindProperty("_statusText").objectReferenceValue = status;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, LobbyPath);
        }

        // ---------- Сцена игры ----------

        private static void BuildGameScene(PlayerRowUI rowPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateEventSystem();

            var canvas = CreateCanvas("GameCanvas");
            CreateBackground(canvas.transform);

            // ---- Верхняя шапка ----
            var header = CreateUIObject("Header", canvas.transform, out var headerRt);
            headerRt.anchorMin = new Vector2(0, 1);
            headerRt.anchorMax = new Vector2(1, 1);
            headerRt.pivot = new Vector2(0.5f, 1);
            headerRt.sizeDelta = new Vector2(0, 64);
            headerRt.anchoredPosition = Vector2.zero;

            var headerBg = header.AddComponent<Image>();
            headerBg.color = PanelColor;

            var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
            headerLayout.padding = new RectOffset(20, 20, 0, 0);
            headerLayout.spacing = 20;
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = true;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;

            var roundText = CreateText(header.transform, "RoundText", "Лобби", 24, TextAnchor.MiddleLeft);
            roundText.fontStyle = FontStyle.Bold;
            SetLayout(roundText.gameObject, preferredWidth: 160, flexibleWidth: 0);

            var phaseText = CreateText(header.transform, "PhaseText", "", 18, TextAnchor.MiddleLeft);
            phaseText.color = new Color(0.65f, 0.68f, 0.75f);
            SetLayout(phaseText.gameObject, preferredWidth: 300, flexibleWidth: 1);

            var roomCodeText = CreateText(header.transform, "RoomCode", "Код: —", 20, TextAnchor.MiddleRight);
            SetLayout(roomCodeText.gameObject, preferredWidth: 150, flexibleWidth: 0);

            var leaveButton = CreateButton(header.transform, "LeaveButton", "Выйти",
                new Color(0.35f, 0.24f, 0.26f));
            SetLayout(leaveButton.gameObject, preferredWidth: 100, flexibleWidth: 0);

            // ---- Панель ведущего ----
            var hostPanel = CreateUIObject("HostPanel", canvas.transform, out var hostRt);
            hostRt.anchorMin = new Vector2(0, 1);
            hostRt.anchorMax = new Vector2(1, 1);
            hostRt.pivot = new Vector2(0.5f, 1);
            hostRt.sizeDelta = new Vector2(-40, 210);
            hostRt.anchoredPosition = new Vector2(0, -78);

            var hostBg = hostPanel.AddComponent<Image>();
            hostBg.color = PanelColor;

            var hostLayout = hostPanel.AddComponent<VerticalLayoutGroup>();
            hostLayout.padding = new RectOffset(18, 18, 14, 14);
            hostLayout.spacing = 10;
            hostLayout.childForceExpandWidth = true;
            hostLayout.childForceExpandHeight = false;
            hostLayout.childControlWidth = true;
            hostLayout.childControlHeight = true;

            var articleLabel = CreateText(hostPanel.transform, "ArticleLabel",
                "Нажми «Случайная статья»", 28, TextAnchor.MiddleLeft);
            articleLabel.fontStyle = FontStyle.Bold;
            SetLayout(articleLabel.gameObject, preferredHeight: 40);

            // Ряд кнопок ведущего
            var hostButtons = CreateUIObject("HostButtons", hostPanel.transform, out _);
            var hbLayout = hostButtons.AddComponent<HorizontalLayoutGroup>();
            hbLayout.spacing = 10;
            hbLayout.childForceExpandWidth = false;
            hbLayout.childForceExpandHeight = true;
            hbLayout.childControlWidth = true;
            hbLayout.childControlHeight = true;
            SetLayout(hostButtons, preferredHeight: 50);

            var randomButton = CreateButton(hostButtons.transform, "RandomButton",
                "Случайная статья", AccentColor);
            SetLayout(randomButton.gameObject, preferredWidth: 220, flexibleWidth: 0);

            var toggleTextButton = CreateButton(hostButtons.transform, "ToggleTextButton",
                "Показать формулировку", new Color(0.28f, 0.31f, 0.38f));
            SetLayout(toggleTextButton.gameObject, preferredWidth: 240, flexibleWidth: 0);

            var startRoundButton = CreateButton(hostButtons.transform, "StartRoundButton",
                "Начать раунд", new Color(0.25f, 0.6f, 0.38f));
            SetLayout(startRoundButton.gameObject, preferredWidth: 190, flexibleWidth: 0);

            var revealButton = CreateButton(hostButtons.transform, "RevealButton",
                "Открыть ответы", new Color(0.25f, 0.6f, 0.38f));
            SetLayout(revealButton.gameObject, preferredWidth: 190, flexibleWidth: 0);

            var nextRoundButton = CreateButton(hostButtons.transform, "NextRoundButton",
                "Следующий раунд", new Color(0.25f, 0.6f, 0.38f));
            SetLayout(nextRoundButton.gameObject, preferredWidth: 210, flexibleWidth: 0);

            // Свёрнутый текст статьи
            var articleTextGroup = CreateUIObject("ArticleTextGroup", hostPanel.transform, out _);
            var atgBg = articleTextGroup.AddComponent<Image>();
            atgBg.color = new Color(0.09f, 0.1f, 0.13f);
            var atgLayout = articleTextGroup.AddComponent<VerticalLayoutGroup>();
            atgLayout.padding = new RectOffset(12, 12, 10, 10);
            atgLayout.childForceExpandWidth = true;
            atgLayout.childForceExpandHeight = false;
            atgLayout.childControlWidth = true;
            atgLayout.childControlHeight = true;
            SetLayout(articleTextGroup, preferredHeight: 90);

            var articleText = CreateText(articleTextGroup.transform, "ArticleText", "", 16,
                TextAnchor.UpperLeft);
            articleText.color = new Color(0.72f, 0.75f, 0.8f);
            SetLayout(articleText.gameObject, preferredHeight: 70);

            // ---- Панель игрока ----
            var playerPanel = CreateUIObject("PlayerPanel", canvas.transform, out var ppRt);
            ppRt.anchorMin = new Vector2(0, 1);
            ppRt.anchorMax = new Vector2(1, 1);
            ppRt.pivot = new Vector2(0.5f, 1);
            ppRt.sizeDelta = new Vector2(-40, 170);
            ppRt.anchoredPosition = new Vector2(0, -78);

            var ppBg = playerPanel.AddComponent<Image>();
            ppBg.color = PanelColor;

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
            arLayout.childForceExpandWidth = false;
            arLayout.childForceExpandHeight = true;
            arLayout.childControlWidth = true;
            arLayout.childControlHeight = true;
            SetLayout(answerRow, preferredHeight: 56);

            var answerInput = CreateInputField(answerRow.transform, "AnswerInput",
                "Например: 158 ч.2");
            SetLayout(answerInput.gameObject, preferredWidth: 400, flexibleWidth: 1);

            var submitButton = CreateButton(answerRow.transform, "SubmitButton",
                "Отправить", AccentColor);
            SetLayout(submitButton.gameObject, preferredWidth: 170, flexibleWidth: 0);

            var submitStatus = CreateText(playerPanel.transform, "SubmitStatus",
                "Ждём, пока ведущий начнёт", 18, TextAnchor.MiddleLeft);
            submitStatus.color = new Color(0.65f, 0.68f, 0.75f);
            SetLayout(submitStatus.gameObject, preferredHeight: 30);

            var revealedArticleText = CreateText(playerPanel.transform, "RevealedArticle",
                "", 22, TextAnchor.MiddleLeft);
            revealedArticleText.color = new Color(0.45f, 0.92f, 0.5f);
            revealedArticleText.fontStyle = FontStyle.Bold;
            SetLayout(revealedArticleText.gameObject, preferredHeight: 34);

            // ---- Таблица игроков ----
            var scrollGo = CreateUIObject("PlayerList", canvas.transform, out var scrollRt);
            scrollRt.anchorMin = new Vector2(0, 0);
            scrollRt.anchorMax = new Vector2(1, 1);
            scrollRt.offsetMin = new Vector2(20, 20);
            scrollRt.offsetMax = new Vector2(-20, -300);

            var scrollBg = scrollGo.AddComponent<Image>();
            scrollBg.color = new Color(0.13f, 0.14f, 0.18f);

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

            // ---- Связываем GameUI ----
            var gameUiGo = new GameObject("GameUI");
            var gameUi = gameUiGo.AddComponent<GameUI>();
            var so = new SerializedObject(gameUi);

            so.FindProperty("_roomCodeText").objectReferenceValue = roomCodeText;
            so.FindProperty("_roundText").objectReferenceValue = roundText;
            so.FindProperty("_phaseText").objectReferenceValue = phaseText;
            so.FindProperty("_leaveButton").objectReferenceValue = leaveButton;

            so.FindProperty("_hostPanel").objectReferenceValue = hostPanel;
            so.FindProperty("_articleLabel").objectReferenceValue = articleLabel;
            so.FindProperty("_randomButton").objectReferenceValue = randomButton;
            so.FindProperty("_toggleTextButton").objectReferenceValue = toggleTextButton;
            so.FindProperty("_articleTextGroup").objectReferenceValue = articleTextGroup;
            so.FindProperty("_articleText").objectReferenceValue = articleText;
            so.FindProperty("_startRoundButton").objectReferenceValue = startRoundButton;
            so.FindProperty("_revealButton").objectReferenceValue = revealButton;
            so.FindProperty("_nextRoundButton").objectReferenceValue = nextRoundButton;

            so.FindProperty("_playerPanel").objectReferenceValue = playerPanel;
            so.FindProperty("_answerInput").objectReferenceValue = answerInput;
            so.FindProperty("_submitButton").objectReferenceValue = submitButton;
            so.FindProperty("_submitStatus").objectReferenceValue = submitStatus;
            so.FindProperty("_revealedArticleText").objectReferenceValue = revealedArticleText;

            so.FindProperty("_rowsParent").objectReferenceValue = contentRt;
            so.FindProperty("_rowPrefab").objectReferenceValue = rowPrefab;

            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, GamePath);
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

            var text = CreateText(go.transform, "Label", label, 19, TextAnchor.MiddleCenter);
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(6, 2);
            textRt.offsetMax = new Vector2(-6, -2);

            return button;
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
            ph.color = new Color(0.45f, 0.48f, 0.55f);
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

        private static Font DefaultFont
        {
            get
            {
                // LegacyRuntime.ttf — штатный шрифт uGUI в Unity 2022+/6.
                var font = AssetDatabase.GetBuiltinExtraResource<Font>("LegacyRuntime.ttf");
                if (font == null)
                    font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

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

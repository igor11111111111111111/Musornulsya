using Musornulsya.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Musornulsya.UI
{
    /// <summary>
    /// Попап прогресса подключения: этап словами и полоса заполнения.
    ///
    /// Нужен потому, что подключение занимает несколько секунд и состоит
    /// из разных стадий — раньше игрок видел одну неподвижную строчку
    /// и не понимал, идёт процесс или завис.
    /// </summary>
    public class ConnectProgressUI : MonoBehaviour
    {
        /// <summary>Затемнение фона — гасим компонент, а не объект.</summary>
        [SerializeField] private Image _backdrop;

        /// <summary>Само окно попапа.</summary>
        [SerializeField] private GameObject _panel;

        [SerializeField] private Text _stageText;
        [SerializeField] private Image _barFill;

        /// <summary>Полоса догоняет целевое значение, а не прыгает рывками.</summary>
        private const float FillSpeed = 0.9f;

        /// <summary>
        /// Сколько секунд этап держится на экране минимум. Создание раннера
        /// и загрузка сцены занимают доли кадра — без задержки игрок видел
        /// только ту стадию, на которой шло реальное ожидание сети.
        /// </summary>
        private const float MinStageSeconds = 0.45f;

        private float _target;
        private float _stageHold;
        private readonly System.Collections.Generic.Queue<(string text, float value)> _pending =
            new System.Collections.Generic.Queue<(string, float)>();

        /// <summary>Коннектор, на события которого мы подписаны.</summary>
        private RoomConnector _connector;

        private void Awake()
        {
            HideVisuals();
            TrySubscribe();
        }

        /// <summary>
        /// Порядок Awake между объектами сцены не определён: этот компонент
        /// может проснуться раньше, чем RoomConnector выставит свой Instance.
        /// Поэтому подписываемся при первой возможности и повторяем попытку
        /// каждый кадр, пока коннектор не появится.
        /// </summary>
        private void TrySubscribe()
        {
            if (_connector != null) return;

            var connector = RoomConnector.Instance;
            if (connector == null) return;

            _connector = connector;
            _connector.Progress += OnProgress;
            _connector.Connected += Hide;
            _connector.Failed += OnFailed;
        }

        private void OnDestroy()
        {
            if (_connector == null) return;

            _connector.Progress -= OnProgress;
            _connector.Connected -= Hide;
            _connector.Failed -= OnFailed;
        }

        private void Update()
        {
            TrySubscribe();

            // Очередь прокачиваем ВСЕГДА, а не только когда попап виден:
            // иначе после скрытия в ней оставались недопоказанные этапы,
            // и они всплывали при следующем открытии — то есть при выходе
            // из комнаты игрок видел «Создаём комнату» и «Готово».
            PumpStages();

            if (_closeRequested && _pending.Count == 0 && _stageHold <= 0f)
                CloseNow();

            if (_barFill == null || !_visible) return;

            _shown = Mathf.MoveTowards(_shown, _target, FillSpeed * Time.deltaTime);
            ApplyFill(_shown);
        }

        private void OnProgress(string stage, float progress)
        {
            ShowVisuals();

            // Складываем в очередь: этапы приходят пачкой в одном кадре,
            // и без неё на экране успевал отобразиться только последний.
            _pending.Enqueue((stage, Mathf.Clamp01(progress)));
        }

        /// <summary>Достаёт следующий этап, когда предыдущий побыл на экране.</summary>
        private void PumpStages()
        {
            if (_stageHold > 0f)
            {
                _stageHold -= Time.deltaTime;
                return;
            }

            if (_pending.Count == 0)
            {
                CreepWhileWaiting();
                return;
            }

            var (text, value) = _pending.Dequeue();

            if (_stageText != null) _stageText.text = text;
            _target = value;
            _stageHold = MinStageSeconds;
        }

        /// <summary>
        /// Пока ждём ответа сети, подтягиваем полосу к следующему рубежу.
        ///
        /// StartGame у Fusion — одна непрозрачная операция: коннект, вход
        /// в комнату и загрузка сцены идут внутри неё без промежуточных
        /// событий. Без этого доползания полоса замирала на несколько секунд,
        /// и казалось, что игра зависла.
        /// </summary>
        private void CreepWhileWaiting()
        {
            if (!_visible || _closeRequested) return;

            // Не доходим до следующего этапа вплотную: рывок при его приходе
            // должен оставаться заметным.
            var ceiling = Mathf.Min(_target + 0.28f, 0.95f);

            _target = Mathf.MoveTowards(_target, ceiling, 0.05f * Time.deltaTime);
        }

        /// <summary>При ошибке попап убираем — причину покажет лобби.</summary>
        private void OnFailed(string message) => Hide();

        /// <summary>
        /// Закрываем не сразу: последние этапы приходят в том же кадре,
        /// что и завершение, и без выдержки игрок не успел бы их увидеть.
        /// </summary>
        private void Hide()
        {
            if (_pending.Count > 0 || _stageHold > 0f)
            {
                _closeRequested = true;
                return;
            }

            CloseNow();
        }

        private void CloseNow()
        {
            HideVisuals();

            _target = 0f;
            _stageHold = 0f;
            _closeRequested = false;
            _pending.Clear();

            _shown = 0f;
            ApplyFill(0f);
        }

        private bool _closeRequested;

        /// <summary>
        /// Задаёт ширину заливки долей от родителя. Ширину, а не fillAmount:
        /// Image.Type.Filled несовместим с 9-slice, и скруглённый спрайт
        /// при нём растягивался в овал.
        /// </summary>
        private void ApplyFill(float value)
        {
            if (_barFill == null) return;

            var rt = _barFill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Текущая отрисованная доля — отдельно от целевой.</summary>
        private float _shown;

        /// <summary>
        /// Показываем и прячем содержимое, а не сам объект: на нём висит этот
        /// компонент, и выключение оборвало бы ему Update и подписки.
        /// </summary>
        private void ShowVisuals() => SetVisuals(true);

        private void HideVisuals() => SetVisuals(false);

        private void SetVisuals(bool visible)
        {
            _visible = visible;

            if (_backdrop != null) _backdrop.enabled = visible;
            if (_panel != null) _panel.SetActive(visible);
        }

        private bool _visible;
    }
}

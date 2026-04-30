using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Ребро мини-карты: два якоря в мире, линия между ними (с отступами вдоль отрезка), логические ссылки на ноды (from → to).
/// Клик и переходы — не здесь; только визуал и данные связи.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class MinimapEdge : MonoBehaviour
{
    [Header("Anchors")]
    [Tooltip("Начало ребра: Transform с коллайдером (на ноде-старте). Кнопка «Связать» в инспекторе ищет ноду по пересечению bounds коллайдеров.")]
    [SerializeField] private Transform startAnchor;

    [Tooltip("Конец ребра: Transform с коллайдером (на ноде-конца).")]
    [SerializeField] private Transform endAnchor;

    [Header("Middle")]
    [Tooltip("Размещается в середине отрисовываемого отрезка (после отступов), в мировых координатах.")]
    [SerializeField] private Transform middlePoint;

    [Header("Insets (world units along start→end)")]
    [Tooltip("Расстояние от центра начала по направлению к концу — линия начинается здесь.")]
    [SerializeField, Min(0f)] private float startInset;

    [Tooltip("Расстояние от центра конца назад к началу — линия заканчивается здесь.")]
    [SerializeField, Min(0f)] private float endInset;

    [Header("Visual")]
    [Tooltip("Линия между якорями; positionCount будет 2, useWorldSpace = true. Ширина/материал — как настроишь.")]
    [SerializeField] private LineRenderer lineRenderer;

    [Tooltip("Палитра графа: общие Color A/B/C для кнопки «Применить цвета к рёбрам» в ассете палитры (LineRendererGradientPropertyDriver). Обычно задаётся с MinimapEdgeRegistry.")]
    [SerializeField] private MinimapGraphVisualPalette lineColorPalette;

    [Header("Edge state")]
    [Tooltip("Длительность «перемещение по ребру», если на ребре нет клипа перехода (Play Mode).")]
    [SerializeField, Min(0.01f)] private float movingAlongEdgeDuration = 1f;

    [Header("Travel along edge (Play)")]
    [Tooltip("Ролик на время MovingAlongEdge; общий VideoPlayer у GameManager. Пусто — длительность из movingAlongEdgeDuration.")]
    [SerializeField] private VideoClip edgeTravelVideoClip;

    [Tooltip("Дочерний объект со спрайтом: включается на время MovingAlongEdge, движется от start anchor к end anchor, затем выключается.")]
    [SerializeField] private GameObject travelMoveVisual;

    [Tooltip("Длительность Appearing: линия «растёт» от старта к концу, затем переход в Idle (Play). 0 — длительность 1 с (как раньше).")]
    [SerializeField, Min(0f)] private float appearingToIdleDuration;

    [Header("MovingAlongEdge (travel) → Slider AC")]
    [Tooltip("Если не задано: LineRendererGradientPropertyDriver на том же GameObject, что и LineRenderer.")]
    [SerializeField]
    private LineRendererGradientPropertyDriver lineGradientDriver;

    [Tooltip("За время перемещения travel move visual Slider AC линейно меняется до этого процента (0–100).")]
    [SerializeField, Range(0f, 100f)]
    private float travelAlongEdgeSliderAcTargetPercent = 100f;

    [Header("Blocked → Sliders AB / AC")]
    [Tooltip(
        "Только если нода на конце ребра (корень ToNode) в NodeMapState.Blocked: за треть этого времени Slider AC → 100%, за всё время — Slider AB → 100% (одновременно, AC быстрее). В Play фактическая длительность может быть короче (см. поле ниже). После завершения ramp на ребре у ноды может стартовать её секвенция (см. Node). Не при Selected на конце. При выходе из Blocked — откат слайдеров.")]
    [SerializeField, Min(0.01f)]
    private float blockedSlidersRampDuration = 1f;

    [Tooltip(
        "В Play: от базовой длительности ramp выше на каждый старт случайно отнимается доля от 0 до этого максимума (0.3 = до 30%), у каждого ребра своё. 0 — всегда ровно базовое время.")]
    [SerializeField, Range(0f, 0.3f)]
    private float blockedSlidersRampDurationPlayRandomSubtractMax = 0.3f;

    [Header("Nodes")]
    [Tooltip("Начало ориентированного ребра на карте. Для группы — только родительская нода, не дочерняя.")]
    [SerializeField] private Node fromNode;

    [SerializeField] private Node toNode;

    [Header("Anchors ↔ nodes")]
    [Tooltip(
        "Если включено: Start/End якоря остаются детьми объекта ребра, но каждый кадр совпадают с pivot From/To ноды (позиция и вращение, localScale = 1), как дочерний с локальным нулём у ноды.")]
    [SerializeField]
    private bool anchorsFollowLinkedNodes;

    public Transform StartAnchor => startAnchor;
    public Transform EndAnchor => endAnchor;
    public Transform MiddlePoint => middlePoint;
    public float StartInset
    {
        get => startInset;
        set => startInset = Mathf.Max(0f, value);
    }

    public float EndInset
    {
        get => endInset;
        set => endInset = Mathf.Max(0f, value);
    }

    public LineRenderer Line => lineRenderer;
    public Node FromNode => fromNode;
    public Node ToNode => toNode;

    /// <summary>Якоря синхронизируются с pivot связанных нод (см. кнопку привязки в реестре / инспекторе ребра).</summary>
    public bool AnchorsFollowLinkedNodes => anchorsFollowLinkedNodes;

    public MinimapEdgeState CurrentEdgeState => _currentState;

    /// <summary>Текущая палитра (задаётся с <see cref="MinimapEdgeRegistry"/> или вручную).</summary>
    public MinimapGraphVisualPalette LineColorPalette => lineColorPalette;

    /// <summary>Назначить палитру (ссылка для массового применения A/B/C) и обновить видимость линии.</summary>
    public void SetLineColorPalette(MinimapGraphVisualPalette palette)
    {
        lineColorPalette = palette;
        ApplyCombinedVisual();
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>Длительность фазы <see cref="MinimapEdgeState.MovingAlongEdge"/> (длина клипа ребра или fallback в инспекторе).</summary>
    public float MovingAlongEdgeDuration => ComputeTravelDurationSeconds();

    /// <summary>Длительность ramp слайдеров AB/AC при <see cref="MinimapEdgeState.Blocked"/> (секунды, минимум 0.01).</summary>
    public float BlockedSlidersRampDurationSeconds => Mathf.Max(0.01f, blockedSlidersRampDuration);

    /// <summary>Совпадает с <see cref="BlockedSlidersRampDurationSeconds"/> — AC и AB на ребре в Blocked укладываются в этот интервал.</summary>
    public float BlockedSlidersFullSequenceDurationSeconds() => BlockedSlidersRampDurationSeconds;

    /// <summary>В Play: разрешение по выбору на карте (<see cref="MinimapEdgeRegistry"/>).</summary>
    public bool MapOutgoingLineVisible => _mapOutgoingLineVisible;

    private MinimapEdgeState _currentState = MinimapEdgeState.Idle;
    private MinimapEdgeState _stateAfterMovingCompletes = MinimapEdgeState.Idle;
    private bool _mapOutgoingLineVisible;
    private Coroutine _movingCoroutine;
    private Coroutine _appearingCoroutine;
    /// <summary>True между стартом <see cref="CoMovingAlongEdge"/> и нормальным завершением / прерыванием — для отката Slider AC при Stop.</summary>
    private bool _travelSliderAcTweenActive;
    private float _sliderAcAtTravelStart;
    private Coroutine _blockedSlidersCoroutine;
    private float _sliderAbBeforeBlocked;
    private float _sliderAcBeforeBlocked;
    private Node _blockedRampNotifyEndRoot;
    private bool _blockedRampNotifyCountedWithEndRoot;
    private bool _blockedEdgeLineAcFullNotifiedToEndNode;
    private bool _edgeTravelVideoPlaying;
    /// <summary>В Play при <see cref="MinimapEdgeState.Appearing"/>: 0 — линия у старта, 1 — полный отрезок. Вне Appearing всегда 1.</summary>
    private float _appearLineT = 1f;

    /// <summary>Вызывается один раз в Play после завершения роста линии и перехода ребра Appearing → Idle (если задан в <see cref="SetEdgeState"/>).</summary>
    private Action _onAppearingIdleComplete;

    /// <summary>
    /// GameManager: ребро ждёт случайную задержку перед Appearing — <see cref="MinimapEdgeRegistry.SetAllEdgesVisualStateIdle"/> не должен переводить его в Idle
    /// (иначе после выбора стартовой ноды <see cref="GameManager.ApplyMinimapRulesAfterMapNotify"/> снова покажет полную линию).
    /// </summary>
    private bool _pendingOutgoingAppearStagger;

    /// <summary>Ожидается stagger перед раскрытием к Inactive-соседу (см. GameManager).</summary>
    public bool PendingOutgoingAppearStagger => _pendingOutgoingAppearStagger;

    public void SetPendingOutgoingAppearStagger(bool value) => _pendingOutgoingAppearStagger = value;

    private void Start()
    {
        if (!Application.isPlaying)
            return;
        _currentState = MinimapEdgeState.Idle;
        ApplyCombinedVisual();
    }

    private void OnEnable()
    {
        RefreshLinePositions();
#if UNITY_EDITOR
        EditorApplication.update += EditorPoll;
#endif
        ApplyCombinedVisual();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorPoll;
#endif
        StopEdgePlayCoroutines();
        StopBlockedSlidersRamp(restore: true);
        _pendingOutgoingAppearStagger = false;
    }

#if UNITY_EDITOR
    private void EditorPoll()
    {
        if (!Application.isPlaying)
            RefreshLinePositions();
    }

    private void OnValidate()
    {
        if (fromNode != null && fromNode.GroupParent != null)
            Debug.LogWarning($"{name}: у ребра FromNode — дочерняя нода группы. Рёбра должны исходить только от родителя.", this);

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            RefreshLinePositions();

        ApplyCombinedVisual();
    }
#endif

    private void LateUpdate()
    {
        if (Application.isPlaying)
            RefreshLinePositions();
    }

    /// <summary>
    /// Слой выбора на карте: в Play выставляет <see cref="MinimapEdgeRegistry"/> (исходящие от <see cref="Node.SelectionOwner"/>).
    /// Не меняет <see cref="MinimapEdgeState"/>; итоговый вид — <see cref="ApplyCombinedVisual"/>.
    /// </summary>
    public void SetMapOutgoingLineVisible(bool visible)
    {
        _mapOutgoingLineVisible = visible;
        ApplyCombinedVisual();
    }

    /// <summary>Включить или выключить следование якорей за From/To. При выключении якоря остаются там, где были.</summary>
    public void SetAnchorsFollowLinkedNodes(bool value)
    {
        anchorsFollowLinkedNodes = value;
        RefreshLinePositions();
#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>Смена состояния ребра (в т.ч. дебаг с реестра по цифрам 1–5).</summary>
    /// <param name="stateAfterMovingCompletes">Только для <see cref="MinimapEdgeState.MovingAlongEdge"/>: во что перейти после таймера (по умолчанию <see cref="MinimapEdgeState.Idle"/>).</param>
    /// <param name="onAppearingIdleComplete">Только для <see cref="MinimapEdgeState.Appearing"/> в Play: вызов после анимации роста и перехода в <see cref="MinimapEdgeState.IdleRevealed"/>.</param>
    /// <param name="suppressBlockedSlidersRampOnEnter">
    /// Только для <see cref="MinimapEdgeState.Blocked"/> в Play: не запускать ramp AB/AC при входе (ожидается общий пакетный вызов с <see cref="MinimapEdgeRegistry"/> после задержки).
    /// </param>
    public void SetEdgeState(
        MinimapEdgeState next,
        bool forceLog = true,
        MinimapEdgeState stateAfterMovingCompletes = MinimapEdgeState.Idle,
        Action onAppearingIdleComplete = null,
        bool suppressBlockedSlidersRampOnEnter = false)
    {
        if (!Application.isPlaying)
        {
            var prev = _currentState;
            _currentState = next;
            if (next != MinimapEdgeState.Appearing)
                _appearLineT = 1f;
            if (forceLog && prev != next)
                Debug.Log($"[{name}] MinimapEdge state: {prev} → {next} (не Play — таймер Moving не запускается)", this);
            if (next == MinimapEdgeState.Blocked)
                ApplyBlockedSlidersInstantEditor();
            ApplyCombinedVisual();
            if (next == MinimapEdgeState.Appearing)
                onAppearingIdleComplete?.Invoke();
            return;
        }

        if (_currentState == next && next != MinimapEdgeState.MovingAlongEdge && next != MinimapEdgeState.Appearing)
        {
            // Уже Blocked (например после MovingAlong по маршруту): повторный Blocked из реестра должен запустить ramp, иначе слайдеры не трогаются.
            if (next == MinimapEdgeState.Blocked && Application.isPlaying && !suppressBlockedSlidersRampOnEnter)
                BeginBlockedSlidersRampIfApplicable();

            return;
        }

        StopEdgePlayCoroutines();
        var prevPlay = _currentState;
        if (prevPlay == MinimapEdgeState.Blocked && next != MinimapEdgeState.Blocked)
            StopBlockedSlidersRamp(restore: true);
        _currentState = next;
        if (next != MinimapEdgeState.Appearing)
            _appearLineT = 1f;
        if (forceLog)
            Debug.Log($"[{name}] MinimapEdge state: {prevPlay} → {next}", this);

        if (next == MinimapEdgeState.MovingAlongEdge)
        {
            _stateAfterMovingCompletes = stateAfterMovingCompletes;
            _movingCoroutine = StartCoroutine(CoMovingAlongEdge());
        }
        else
        {
            _stateAfterMovingCompletes = MinimapEdgeState.Idle;
            if (next == MinimapEdgeState.Appearing)
            {
                _appearLineT = 0f;
                _onAppearingIdleComplete = onAppearingIdleComplete;
                _appearingCoroutine = StartCoroutine(CoAppearingThenIdle());
            }
            else
            {
                _onAppearingIdleComplete = null;
            }
        }

        if (next == MinimapEdgeState.Blocked)
        {
            if (Application.isPlaying && !suppressBlockedSlidersRampOnEnter)
                BeginBlockedSlidersRampIfApplicable();
        }

        ApplyCombinedVisual();
    }

    private float ComputeTravelDurationSeconds()
    {
        if (edgeTravelVideoClip != null && edgeTravelVideoClip.length > 1e-5)
            return (float)edgeTravelVideoClip.length;
        return Mathf.Max(0.01f, movingAlongEdgeDuration);
    }

    private void BeginMovingAlongPresentation()
    {
        EndMovingAlongPresentation();

        if (travelMoveVisual != null && startAnchor != null)
        {
            travelMoveVisual.SetActive(true);
            travelMoveVisual.transform.position = startAnchor.position;
        }

        _edgeTravelVideoPlaying = edgeTravelVideoClip != null &&
                                  GameManager.Instance != null &&
                                  GameManager.Instance.TryPlayMinimapEdgeTravelVideo(edgeTravelVideoClip);
    }

    private void UpdateTravelMarkerPosition(float t01)
    {
        if (travelMoveVisual == null || startAnchor == null || endAnchor == null)
            return;
        travelMoveVisual.transform.position = Vector3.Lerp(
            startAnchor.position,
            endAnchor.position,
            Mathf.Clamp01(t01));
    }

    private void EndMovingAlongPresentation()
    {
        if (travelMoveVisual != null)
            travelMoveVisual.SetActive(false);

        if (_edgeTravelVideoPlaying && GameManager.Instance != null)
            GameManager.Instance.StopMinimapEdgeTravelVideo();
        _edgeTravelVideoPlaying = false;
    }

    private IEnumerator CoMovingAlongEdge()
    {
        float duration = ComputeTravelDurationSeconds();
        var gradientDriver = ResolveLineGradientDriver();
        float sliderTarget = Mathf.Clamp(travelAlongEdgeSliderAcTargetPercent, 0f, 100f);
        if (gradientDriver != null)
        {
            _sliderAcAtTravelStart = gradientDriver.GetSliderAC();
            _travelSliderAcTweenActive = true;
        }
        else
            _travelSliderAcTweenActive = false;

        BeginMovingAlongPresentation();

        float t = 0f;
        while (t < duration)
        {
            float u = duration > 1e-6f ? t / duration : 1f;
            UpdateTravelMarkerPosition(u);
            if (gradientDriver != null)
                gradientDriver.SetSliderAC(Mathf.Lerp(_sliderAcAtTravelStart, sliderTarget, u));
            t += Time.deltaTime;
            yield return null;
        }

        UpdateTravelMarkerPosition(1f);
        if (gradientDriver != null)
            gradientDriver.SetSliderAC(sliderTarget);
        _travelSliderAcTweenActive = false;

        EndMovingAlongPresentation();

        _movingCoroutine = null;
        var landed = _stateAfterMovingCompletes;
        _stateAfterMovingCompletes = MinimapEdgeState.Idle;
        _currentState = landed;
        Debug.Log($"[{name}] MinimapEdge state: MovingAlongEdge → {landed} (timer done)", this);
        ApplyCombinedVisual();
    }

    /// <summary>
    /// Вторая фаза «блокировки» линии (ramp Slider AB/AC за <see cref="blockedSlidersRampDuration"/>), как у альтернативных рёбер в Blocked.
    /// Для ребра маршрута после MovingAlong конец в <see cref="NodeMapState.Selected"/> — <see cref="BeginBlockedSlidersRampIfApplicable"/> не сработает; вызывать из <see cref="GameManager.CoMapSelectionTravel"/>.
    /// </summary>
    public void PlayBlockedSlidersRampAfterMapRouteTravel()
    {
        if (!Application.isPlaying || _currentState != MinimapEdgeState.Blocked)
            return;
        BeginBlockedSlidersRamp();
    }

    /// <summary>
    /// Ramp AB/AC при <see cref="MinimapEdgeState.Blocked"/>, если конец на карте в <see cref="NodeMapState.Blocked"/>.
    /// Вызывается с реестра после общей задержки для пакета альтернативных рёбер (без рассинхрона по отдельным корутинам на каждом ребре).
    /// </summary>
    public void TryBeginBlockedSlidersRampSecondPhase()
    {
        if (!Application.isPlaying || _currentState != MinimapEdgeState.Blocked)
            return;
        BeginBlockedSlidersRampIfApplicable();
    }

    private IEnumerator CoAppearingThenIdle()
    {
        float d = appearingToIdleDuration <= 0f ? 1f : appearingToIdleDuration;
        float elapsed = 0f;
        _appearLineT = 0f;
        RefreshLinePositions();

        while (elapsed < d)
        {
            _appearLineT = d > 1e-6f ? Mathf.Clamp01(elapsed / d) : 1f;
            RefreshLinePositions();
            elapsed += Time.deltaTime;
            yield return null;
        }

        _appearLineT = 1f;
        RefreshLinePositions();

        _appearingCoroutine = null;
        if (_currentState != MinimapEdgeState.Appearing)
            yield break;
        _currentState = MinimapEdgeState.IdleRevealed;
        ApplyCombinedVisual();
        Debug.Log($"[{name}] MinimapEdge state: Appearing → {_currentState} (growth done)", this);
        var done = _onAppearingIdleComplete;
        _onAppearingIdleComplete = null;
        done?.Invoke();
    }

    private void StopEdgePlayCoroutines()
    {
        StopMovingIfAny();
        StopAppearingIfAny();
    }

    private void StopMovingIfAny()
    {
        if (_movingCoroutine == null)
            return;
        if (_travelSliderAcTweenActive)
        {
            var d = ResolveLineGradientDriver();
            if (d != null)
                d.SetSliderAC(_sliderAcAtTravelStart);
            _travelSliderAcTweenActive = false;
        }

        EndMovingAlongPresentation();
        StopCoroutine(_movingCoroutine);
        _movingCoroutine = null;
    }

    private void StopAppearingIfAny()
    {
        if (_appearingCoroutine == null)
            return;
        StopCoroutine(_appearingCoroutine);
        _appearingCoroutine = null;
        _onAppearingIdleComplete = null;
    }

    private LineRendererGradientPropertyDriver ResolveLineGradientDriver()
    {
        if (lineGradientDriver != null)
            return lineGradientDriver;
        if (lineRenderer == null)
            return null;
        return lineRenderer.GetComponent<LineRendererGradientPropertyDriver>();
    }

    private void ApplyBlockedSlidersInstantEditor()
    {
        var driver = ResolveLineGradientDriver();
        if (driver == null)
            return;
        driver.SetSliderAB(100f);
        driver.SetSliderAC(100f);
    }

    /// <summary>
    /// Ramp слайдеров только когда конец ребра на карте реально <see cref="NodeMapState.Blocked"/> (не при <see cref="NodeMapState.Selected"/> и т.д.).
    /// Без ToNode — разрешаем (ребро без привязки к ноде конца).
    /// </summary>
    private bool IsEndNodeMapRootBlockedForSliders()
    {
        if (toNode == null)
            return true;
        var endRoot = toNode.SelectionOwner;
        return endRoot != null && endRoot.CurrentState == NodeMapState.Blocked;
    }

    private void BeginBlockedSlidersRampIfApplicable()
    {
        if (!IsEndNodeMapRootBlockedForSliders())
            return;
        BeginBlockedSlidersRamp();
    }

    private void BeginBlockedSlidersRamp()
    {
        if (!Application.isPlaying)
            return;

        StopBlockedSlidersRamp(restore: false);
        var driver = ResolveLineGradientDriver();
        if (driver == null)
            return;

        _sliderAbBeforeBlocked = driver.GetSliderAB();
        _sliderAcBeforeBlocked = driver.GetSliderAC();
        float dur = ComputeBlockedSlidersRampDurationForPlay();
        const float target = 100f;
        _blockedSlidersCoroutine = StartCoroutine(CoBlockedSlidersRamp(driver, dur, target));
    }

    /// <summary>В Play: базовая длительность минус случайная доля до <see cref="blockedSlidersRampDurationPlayRandomSubtractMax"/>; вне Play — базовая.</summary>
    private float ComputeBlockedSlidersRampDurationForPlay()
    {
        float b = Mathf.Max(0.01f, blockedSlidersRampDuration);
        if (!Application.isPlaying)
            return b;
        float subtractFrac = UnityEngine.Random.Range(0f, blockedSlidersRampDurationPlayRandomSubtractMax);
        return Mathf.Max(0.01f, b * (1f - subtractFrac));
    }

    private void StopBlockedSlidersRamp(bool restore)
    {
        AbortBlockedRampNotifyIfNeeded();
        if (_blockedSlidersCoroutine != null)
        {
            StopCoroutine(_blockedSlidersCoroutine);
            _blockedSlidersCoroutine = null;
        }

        if (!restore)
            return;

        var driver = ResolveLineGradientDriver();
        if (driver == null)
            return;
        driver.SetSliderAB(_sliderAbBeforeBlocked);
        driver.SetSliderAC(_sliderAcBeforeBlocked);
    }

    private void AbortBlockedRampNotifyIfNeeded()
    {
        if (!_blockedRampNotifyCountedWithEndRoot)
            return;
        _blockedRampNotifyEndRoot?.MapNotifyIncomingBlockedEdgeVisualRampAborted();
        _blockedRampNotifyCountedWithEndRoot = false;
        _blockedRampNotifyEndRoot = null;
    }

    private void CompleteBlockedRampNotifyIfNeeded()
    {
        if (!_blockedRampNotifyCountedWithEndRoot)
            return;
        _blockedRampNotifyEndRoot?.MapNotifyIncomingBlockedEdgeVisualRampComplete();
        _blockedRampNotifyCountedWithEndRoot = false;
        _blockedRampNotifyEndRoot = null;
    }

    private IEnumerator CoBlockedSlidersRamp(LineRendererGradientPropertyDriver driver, float fullDuration, float target)
    {
        _blockedEdgeLineAcFullNotifiedToEndNode = false;
        _blockedRampNotifyEndRoot = toNode?.SelectionOwner;
        if (_blockedRampNotifyEndRoot != null)
        {
            _blockedRampNotifyEndRoot.MapNotifyIncomingBlockedEdgeVisualRampBegin();
            _blockedRampNotifyCountedWithEndRoot = true;
        }

        float acPhase = Mathf.Max(1e-5f, fullDuration / 3f);
        float ab0 = _sliderAbBeforeBlocked;
        float ac0 = _sliderAcBeforeBlocked;
        float t = 0f;

        while (_currentState == MinimapEdgeState.Blocked && t < fullDuration)
        {
            t += Time.deltaTime;
            float uAc = Mathf.Clamp01(t / acPhase);
            float uAb = Mathf.Clamp01(t / fullDuration);
            driver.SetSliderAC(Mathf.Lerp(ac0, target, uAc));
            driver.SetSliderAB(Mathf.Lerp(ab0, target, uAb));
            if (!_blockedEdgeLineAcFullNotifiedToEndNode &&
                _blockedRampNotifyEndRoot != null &&
                uAc >= 1f - 1e-5f)
            {
                _blockedRampNotifyEndRoot.MapNotifyIncomingBlockedEdgeRampAcReachedOnLine();
                _blockedEdgeLineAcFullNotifiedToEndNode = true;
            }

            yield return null;
        }

        if (_currentState == MinimapEdgeState.Blocked)
        {
            driver.SetSliderAC(target);
            driver.SetSliderAB(target);
            CompleteBlockedRampNotifyIfNeeded();
        }
        else
            AbortBlockedRampNotifyIfNeeded();

        _blockedSlidersCoroutine = null;
    }

    /// <summary>
    /// Карта вне Play всегда «разрешает» линию для вёрстки.
    /// В Play: линия по <see cref="_mapOutgoingLineVisible"/> (исходящие от выбранной / стартов без выбора), при <see cref="MinimapEdgeState.Blocked"/>,
    /// при <see cref="MinimapEdgeState.Selected"/> (ребро к текущей выбранной ноде), и кратко при <see cref="MinimapEdgeState.IdleRevealed"/> с тем же концом — чтобы путь прибытия не гас после выбора ноды.
    /// <see cref="MinimapEdgeState.Disabled"/> выключает линию всегда. Цвет линии — из материала / <see cref="LineRendererGradientPropertyDriver"/>, не из стейта.
    /// </summary>
    public void ApplyCombinedVisual()
    {
        if (lineRenderer == null)
            return;

        bool mapAllows = !Application.isPlaying ||
                         _mapOutgoingLineVisible ||
                         _currentState == MinimapEdgeState.Blocked ||
                         _currentState == MinimapEdgeState.Selected ||
                         MapSelectionShowsIncomingIdleRevealedEdge();
        bool stateShowsLine = _currentState != MinimapEdgeState.Disabled;
        lineRenderer.enabled = mapAllows && stateShowsLine;
    }

    /// <summary>В Play: ребро в IdleRevealed с концом на <see cref="GameManager.CurrentSelectedMapNode"/> — показывать линию до смены стейта на Selected в реестре.</summary>
    private bool MapSelectionShowsIncomingIdleRevealedEdge()
    {
        if (!Application.isPlaying || _currentState != MinimapEdgeState.IdleRevealed || toNode == null)
            return false;
        var gm = GameManager.Instance;
        var sel = gm != null ? gm.CurrentSelectedMapNode : null;
        return sel != null && toNode.SelectionOwner == sel;
    }

    [ContextMenu("Refresh line")]
    public void RefreshLinePositions()
    {
        ApplyAnchorsFollowIfNeeded();

        if (lineRenderer == null || startAnchor == null || endAnchor == null)
            return;

        Vector3 a = startAnchor.position;
        Vector3 b = endAnchor.position;
        Vector3 delta = b - a;
        float len = delta.magnitude;

        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;

        if (len < 1e-6f)
        {
            lineRenderer.SetPosition(0, a);
            lineRenderer.SetPosition(1, a);
            ApplyMiddle(a);
            return;
        }

        Vector3 dir = delta / len;

        float along0 = Mathf.Clamp(startInset, 0f, len);
        float along1 = len - Mathf.Clamp(endInset, 0f, len);
        if (along0 >= along1)
        {
            float mid = len * 0.5f;
            float eps = Mathf.Max(1e-5f, len * 1e-4f);
            along0 = Mathf.Max(0f, mid - eps);
            along1 = Mathf.Min(len, mid + eps);
            if (along0 >= along1)
                along1 = Mathf.Min(len, along0 + 1e-5f);
        }

        Vector3 p0 = a + dir * along0;
        Vector3 p1 = a + dir * along1;

        float growT = (!Application.isPlaying || _currentState != MinimapEdgeState.Appearing) ? 1f : Mathf.Clamp01(_appearLineT);
        Vector3 p1Draw = Vector3.Lerp(p0, p1, growT);

        lineRenderer.SetPosition(0, p0);
        lineRenderer.SetPosition(1, p1Draw);
        ApplyMiddle((p0 + p1Draw) * 0.5f);
    }

    private void ApplyMiddle(Vector3 worldMid)
    {
        if (middlePoint == null)
            return;
        middlePoint.position = worldMid;
    }

    private void ApplyAnchorsFollowIfNeeded()
    {
        if (!anchorsFollowLinkedNodes)
            return;
        if (fromNode == null || toNode == null || startAnchor == null || endAnchor == null)
            return;

        Transform ft = fromNode.transform;
        Transform tt = toNode.transform;
        startAnchor.SetPositionAndRotation(ft.position, ft.rotation);
        endAnchor.SetPositionAndRotation(tt.position, tt.rotation);
        startAnchor.localScale = Vector3.one;
        endAnchor.localScale = Vector3.one;
    }
}

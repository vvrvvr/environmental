using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

public partial class Node : MonoBehaviour
{
    [Header("Main Visual")]
    [Tooltip("Основной спрайт ноды, который реагирует на наведение и клики.")]
    [SerializeField] private SpriteRenderer mainSprite;

    [Header("Hover Animation")]
    [Tooltip("Во сколько раз увеличить основной спрайт при наведении.")]
    [SerializeField, Min(1f)] private float hoverScaleMultiplier = 1.08f;
    [Tooltip("Длительность увеличения при наведении.")]
    [SerializeField, Min(0f)] private float hoverEnterDuration = 0.12f;
    [Tooltip("Длительность возврата к обычному размеру.")]
    [SerializeField, Min(0f)] private float hoverExitDuration = 0.12f;
    [SerializeField] private Ease hoverEnterEase = Ease.OutQuad;
    [SerializeField] private Ease hoverExitEase = Ease.OutQuad;
    
    [Header("Click Animation")]
    [Tooltip("Во сколько раз сжимать текущий масштаб при клике.")]
    [SerializeField, Range(0.8f, 1f)] private float clickScaleMultiplier = 0.92f;
    [Tooltip("Длительность сжатия при клике.")]
    [SerializeField, Min(0f)] private float clickPressDuration = 0.08f;
    [Tooltip("Длительность возврата к исходному масштабу после клика.")]
    [SerializeField, Min(0f)] private float clickReleaseDuration = 0.1f;
    [SerializeField] private Ease clickPressEase = Ease.OutQuad;
    [SerializeField] private Ease clickReleaseEase = Ease.OutBack;

    [Header("Selection")]
    [Tooltip("Кольцо/селектор: показывает выбранную ноду. Включается при выборе.")]
    [SerializeField] private SpriteRenderer selectionRing;
    [Tooltip("Как быстро кольцо масштабируется от 0 до исходного размера при выборе.")]
    [SerializeField, Min(0f)] private float selectionRingAppearDuration = 0.1f;
    [SerializeField] private Ease selectionRingAppearEase = Ease.OutQuad;
    [Tooltip("Как быстро кольцо сжимается к 0 при снятии выбора (состояние Deselected). Используется оркестратором перехода по карте до Blocked.")]
    [SerializeField, Min(0f)] private float selectionRingDisappearDuration = 0.12f;
    [SerializeField] private Ease selectionRingDisappearEase = Ease.InQuad;

    [Header("Minimap")]
    [Tooltip("Стартовая нода карты: при старте Visible; сразу видны исходящие рёбра и ноды на их концах (один шаг). Остальные корни карты — Inactive. См. GameManager.")]
    [SerializeField] private bool isMinimapStartNode;

    [Header("Minimap video")]
    [Tooltip("Ролик мини-карты для этой ноды (уникальный). Воспроизведение ведёт GameManager.")]
    [SerializeField] private VideoClip minimapVideoClip;

    [Tooltip("Палитра графа: общие Color A/B/C для кнопки «Применить цвета к нодам» в ассете палитры (SpriteRendererGradientPropertyDriver). Тот же ассет, что на MinimapEdgeRegistry.")]
    [SerializeField] private MinimapGraphVisualPalette mapVisualPalette;

    [Header("Map state: Selected (gradient sliders)")]
    [Tooltip(
        "В Play: при входе ноды карты в NodeMapState.Selected у всех SpriteRendererGradientPropertyDriver под этой нодой Slider AC за это время линейно идёт от текущего значения до 100. При смене выбора на карте AC не сбрасывается. Только у корня группы (без groupParent); у дочерних нод группы не запускается.")]
    [SerializeField, Min(0.01f)]
    private float selectedMapSliderAcRampDuration = 0.35f;

    [Header("Map state: Blocked (gradient sliders)")]
    [Tooltip(
        "Длительность полной секвенции блокирования ноды на карте (сек): после того как все входящие рёбра в Blocked завершили свою секвенцию слайдеров на линии, у ноды Slider AC и AB за это время линейно идут от текущих значений до 100%. Только корень группы.")]
    [SerializeField, Min(0.01f)]
    private float mapNodeBlockedVisualSequenceDuration = 0.5f;

    [Tooltip(
        "В Play, нода в Blocked: когда у входящего заблокированного ребра Slider AC на линии доходит до 100%, у ноды (корень группы) Slider AC за это время линейно идёт от 0 до 100 (синхрон с «пожелтением» ветки до конца).")]
    [SerializeField, Min(0.01f)]
    private float mapNodeBlockedIncomingEdgeAcFullSyncAcRampDuration = 0.25f;

    /// <summary>Длительность секвенции блокирования ноды (слайдеры AC/AB на спрайтах); минимум 0.01 с.</summary>
    public float MapNodeBlockedSequenceDuration => Mathf.Max(0.01f, mapNodeBlockedVisualSequenceDuration);

    /// <summary>Длительность AC ноды 0→100 при достижении AC=100 на входящем ребре в Blocked; минимум 0.01 с.</summary>
    public float MapNodeBlockedIncomingEdgeAcFullSyncAcRampDuration =>
        Mathf.Max(0.01f, mapNodeBlockedIncomingEdgeAcFullSyncAcRampDuration);

    /// <summary>
    /// У корня карты: после завершения <see cref="CoMapNodeBlockedSlidersRamp"/> (слайдеры ноды в Blocked дошли до 100%).
    /// Внешние подписчики (например travel по ребру) — см. <see cref="GameManager.CoMapSelectionTravel"/>.
    /// </summary>
    public event Action MapPostFullyBlockedGradientRampCompleted;

    /// <summary>
    /// Корень карты в Play: синхронно в начале основного ramp brown по слайдерам ноды (AB/AC), до первого <c>yield</c> корутины этого ramp.
    /// Для привязки визуала исходящих рёбер к шкале времени именно ноды — см. <see cref="GameManager"/>.
    /// </summary>
    public event Action MapNodeBlockedMainBrownRampStarted;

    /// <summary>Стартовая точка обхода мини-карты (флаг в инспекторе).</summary>
    public bool IsMinimapStartNode => isMinimapStartNode;

    /// <summary>Длительность сжатия кольца при <see cref="NodeMapState.Deselected"/>; оркестратор перехода по карте ждёт её перед <see cref="NodeMapState.Blocked"/>.</summary>
    public float SelectionRingDisappearDuration => selectionRingDisappearDuration;

    /// <summary>Клип мини-карты; для группы задаётся на родительской ноде (логический выбор — SelectionOwner).</summary>
    public VideoClip MinimapVideoClip => minimapVideoClip;

    /// <summary>Палитра визуала карты для этой ноды.</summary>
    public MinimapGraphVisualPalette MapVisualPalette => mapVisualPalette;

    /// <summary>Назначить палитру и применить A/B/C к <see cref="SpriteRendererGradientPropertyDriver"/> на ноде (если палитра не null).</summary>
    public void SetMapVisualPalette(MinimapGraphVisualPalette palette)
    {
        mapVisualPalette = palette;
        ApplyPaletteGradientDriversFromPalette();
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>
    /// Копирует Color A/B/C из <see cref="mapVisualPalette"/> во все <see cref="SpriteRendererGradientPropertyDriver"/> под этой нодой.
    /// </summary>
    public void RefreshMapVisualPaletteFromCurrentState()
    {
        ApplyPaletteGradientDriversFromPalette();
    }

    /// <summary>Копирует A/B/C из <see cref="mapVisualPalette"/> во все <see cref="SpriteRendererGradientPropertyDriver"/> в иерархии ноды.</summary>
    public void ApplyPaletteGradientDriversFromPalette()
    {
        if (mapVisualPalette == null)
            return;
        mapVisualPalette.GetNodeGradientColors(out var ca, out var cb, out var cc);
        var drivers = GetComponentsInChildren<SpriteRendererGradientPropertyDriver>(true);
        for (var i = 0; i < drivers.Length; i++)
        {
            if (drivers[i] != null)
                drivers[i].SetColorsABC(ca, cb, cc);
        }
    }

    [Header("UI")]
    [Tooltip("Оставшееся время до конца ролика (сек, один знак). Пусто, пока нода не выбрана на карте или не играет её клип.")]
    [SerializeField] private TMP_Text remainingTimeText;

    private Collider[] _cachedColliders = System.Array.Empty<Collider>();

    private readonly HashSet<Node> overlappingNeighborNodes = new HashSet<Node>();
    private bool mouseOver;
    private Transform mainSpriteTransform;
    private Vector3 mainSpriteBaseScale;
    private Tween hoverTween;
    private Tween clickTween;
    private Tween selectionRingTween;
    private Coroutine _selectedMapSliderAcRampCoroutine;
    private int _incomingBlockedEdgeVisualRampsActive;
    private Coroutine _mapNodeBlockedSlidersCoroutine;
    private Coroutine _mapNodeBlockedEdgeAcSyncCoroutine;
    private Camera cachedMapCamera;
    private Transform selectionRingTransform;
    private Vector3 selectionRingBaseScale;

    private void Awake()
    {
        if (mainSprite != null)
        {
            mainSpriteTransform = mainSprite.transform;
            mainSpriteBaseScale = mainSpriteTransform.localScale;
        }

        _cachedColliders = GetComponentsInChildren<Collider>(true);

        CacheSelectionRingBaseScale();

        TryCacheMapCamera();
    }

    private void TryCacheMapCamera()
    {
        if (cachedMapCamera != null)
            return;
        if (GameManager.Instance != null)
            cachedMapCamera = GameManager.Instance.MapCamera;
    }

    private void OnDisable()
    {
        KillHoverTween();
        KillClickTween();
        KillSelectionRingTween();
        KillStateDelayedTween();
        StopSelectedMapSliderAcRamp();
        StopMapNodeBlockedSlidersSequence(resetSlidersToZero: true);
        ApplyBaseScaleImmediate();
        mouseOver = false;
        ClearRemainingTimeText();
    }

    private void Update()
    {
        ProcessInput();
        UpdateRemainingTimeText();
    }

    private void UpdateRemainingTimeText()
    {
        if (remainingTimeText == null)
            return;

        if (GameManager.Instance == null ||
            !GameManager.Instance.TryGetRemainingTimeForNodeDisplay(this, out float seconds))
        {
            remainingTimeText.text = string.Empty;
            return;
        }

        remainingTimeText.text = seconds.ToString("F1", CultureInfo.InvariantCulture);
    }

    private void ClearRemainingTimeText()
    {
        if (remainingTimeText != null)
            remainingTimeText.text = string.Empty;
    }

    private void ProcessInput()
    {
        if (!ShouldProcessMapPointer())
            return;

        bool over = IsMouseHoveringThisNode();
        if (over != mouseOver)
        {
            if (over)
                OnNodePointerEnter();
            else
                OnNodePointerExit();
        }
        mouseOver = over;
        if (over)
            OnNodePointerHold();
        if (over && Input.GetMouseButtonDown(0))
            OnNodePointerClick();

        if (!Input.GetKeyDown(KeyCode.Space))
            return;
        foreach (var n in overlappingNeighborNodes)
            if (n != null)
                Debug.Log($"trigger neighbor: {n.name}", n);
    }

    protected virtual void OnNodePointerEnter()
    {
        AnimateMainSpriteScale(mainSpriteBaseScale * hoverScaleMultiplier, hoverEnterDuration, hoverEnterEase);
        // Debug.Log("enter");
    }

    protected virtual void OnNodePointerHold()
    {
        // Debug.Log("hold");
    }

    protected virtual void OnNodePointerExit()
    {
        AnimateMainSpriteScale(mainSpriteBaseScale, hoverExitDuration, hoverExitEase);
        // Debug.Log("exit");
    }

    protected virtual void OnNodePointerClick()
    {
        PlayClickAnimation();
        Node owner = SelectionOwner;
        if (GameManager.Instance != null && GameManager.Instance.HandleMapNodeClick(owner, this))
            return;
        SetState(NodeMapState.Selected);
        Debug.Log($"click → selection owner: {owner.name}", owner);
    }

    private bool IsMouseHoveringThisNode()
    {
        TryCacheMapCamera();
        if (cachedMapCamera == null)
            return false;

        Ray ray = cachedMapCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, ~0, QueryTriggerInteraction.Collide))
            return false;

        return hit.collider != null && hit.collider.GetComponentInParent<Node>() == this;
    }

    private void OnTriggerEnter(Collider other)
    {
        Node neighbor = other.GetComponentInParent<Node>();
        if (neighbor == null || neighbor == this)
            return;

        overlappingNeighborNodes.Add(neighbor);
    }

    private void OnTriggerExit(Collider other)
    {
        Node neighbor = other.GetComponentInParent<Node>();
        if (neighbor == null)
            return;

        overlappingNeighborNodes.Remove(neighbor);
    }

    private void AnimateMainSpriteScale(Vector3 targetScale, float duration, Ease ease)
    {
        if (mainSpriteTransform == null)
            return;

        KillHoverTween();
        hoverTween = mainSpriteTransform.DOScale(targetScale, duration).SetEase(ease);
    }

    private void ApplyBaseScaleImmediate()
    {
        if (mainSpriteTransform == null)
            return;

        mainSpriteTransform.localScale = mainSpriteBaseScale;
    }

    private void KillHoverTween()
    {
        if (hoverTween == null)
            return;

        if (hoverTween.IsActive())
            hoverTween.Kill();
        hoverTween = null;
    }

    private void PlayClickAnimation()
    {
        if (mainSpriteTransform == null)
            return;

        KillHoverTween();
        KillClickTween();

        Vector3 startScale = mainSpriteTransform.localScale;
        Vector3 pressedScale = startScale * clickScaleMultiplier;
        Sequence sequence = DOTween.Sequence();
        sequence.Append(mainSpriteTransform.DOScale(pressedScale, clickPressDuration).SetEase(clickPressEase));
        sequence.Append(mainSpriteTransform.DOScale(startScale, clickReleaseDuration).SetEase(clickReleaseEase));
        clickTween = sequence;
    }

    private void KillClickTween()
    {
        if (clickTween == null)
            return;

        if (clickTween.IsActive())
            clickTween.Kill();
        clickTween = null;
    }

    private void CacheSelectionRingBaseScale()
    {
        if (selectionRing == null)
            return;

        selectionRingTransform = selectionRing.transform;
        selectionRingBaseScale = selectionRingTransform.localScale;
    }

    private void ApplySelectionRingDeselectedImmediate()
    {
        if (selectionRingTransform == null)
            return;

        KillSelectionRingTween();
        selectionRingTransform.localScale = Vector3.zero;
        selectionRing.gameObject.SetActive(false);
    }

    private void ShowSelectionRingPopIn()
    {
        if (selectionRingTransform == null)
            return;

        KillSelectionRingTween();
        selectionRing.gameObject.SetActive(true);
        selectionRingTransform.localScale = Vector3.zero;
        selectionRingTween = selectionRingTransform
            .DOScale(selectionRingBaseScale, selectionRingAppearDuration)
            .SetEase(selectionRingAppearEase);
    }

    private void HideSelectionRing()
    {
        if (selectionRingTransform == null)
            return;

        KillSelectionRingTween();
        selectionRingTransform.localScale = Vector3.zero;
        selectionRing.gameObject.SetActive(false);
    }

    /// <summary>
    /// Сжимает кольцо от текущего масштаба к нулю, затем выключает объект. Если кольца нет или оно уже скрыто — сразу вызывает <paramref name="onComplete"/>.
    /// </summary>
    private void HideSelectionRingWithShrink(Action onComplete)
    {
        if (selectionRingTransform == null)
        {
            onComplete?.Invoke();
            return;
        }

        KillSelectionRingTween();

        if (!selectionRing.gameObject.activeSelf || selectionRingTransform.localScale.sqrMagnitude < 1e-8f)
        {
            selectionRingTransform.localScale = Vector3.zero;
            selectionRing.gameObject.SetActive(false);
            onComplete?.Invoke();
            return;
        }

        selectionRingTween = selectionRingTransform
            .DOScale(Vector3.zero, selectionRingDisappearDuration)
            .SetEase(selectionRingDisappearEase)
            .OnComplete(() =>
            {
                selectionRingTween = null;
                if (selectionRing != null)
                {
                    selectionRing.gameObject.SetActive(false);
                }

                onComplete?.Invoke();
            });
    }

    private void KillSelectionRingTween()
    {
        if (selectionRingTween == null)
            return;

        if (selectionRingTween.IsActive())
            selectionRingTween.Kill();
        selectionRingTween = null;
    }

    /// <summary>
    /// Корень карты (без родителя группы): в Play при <see cref="NodeMapState.Selected"/> — Slider AC с текущего до 100 за <see cref="selectedMapSliderAcRampDuration"/>.
    /// </summary>
    private void BeginSelectedMapSliderAcRampIfMapRoot()
    {
        if (!Application.isPlaying || groupParent != null)
            return;

        StopSelectedMapSliderAcRamp();
        _selectedMapSliderAcRampCoroutine = StartCoroutine(CoSelectedMapSliderAcRamp());
    }

    /// <summary>Остановить ramp выбора без изменения Slider AC (значение не откатывается при смене выбора на карте).</summary>
    private void StopSelectedMapSliderAcRamp()
    {
        if (_selectedMapSliderAcRampCoroutine == null)
            return;
        StopCoroutine(_selectedMapSliderAcRampCoroutine);
        _selectedMapSliderAcRampCoroutine = null;
    }

    private IEnumerator CoSelectedMapSliderAcRamp()
    {
        var drivers = GetComponentsInChildren<SpriteRendererGradientPropertyDriver>(true);
        var n = drivers.Length;
        var ac0 = new float[n];
        for (var i = 0; i < n; i++)
        {
            if (drivers[i] != null)
                ac0[i] = drivers[i].GetSliderAC();
        }

        float dur = Mathf.Max(0.01f, selectedMapSliderAcRampDuration);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = dur > 1e-6f ? Mathf.Clamp01(t / dur) : 1f;
            for (var i = 0; i < n; i++)
            {
                if (drivers[i] != null)
                    drivers[i].SetSliderAC(Mathf.Lerp(ac0[i], 100f, u));
            }

            yield return null;
        }

        for (var i = 0; i < n; i++)
        {
            if (drivers[i] != null)
                drivers[i].SetSliderAC(100f);
        }

        _selectedMapSliderAcRampCoroutine = null;
    }

    /// <summary>Сброс счётчика входящих ramp Blocked-рёбер при первом входе ноды в Blocked (корень карты).</summary>
    public void MapResetIncomingBlockedEdgeRampTallyOnEnterBlocked()
    {
        if (groupParent != null || !Application.isPlaying)
            return;
        _incomingBlockedEdgeVisualRampsActive = 0;
    }

    /// <summary>Ребро начало секвенцию слайдеров Blocked к этой ноде (корень ToNode).</summary>
    public void MapNotifyIncomingBlockedEdgeVisualRampBegin()
    {
        if (!Application.isPlaying || groupParent != null)
            return;
        _incomingBlockedEdgeVisualRampsActive++;
    }

    /// <summary>Ребро прервало ramp до завершения.</summary>
    public void MapNotifyIncomingBlockedEdgeVisualRampAborted()
    {
        if (groupParent != null)
            return;
        if (_incomingBlockedEdgeVisualRampsActive > 0)
            _incomingBlockedEdgeVisualRampsActive--;
        StopMapNodeBlockedEdgeAcSyncRamp();
    }

    /// <summary>
    /// Во время ramp Blocked на входящем ребре: Slider AC на линии достиг 100% — запуск AC ноды 0→100 за <see cref="MapNodeBlockedIncomingEdgeAcFullSyncAcRampDuration"/>.
    /// Только корень группы и только при <see cref="NodeMapState.Blocked"/>.
    /// </summary>
    public void MapNotifyIncomingBlockedEdgeRampAcReachedOnLine()
    {
        if (!Application.isPlaying || groupParent != null || CurrentState != NodeMapState.Blocked)
            return;

        StopMapNodeBlockedEdgeAcSyncRamp();
        _mapNodeBlockedEdgeAcSyncCoroutine = StartCoroutine(CoMapNodeBlockedEdgeAcSyncRamp());
    }

    /// <summary>Ребро завершило полную секвенцию Blocked (AB=100); при нуле активных ramp и ноде в Blocked — старт секвенции слайдеров ноды.</summary>
    public void MapNotifyIncomingBlockedEdgeVisualRampComplete()
    {
        if (groupParent != null)
            return;
        if (_incomingBlockedEdgeVisualRampsActive > 0)
            _incomingBlockedEdgeVisualRampsActive--;
        if (!Application.isPlaying)
            return;
        if (_incomingBlockedEdgeVisualRampsActive == 0 && CurrentState == NodeMapState.Blocked)
            BeginMapNodeBlockedSlidersSequenceIfMapRoot();
    }

    /// <summary>
    /// В <see cref="NodeMapState.Blocked"/>: если нет ожидаемых ramp входящих Blocked-рёбер, сразу запускает brown-секвенцию ноды.
    /// Иначе ждут <see cref="MapNotifyIncomingBlockedEdgeVisualRampComplete"/>. Нужно при уходе со стартовой ноды без prior travel (нет pending arrival).
    /// </summary>
    public void MapTryBeginBlockedNodeSlidersIfNoIncomingEdgeRampPending()
    {
        if (!Application.isPlaying || groupParent != null || CurrentState != NodeMapState.Blocked)
            return;
        if (_incomingBlockedEdgeVisualRampsActive != 0)
            return;
        BeginMapNodeBlockedSlidersSequenceIfMapRoot();
    }

    private void BeginMapNodeBlockedSlidersSequenceIfMapRoot()
    {
        if (!Application.isPlaying || groupParent != null)
            return;
        StopMapNodeBlockedSlidersSequence(resetSlidersToZero: false);
        _mapNodeBlockedSlidersCoroutine = StartCoroutine(CoMapNodeBlockedSlidersRamp());
    }

    private void StopMapNodeBlockedEdgeAcSyncRamp()
    {
        if (_mapNodeBlockedEdgeAcSyncCoroutine == null)
            return;
        StopCoroutine(_mapNodeBlockedEdgeAcSyncCoroutine);
        _mapNodeBlockedEdgeAcSyncCoroutine = null;
    }

    private void StopMapNodeBlockedSlidersSequence(bool resetSlidersToZero)
    {
        StopMapNodeBlockedEdgeAcSyncRamp();
        if (_mapNodeBlockedSlidersCoroutine != null)
        {
            StopCoroutine(_mapNodeBlockedSlidersCoroutine);
            _mapNodeBlockedSlidersCoroutine = null;
        }

        if (!resetSlidersToZero || groupParent != null)
            return;

        var drivers = GetComponentsInChildren<SpriteRendererGradientPropertyDriver>(true);
        for (var i = 0; i < drivers.Length; i++)
        {
            if (drivers[i] != null)
            {
                drivers[i].SetSliderAB(0f);
                drivers[i].SetSliderAC(0f);
            }
        }
    }

    private IEnumerator CoMapNodeBlockedSlidersRamp()
    {
        if (groupParent == null)
            MapNodeBlockedMainBrownRampStarted?.Invoke();

        var drivers = GetComponentsInChildren<SpriteRendererGradientPropertyDriver>(true);
        var n = drivers.Length;
        var ab0 = new float[n];
        var ac0 = new float[n];
        for (var i = 0; i < n; i++)
        {
            if (drivers[i] == null)
                continue;
            ab0[i] = drivers[i].GetSliderAB();
            ac0[i] = drivers[i].GetSliderAC();
        }

        float dur = MapNodeBlockedSequenceDuration;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = dur > 1e-6f ? Mathf.Clamp01(t / dur) : 1f;
            for (var i = 0; i < n; i++)
            {
                if (drivers[i] == null)
                    continue;
                drivers[i].SetSliderAB(Mathf.Lerp(ab0[i], 100f, u));
                drivers[i].SetSliderAC(Mathf.Lerp(ac0[i], 100f, u));
            }

            yield return null;
        }

        for (var i = 0; i < n; i++)
        {
            if (drivers[i] != null)
            {
                drivers[i].SetSliderAB(100f);
                drivers[i].SetSliderAC(100f);
            }
        }

        _mapNodeBlockedSlidersCoroutine = null;
        if (groupParent == null)
            MapPostFullyBlockedGradientRampCompleted?.Invoke();
    }

    private IEnumerator CoMapNodeBlockedEdgeAcSyncRamp()
    {
        var drivers = GetComponentsInChildren<SpriteRendererGradientPropertyDriver>(true);
        var n = drivers.Length;
        var ac0 = new float[n];
        for (var i = 0; i < n; i++)
        {
            if (drivers[i] != null)
                ac0[i] = drivers[i].GetSliderAC();
        }

        float dur = MapNodeBlockedIncomingEdgeAcFullSyncAcRampDuration;
        float t = 0f;
        while (t < dur)
        {
            if (CurrentState != NodeMapState.Blocked)
            {
                _mapNodeBlockedEdgeAcSyncCoroutine = null;
                yield break;
            }

            t += Time.deltaTime;
            float u = dur > 1e-6f ? Mathf.Clamp01(t / dur) : 1f;
            for (var i = 0; i < n; i++)
            {
                if (drivers[i] != null)
                    drivers[i].SetSliderAC(Mathf.Lerp(ac0[i], 100f, u));
            }

            yield return null;
        }

        for (var i = 0; i < n; i++)
        {
            if (drivers[i] != null)
                drivers[i].SetSliderAC(100f);
        }

        _mapNodeBlockedEdgeAcSyncCoroutine = null;
    }
}

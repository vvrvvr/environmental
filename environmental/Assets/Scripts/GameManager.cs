using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Глобальная точка доступа к настройкам сцены/игры. Камера карты назначается один раз в инспекторе.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Map")]
    [Tooltip("Камера, с которой строится луч для нод карты и прочего взаимодействия с картой.")]
    [SerializeField] private Camera mapCamera;

    [Tooltip("Рёбра мини-карты. Если пусто — ищется MinimapEdgeRegistry в сцене.")]
    [SerializeField] private MinimapEdgeRegistry minimapEdgeRegistry;

    [Tooltip("Писать в консоль каждое уведомление о смене состояния ноды на карте.")]
    [SerializeField] private bool logMapNodeStateChanges;

    [Header("Minimap discovery")]
    [Tooltip("Дебаг: все ноды карты Visible (кроме уже выбранной), можно кликнуть любую и выбрать. Выкл — достижимость только от нод с IsMinimapStartNode по рёбрам реестра.")]
    [SerializeField] private bool debugRevealFullMinimap;

    [Tooltip("Лог разметки стартовых нод / блокировки других стартов.")]
    [SerializeField] private bool logMinimapDiscovery;

    [Header("Minimap outgoing edge stagger")]
    [Tooltip(
        "Нижняя граница (сек): для каждого подходящего исходящего ребра отдельно берётся случайное значение между Min и Max. Если Min > Max в инспекторе, диапазон сортируется.")]
    [SerializeField, Min(0f)]
    private float outgoingEdgeAppearStaggerMin;

    [Tooltip("Верхняя граница (сек), включительно для Random.Range; у каждого ребра своё независимое случайное значение в [Min, Max].")]
    [SerializeField, Min(0f)]
    private float outgoingEdgeAppearStaggerMax = 0.35f;

    [Header("Minimap travel — outgoing block visuals")]
    [Tooltip(
        "Смещение момента визуальной блокировки исходящих от From рёбер (вторая фаза ramp + лёгкий AB линии маршрута) относительно brown ноды From (не ребра). " +
        "≥ 0: секунды после полного brown ноды — событие MapPostFullyBlockedGradientCompleted (0 — сразу). " +
        "< 0: относительно старта основной brown-секвенции ноды (MapNodeBlockedMainBrownRampStarted): ждём max(0, MapNodeBlockedSequenceDuration + значение); " +
        "например −0.1 при длительности 0.5 с — старт рёбер через 0.4 с после начала градиента на ноде (≈0.1 с до его конца).")]
    [SerializeField]
    private float outgoingBlockedEdgeVisualOffsetSeconds;

    [Header("Minimap video")]
    [Tooltip("Единый VideoPlayer для ролика выбранной ноды (выход на RawImage / Camera и т.д. настраивается на нём).")]
    [SerializeField] private VideoPlayer mapVideoPlayer;

    private Node _groupPlaylistRoot;
    private Node _groupPlaylistFocusNode;
    private bool _groupLoopHandlerRegistered;
    private VideoPlayer.EventHandler _groupLoopPointHandler;

    private bool _suppressMapTravelSelectionClear;
    private bool _mapSelectionTravelInProgress;
    private Node _mapTravelTargetRoot;
    private Coroutine _mapTravelCoroutine;

    /// <summary>
    /// From-нода travel: подписка на конец brown (<see cref="Node.MapPostFullyBlockedGradientRampCompleted"/>, offset ≥ 0) или на старт основной brown (<see cref="Node.MapNodeBlockedMainBrownRampStarted"/>, offset &lt; 0), плюс корутина ожидания/запуска визуалов исходящих рёбер.
    /// </summary>
    private Node _mapTravelPostFullyBlockedListenerRoot;
    private Action _mapTravelPostFullyBlockedForOutgoingBlockVisuals;
    private Action _mapTravelOnFromMainBrownRampStarted;
    private Coroutine _outgoingBlockedEdgeVisualsCoroutine;

    /// <summary>Ребро последнего прибытия по карте: в Blocked переводится при следующем travel с его конечной ноды, а не сразу после MovingAlong.</summary>
    private MinimapEdge _pendingArrivalEdgeToBlockWhenLeavingEndNode;

    [Header("Debug")]
    [Tooltip("Запасная нода для горячих клавиш, пока ни одна нода не вызвала NotifyNodeMapStateChanged (иначе используется Last Map State Source Node).")]
    public Node debugTargetNode;

    [Tooltip("Включить переключение состояний ноды по цифрам 1–9.")]
    [SerializeField] private bool enableNodeStateHotkeys = true;

    private bool _lastDebugRevealFullMinimap;

    /// <summary>Гасит повторный вход из <see cref="NotifyNodeMapStateChanged"/> при массовых <see cref="Node.ForceMapState"/>.</summary>
    private bool _suppressMinimapPostNotify;

    private readonly HashSet<Node> _scratchNodesB = new HashSet<Node>();

    /// <summary>Корни карты с <see cref="Node.IsMinimapStartNode"/>; обновляется в <see cref="RefreshMinimapDiscoveryFromStartNodes"/> для линий при отсутствии выбора.</summary>
    private readonly HashSet<Node> _minimapStartRootCache = new HashSet<Node>();

    public Camera MapCamera => mapCamera;

    /// <summary>Дебаг: вся карта доступна для клика и выбора.</summary>
    public bool DebugRevealFullMinimap => debugRevealFullMinimap;

    /// <summary>Корень карты помечен как стартовый (кэш актуален после последнего <see cref="RefreshMinimapDiscoveryFromStartNodes"/>).</summary>
    public bool IsMinimapStartMapRoot(Node mapRoot) =>
        mapRoot != null && _minimapStartRootCache.Contains(mapRoot);

    /// <summary>
    /// При отсутствии выбранной ноды: исходящие линии скрыты, пока игрок не выберет стартовую ноду (раскрытие — <see cref="RevealOutgoingDisabledFrontierAfterMapTravel"/>).
    /// </summary>
    public bool ShouldShowMinimapEdgeWhenNothingSelected(MinimapEdge _) => false;

    /// <summary>Идёт секвенция перехода выбора по карте (клики гасятся в <see cref="HandleMapNodeClick"/>).</summary>
    public bool IsMapSelectionTravelInProgress => _mapSelectionTravelInProgress;

    /// <summary>
    /// Пересчитать видимость корней карты от стартовых нод и рёбер реестра. Не трогает ноду в <see cref="CurrentSelectedMapNode"/>, если она в <see cref="NodeMapState.Selected"/>.
    /// </summary>
    public void RefreshMinimapDiscoveryFromStartNodes()
    {
        if (!Application.isPlaying)
            return;

        var ownsSuppress = false;
        if (!_suppressMinimapPostNotify)
        {
            _suppressMinimapPostNotify = true;
            ownsSuppress = true;
        }

        try
        {
            if (debugRevealFullMinimap)
            {
                ApplyDebugFullMinimapLayout();
                ResetAllMinimapEdgesIdle();
                RefreshMinimapEdgeRegistryLines();
                return;
            }

            var mapRoots = CollectMapRootNodes();
            var registry = ResolveMinimapEdgeRegistry();
            _minimapStartRootCache.Clear();
            for (var i = 0; i < mapRoots.Count; i++)
            {
                var r = mapRoots[i];
                if (r != null && r.IsMinimapStartNode)
                    _minimapStartRootCache.Add(r);
            }

            var startCount = _minimapStartRootCache.Count;

            if (startCount == 0)
            {
                for (var i = 0; i < mapRoots.Count; i++)
                {
                    var r = mapRoots[i];
                    if (r == null)
                        continue;
                    if (ShouldSkipStateOverrideBecauseSelected(r))
                        continue;
                    r.ForceMapState(NodeMapState.Visible);
                }

                Debug.LogWarning(
                    "[GameManager] Нет ни одной ноды с IsMinimapStartNode — все корни карты оставлены Visible.",
                    this);
                ResetAllMinimapEdgesIdle();
                RefreshMinimapEdgeRegistryLines();
                return;
            }

            // Видимые (активные) корни: все старты + логические владельцы концов исходящих от стартов рёбер (один шаг по направлению ребра).
            _scratchNodesB.Clear();
            foreach (var s in _minimapStartRootCache)
                _scratchNodesB.Add(s);

            // Первый порядок от стартов не входит в стартовое облако — появляется после выбора старта (см. Notify → Reveal).

            // Исходящие от текущей выбранной: следующий слой (вторая волна и дальше) не сбрасывается в Inactive при пересчёте.
            if (CurrentSelectedMapNode != null && registry != null)
            {
                var fromSelected = registry.GetEdgesFrom(CurrentSelectedMapNode);
                for (var i = 0; i < fromSelected.Count; i++)
                {
                    var e = fromSelected[i];
                    if (e == null || e.ToNode == null)
                        continue;
                    var toOwner = e.ToNode.SelectionOwner;
                    if (toOwner != null)
                        _scratchNodesB.Add(toOwner);
                }
            }

            for (var i = 0; i < mapRoots.Count; i++)
            {
                var root = mapRoots[i];
                if (root == null)
                    continue;
                if (ShouldSkipStateOverrideBecauseSelected(root))
                    continue;

                if (_scratchNodesB.Contains(root))
                {
                    if (root.CurrentState == NodeMapState.Appearing ||
                        root.CurrentState == NodeMapState.Blocked)
                        continue;
                    // Inactive в облаке выбранной: раскрытие через Reveal (Appearing), не форсировать Visible до него.
                    if (root.CurrentState == NodeMapState.Inactive)
                        continue;
                    root.ForceMapState(NodeMapState.Visible);
                }
                else
                {
                    // Не в облаке текущего выбора (например соседняя ветка, заблокированная при переходе) — не гасить Blocked.
                    if (root.CurrentState == NodeMapState.Blocked)
                        continue;
                    root.ForceMapState(NodeMapState.Inactive);
                }
            }

            if (logMinimapDiscovery)
                Debug.Log($"[GameManager] Minimap discovery: стартов {startCount}, корней в облаке видимости {_scratchNodesB.Count} из {mapRoots.Count}.", this);

            ResetAllMinimapEdgesIdle();
            RefreshMinimapEdgeRegistryLines();
        }
        finally
        {
            if (ownsSuppress)
                _suppressMinimapPostNotify = false;
        }
    }

    /// <summary>Нода, для которой последний раз пришло уведомление о смене <see cref="NodeMapState"/>.</summary>
    public Node LastMapStateSourceNode { get; private set; }

    /// <summary>Актуальное состояние у <see cref="LastMapStateSourceNode"/> на момент последнего уведомления.</summary>
    public NodeMapState LastReportedMapState { get; private set; }

    /// <summary>Предыдущее состояние той же ноды в том же переходе; для первого применения в сцене — null.</summary>
    public NodeMapState? PreviousReportedMapState { get; private set; }

    /// <summary>Нода в состоянии <see cref="NodeMapState.Selected"/> (логический выбор на карте); null если ни одна не в Selected.</summary>
    public Node CurrentSelectedMapNode { get; private set; }

    /// <summary>Для группы: чей ролик сейчас идёт (родитель или дочерняя); иначе null.</summary>
    public Node CurrentGroupPlaybackFocusNode => _groupPlaylistFocusNode;

    /// <summary>Вызывается из <see cref="Node"/> после каждого успешного перехода стейт-машины карты.</summary>
    public event Action<Node, NodeMapState, NodeMapState?> MapNodeStateChanged;

    /// <summary>
    /// Уведомление о смене состояния ноды (вызывается из <see cref="Node"/>). Позже сюда можно добавить фильтры и правила «одна активная нода».
    /// </summary>
    public void NotifyNodeMapStateChanged(Node node, NodeMapState newState, NodeMapState? previousState)
    {
        if (node == null)
            return;

        LastMapStateSourceNode = node;
        LastReportedMapState = newState;
        PreviousReportedMapState = previousState;

        if (newState == NodeMapState.Selected)
        {
            CurrentSelectedMapNode = node;
            PlayMinimapVideoForSelectedNode(node);
            if (node.GroupParent == null && node.IsMinimapStartNode)
            {
                var registry = ResolveMinimapEdgeRegistry();
                RevealOutgoingDisabledFrontierAfterMapTravel(registry, node);
            }
        }
        else if (previousState == NodeMapState.Selected && CurrentSelectedMapNode == node)
        {
            if (!_suppressMapTravelSelectionClear)
            {
                CurrentSelectedMapNode = null;
                StopMinimapVideo();
            }
        }

        MapNodeStateChanged?.Invoke(node, newState, previousState);

        if (logMapNodeStateChanges)
        {
            Debug.Log(
                $"[GameManager] Map state: «{node.name}» {previousState?.ToString() ?? "∅"} → {newState}",
                node);
        }

        ApplyMinimapRulesAfterMapNotify();
    }

    /// <summary>
    /// Клик по ноде на карте: для группы — переключение ролика или повтор с начала; для одиночной ноды — повтор если уже выбрана.
    /// Возвращает true, если <see cref="Node.SetState"/> вызывать не нужно (уже выбрано и обработано).
    /// </summary>
    public bool HandleMapNodeClick(Node logicalOwner, Node clickedNode)
    {
        if (debugRevealFullMinimap)
            return false;

        if (_mapSelectionTravelInProgress)
            return true;

        if (TryBeginMapTravelToNeighbor(logicalOwner, clickedNode))
            return true;

        if (mapVideoPlayer == null || logicalOwner == null)
            return false;

        bool groupPlaylistActive =
            _groupPlaylistRoot != null &&
            logicalOwner.IsGroupParent &&
            logicalOwner.OrderedChildNodes.Count > 0 &&
            CurrentSelectedMapNode == logicalOwner;

        if (groupPlaylistActive && IsPartOfSelectedGroup(logicalOwner, clickedNode))
        {
            if (clickedNode == _groupPlaylistFocusNode)
            {
                RestartMinimapVideoFromStart();
                return true;
            }

            JumpGroupPlaylistTo(clickedNode);
            return true;
        }

        if (CurrentSelectedMapNode == logicalOwner)
        {
            RestartMinimapVideoFromStart();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Клик по другой активной ноде, соединённой исходящим ребром с текущей выбранной: прочие исходящие рёбра и их активные концы — Blocked; затем секвенция Deselected → (кольцо) → Blocked → ребро Moving → новая Selected.
    /// </summary>
    private bool TryBeginMapTravelToNeighbor(Node logicalOwner, Node clickedNode)
    {
        if (logicalOwner == null)
            return false;

        Node selected = CurrentSelectedMapNode;
        if (selected == null)
            return false;

        if (logicalOwner == selected)
            return false;

        if (logicalOwner.CurrentState != NodeMapState.Visible)
            return false;

        var registry = ResolveMinimapEdgeRegistry();
        if (registry == null)
            return false;

        var edge = registry.TryFindDirectedEdgeBetweenMapRoots(selected, logicalOwner);
        if (edge == null)
            return false;

        ClearMapTravelFromRootPostFullyBlockedListeners();

        if (_mapTravelCoroutine != null)
        {
            StopCoroutine(_mapTravelCoroutine);
            _mapTravelCoroutine = null;
        }

        _mapTravelTargetRoot = logicalOwner;
        _mapTravelCoroutine = StartCoroutine(CoMapSelectionTravel(registry, selected, logicalOwner, edge));
        return true;
    }

    /// <summary>
    /// Исходящие от <paramref name="fromRoot"/> рёбра, кроме <paramref name="chosenEdge"/>: <see cref="MinimapEdgeState.Blocked"/> с подавленным ramp на входе.
    /// Если корень конца — «доступный сосед», сначала <see cref="NodeMapState.Blocked"/> у ноды, затем <see cref="MinimapEdgeRegistry.SetAllEdgesEndingAtMapRootToBlocked"/> (без ramp на каждом ребре).
    /// Вторую фазу ramp для пакета и AB линии маршрута планирует <see cref="GameManager.ScheduleOutgoingBlockedEdgeVisualsAfterFromBrown"/> (см. <see cref="outgoingBlockedEdgeVisualOffsetSeconds"/>).
    /// </summary>
    /// <returns>Список рёбер для <see cref="MinimapEdgeRegistry.RunBlockedSlidersSecondPhaseForEdgesNow"/>; null если пакета нет.</returns>
    private static List<MinimapEdge> BlockAlternateOutgoingPathsForMapTravel(MinimapEdgeRegistry registry, Node fromRoot, MinimapEdge chosenEdge)
    {
        if (registry == null || fromRoot == null || chosenEdge == null)
            return null;

        var delayedSecondPhase = new HashSet<MinimapEdge>();
        var outgoing = registry.GetEdgesFrom(fromRoot);
        for (var i = 0; i < outgoing.Count; i++)
        {
            var e = outgoing[i];
            if (e == null || ReferenceEquals(e, chosenEdge))
                continue;

            if (e.ToNode == null)
            {
                e.SetEdgeState(MinimapEdgeState.Blocked, forceLog: false, suppressBlockedSlidersRampOnEnter: true);
                delayedSecondPhase.Add(e);
                continue;
            }

            Node neighborRoot = e.ToNode.SelectionOwner;
            if (neighborRoot != null && IsActiveAlternateNeighborForMapTravel(neighborRoot))
            {
                neighborRoot.ForceMapState(NodeMapState.Blocked);
                registry.SetAllEdgesEndingAtMapRootToBlocked(neighborRoot, suppressBlockedSlidersRampOnEnter: true);
                registry.CollectEdgesEndingAtMapRoot(neighborRoot, delayedSecondPhase);
            }
            else
            {
                e.SetEdgeState(MinimapEdgeState.Blocked, forceLog: false, suppressBlockedSlidersRampOnEnter: true);
                delayedSecondPhase.Add(e);
            }
        }

        return delayedSecondPhase.Count > 0 ? new List<MinimapEdge>(delayedSecondPhase) : null;
    }

    private static bool IsActiveAlternateNeighborForMapTravel(Node neighborRoot)
    {
        switch (neighborRoot.CurrentState)
        {
            case NodeMapState.Visible:
            case NodeMapState.Deselected:
            case NodeMapState.Appearing:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// В начале нового travel с <paramref name="fromRoot"/>: ребро, по которому приехали на эту ноду в прошлый раз, переводится в Blocked (и ramp слайдеров), если ещё не уходили с неё.
    /// </summary>
    private void TryBlockPendingArrivalEdgeWhenLeavingEndNode(MinimapEdgeRegistry registry, Node fromRoot)
    {
        if (_pendingArrivalEdgeToBlockWhenLeavingEndNode == null || fromRoot == null)
            return;

        var arrival = _pendingArrivalEdgeToBlockWhenLeavingEndNode;
        _pendingArrivalEdgeToBlockWhenLeavingEndNode = null;

        if (arrival == null || arrival.ToNode == null || arrival.ToNode.SelectionOwner != fromRoot)
            return;

        arrival.SetEdgeState(MinimapEdgeState.Blocked, forceLog: false);
        arrival.PlayBlockedSlidersRampAfterMapRouteTravel();
    }

    /// <summary>
    /// После прибытия по ребру: исходящие от новой выбранной ноды рёбра в <see cref="MinimapEdgeState.Disabled"/> или «полная линия» (<see cref="MinimapEdgeState.Idle"/> / <see cref="MinimapEdgeState.IdleRevealed"/> / <see cref="MinimapEdgeState.Selected"/>)
    /// с дальним <see cref="NodeMapState.Inactive"/> — ребро в <see cref="MinimapEdgeState.Appearing"/> (рост линии); дальний корень переходит в
    /// <see cref="NodeMapState.Appearing"/> только после завершения анимации ребра (Appearing → <see cref="MinimapEdgeState.IdleRevealed"/>).
    /// Если ребро не уходит в Appearing, дальняя нода сразу получает <see cref="NodeMapState.Appearing"/>.
    /// Старт каждого такого раскрытия смещается на своё независимое случайное время в [ <see cref="outgoingEdgeAppearStaggerMin"/>, <see cref="outgoingEdgeAppearStaggerMax"/> ] от одного момента.
    /// </summary>
    private void RevealOutgoingDisabledFrontierAfterMapTravel(MinimapEdgeRegistry registry, Node selectedRoot)
    {
        if (registry == null || selectedRoot == null)
            return;

        float minD = Mathf.Min(outgoingEdgeAppearStaggerMin, outgoingEdgeAppearStaggerMax);
        float maxD = Mathf.Max(outgoingEdgeAppearStaggerMin, outgoingEdgeAppearStaggerMax);

        var outgoing = registry.GetEdgesFrom(selectedRoot);
        for (var i = 0; i < outgoing.Count; i++)
        {
            MinimapEdge e = outgoing[i];
            if (e == null || e.ToNode == null)
                continue;

            Node farRoot = e.ToNode.SelectionOwner;
            if (farRoot == null || farRoot == selectedRoot)
                continue;

            if (farRoot.CurrentState != NodeMapState.Inactive)
                continue;

            // Отдельное случайное значение для каждого ребра (не один раз на весь набор).
            float delay = maxD > minD ? UnityEngine.Random.Range(minD, maxD) : minD;
            e.SetPendingOutgoingAppearStagger(true);
            StartCoroutine(CoRevealOneOutgoingAfterStaggerDelay(e, farRoot, delay));
        }
    }

    private IEnumerator CoRevealOneOutgoingAfterStaggerDelay(MinimapEdge edge, Node farRoot, float delaySeconds)
    {
        if (edge == null || farRoot == null)
        {
            if (edge != null)
                edge.SetPendingOutgoingAppearStagger(false);
            yield break;
        }

        if (farRoot.CurrentState != NodeMapState.Inactive)
        {
            edge.SetPendingOutgoingAppearStagger(false);
            yield break;
        }

        // Idle / уже показанное / «конец выбран» рисуют полную линию до старта Appearing: на время задержки прячем ребро.
        if (MinimapEdgeStateUtil.IsFullLineIdleLike(edge.CurrentEdgeState))
            edge.SetEdgeState(MinimapEdgeState.Disabled, forceLog: false);

        if (delaySeconds > 1e-5f)
            yield return new WaitForSeconds(delaySeconds);

        if (edge == null || farRoot == null)
        {
            if (edge != null)
                edge.SetPendingOutgoingAppearStagger(false);
            yield break;
        }

        if (farRoot.CurrentState != NodeMapState.Inactive)
        {
            edge.SetPendingOutgoingAppearStagger(false);
            yield break;
        }

        var es = edge.CurrentEdgeState;
        if (es == MinimapEdgeState.Disabled || MinimapEdgeStateUtil.IsFullLineIdleLike(es))
        {
            edge.SetPendingOutgoingAppearStagger(false);
            edge.SetEdgeState(
                MinimapEdgeState.Appearing,
                forceLog: false,
                MinimapEdgeState.Idle,
                () =>
                {
                    if (farRoot != null)
                        farRoot.ForceMapState(NodeMapState.Appearing);
                });
        }
        else
        {
            edge.SetPendingOutgoingAppearStagger(false);
            farRoot.ForceMapState(NodeMapState.Appearing);
        }
    }

    private void ClearMapTravelFromRootPostFullyBlockedListeners()
    {
        if (_outgoingBlockedEdgeVisualsCoroutine != null)
        {
            StopCoroutine(_outgoingBlockedEdgeVisualsCoroutine);
            _outgoingBlockedEdgeVisualsCoroutine = null;
        }

        var root = _mapTravelPostFullyBlockedListenerRoot;
        if (root != null)
        {
            if (_mapTravelPostFullyBlockedForOutgoingBlockVisuals != null)
            {
                root.MapPostFullyBlockedGradientRampCompleted -= _mapTravelPostFullyBlockedForOutgoingBlockVisuals;
                _mapTravelPostFullyBlockedForOutgoingBlockVisuals = null;
            }

            if (_mapTravelOnFromMainBrownRampStarted != null)
            {
                root.MapNodeBlockedMainBrownRampStarted -= _mapTravelOnFromMainBrownRampStarted;
                _mapTravelOnFromMainBrownRampStarted = null;
            }
        }

        _mapTravelPostFullyBlockedListenerRoot = null;
    }

    private static void RunOutgoingBlockedEdgeVisuals(MinimapEdgeRegistry registry, List<MinimapEdge> altList, MinimapEdge travelEdge)
    {
        if (registry != null && altList != null && altList.Count > 0)
            registry.RunBlockedSlidersSecondPhaseForEdgesNow(altList);
        if (travelEdge != null)
            travelEdge.ApplyTravelLineSliderAbAfterFromNodeFullyBlocked();
    }

    private IEnumerator CoWaitThenRunOutgoingBlockedEdgeVisuals(
        float waitSeconds,
        MinimapEdgeRegistry registry,
        List<MinimapEdge> altList,
        MinimapEdge travelEdge)
    {
        if (waitSeconds > 1e-5f)
            yield return new WaitForSeconds(waitSeconds);
        RunOutgoingBlockedEdgeVisuals(registry, altList, travelEdge);
        _outgoingBlockedEdgeVisualsCoroutine = null;
    }

    /// <summary>
    /// Планирует вторую фазу блокировки исходящих рёбер и AB линии маршрута; см. <see cref="outgoingBlockedEdgeVisualOffsetSeconds"/>.
    /// </summary>
    private void ScheduleOutgoingBlockedEdgeVisualsAfterFromBrown(
        MinimapEdgeRegistry registry,
        Node fromRoot,
        MinimapEdge travelEdge,
        List<MinimapEdge> delayedAlternateSecondPhase)
    {
        if (fromRoot == null || travelEdge == null)
            return;

        if (_outgoingBlockedEdgeVisualsCoroutine != null)
        {
            StopCoroutine(_outgoingBlockedEdgeVisualsCoroutine);
            _outgoingBlockedEdgeVisualsCoroutine = null;
        }

        if (outgoingBlockedEdgeVisualOffsetSeconds < 0f)
        {
            Action onMainBrownRampStart = null;
            onMainBrownRampStart = () =>
            {
                if (fromRoot != null && onMainBrownRampStart != null)
                    fromRoot.MapNodeBlockedMainBrownRampStarted -= onMainBrownRampStart;
                _mapTravelOnFromMainBrownRampStarted = null;

                if (_outgoingBlockedEdgeVisualsCoroutine != null)
                {
                    StopCoroutine(_outgoingBlockedEdgeVisualsCoroutine);
                    _outgoingBlockedEdgeVisualsCoroutine = null;
                }

                float w = Mathf.Max(0f, fromRoot.MapNodeBlockedSequenceDuration + outgoingBlockedEdgeVisualOffsetSeconds);
                _outgoingBlockedEdgeVisualsCoroutine = StartCoroutine(
                    CoWaitThenRunOutgoingBlockedEdgeVisuals(w, registry, delayedAlternateSecondPhase, travelEdge));
            };

            fromRoot.MapNodeBlockedMainBrownRampStarted += onMainBrownRampStart;
            _mapTravelPostFullyBlockedListenerRoot = fromRoot;
            _mapTravelOnFromMainBrownRampStarted = onMainBrownRampStart;
            return;
        }

        Action onFromBrownComplete = null;
        onFromBrownComplete = () =>
        {
            if (fromRoot != null && onFromBrownComplete != null)
                fromRoot.MapPostFullyBlockedGradientRampCompleted -= onFromBrownComplete;
            _mapTravelPostFullyBlockedForOutgoingBlockVisuals = null;

            if (_outgoingBlockedEdgeVisualsCoroutine != null)
            {
                StopCoroutine(_outgoingBlockedEdgeVisualsCoroutine);
                _outgoingBlockedEdgeVisualsCoroutine = null;
            }

            float d = Mathf.Max(0f, outgoingBlockedEdgeVisualOffsetSeconds);
            _outgoingBlockedEdgeVisualsCoroutine = StartCoroutine(
                CoWaitThenRunOutgoingBlockedEdgeVisuals(d, registry, delayedAlternateSecondPhase, travelEdge));
        };

        fromRoot.MapPostFullyBlockedGradientRampCompleted += onFromBrownComplete;
        _mapTravelPostFullyBlockedListenerRoot = fromRoot;
        _mapTravelPostFullyBlockedForOutgoingBlockVisuals = onFromBrownComplete;
    }

    private IEnumerator CoMapSelectionTravel(MinimapEdgeRegistry registry, Node fromRoot, Node toRoot, MinimapEdge edge)
    {
        _mapSelectionTravelInProgress = true;
        _suppressMapTravelSelectionClear = true;

        try
        {
            var delayedAlternateSecondPhase = BlockAlternateOutgoingPathsForMapTravel(registry, fromRoot, edge);
            fromRoot.SetState(NodeMapState.Deselected);
            float ring = fromRoot.SelectionRingDisappearDuration;
            if (ring > 0f)
                yield return new WaitForSeconds(ring);
            fromRoot.ForceMapState(NodeMapState.Blocked);
            // Подписку на brown From (MapPost / MapNodeBlockedMainBrownRampStarted) вешаем до TryBlock/MapTryBegin: иначе при старте со старта
            // MapTryBegin синхронно запускает ramp и MapNodeBlockedMainBrownRampStarted срабатывает до подписки — альтернативы и AB линии маршрута не запускаются.
            ScheduleOutgoingBlockedEdgeVisualsAfterFromBrown(registry, fromRoot, edge, delayedAlternateSecondPhase);
            // После входа в Blocked: сброс счётчика входящих ramp. Блокировку «ребра прибытия» делаем здесь же — иначе RampBegin до Blocked,
            // затем Enter Blocked обнуляет счётчик, и сценарий без pending (старт без prior travel) никогда не вызывает Complete → нет brown на ноде.
            TryBlockPendingArrivalEdgeWhenLeavingEndNode(registry, fromRoot);
            fromRoot.MapTryBeginBlockedNodeSlidersIfNoIncomingEdgeRampPending();

            edge.SetEdgeState(MinimapEdgeState.MovingAlongEdge, forceLog: false, MinimapEdgeState.IdleRevealed);
            while (edge.CurrentEdgeState == MinimapEdgeState.MovingAlongEdge)
                yield return null;
            _suppressMapTravelSelectionClear = false;
            toRoot.SetState(NodeMapState.Selected);
            RefreshMinimapEdgeRegistryLines();
            RevealOutgoingDisabledFrontierAfterMapTravel(registry, toRoot);
            _pendingArrivalEdgeToBlockWhenLeavingEndNode = edge;
        }
        finally
        {
            // Подписки на MapPostFullyBlockedGradientRampCompleted не снимаем здесь: brown ноды может закончиться позже короткого MovingAlongEdge (ребро без видео).

            _suppressMapTravelSelectionClear = false;
            _mapSelectionTravelInProgress = false;
            _mapTravelTargetRoot = null;
            _mapTravelCoroutine = null;
        }
    }

    private void PlayMinimapVideoForSelectedNode(Node node)
    {
        if (mapVideoPlayer == null || node == null)
            return;

        EndGroupPlaylist();

        if (node.IsGroupParent && node.OrderedChildNodes.Count > 0)
        {
            BeginGroupPlaylist(node);
            return;
        }

        VideoClip clip = node.MinimapVideoClip;
        if (clip == null)
            return;

        mapVideoPlayer.clip = clip;
        mapVideoPlayer.isLooping = true;
        mapVideoPlayer.Play();
    }

    private void BeginGroupPlaylist(Node root)
    {
        if (mapVideoPlayer == null)
            return;

        _groupPlaylistRoot = root;
        RegisterGroupLoopHandler();
        PlayGroupClip(root);
    }

    private void EndGroupPlaylist()
    {
        if (mapVideoPlayer != null && _groupLoopHandlerRegistered && _groupLoopPointHandler != null)
        {
            mapVideoPlayer.loopPointReached -= _groupLoopPointHandler;
            _groupLoopHandlerRegistered = false;
            _groupLoopPointHandler = null;
        }

        _groupPlaylistRoot = null;
        _groupPlaylistFocusNode = null;
    }

    private void RegisterGroupLoopHandler()
    {
        if (mapVideoPlayer == null || _groupLoopHandlerRegistered)
            return;

        _groupLoopPointHandler = OnGroupVideoLoopPointReached;
        mapVideoPlayer.loopPointReached += _groupLoopPointHandler;
        _groupLoopHandlerRegistered = true;
    }

    private void OnGroupVideoLoopPointReached(VideoPlayer source)
    {
        if (_groupPlaylistRoot == null || mapVideoPlayer == null)
            return;
        if (CurrentSelectedMapNode != _groupPlaylistRoot)
            return;

        Node next = GetNextInGroupPlaylist(_groupPlaylistRoot, _groupPlaylistFocusNode);
        PlayGroupClip(next);
    }

    private static Node GetNextInGroupPlaylist(Node root, Node current)
    {
        if (root == null || current == null)
            return root;

        if (current == root)
        {
            IReadOnlyList<Node> children = root.OrderedChildNodes;
            return children.Count > 0 ? children[0] : root;
        }

        IReadOnlyList<Node> list = root.OrderedChildNodes;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] != current)
                continue;
            if (i + 1 < list.Count)
                return list[i + 1];
            return root;
        }

        return root;
    }

    private void PlayGroupClip(Node target, int skipDepth = 0)
    {
        if (mapVideoPlayer == null || _groupPlaylistRoot == null || target == null)
            return;

        const int maxSkips = 24;
        if (skipDepth > maxSkips)
            return;

        _groupPlaylistFocusNode = target;
        VideoClip clip = target.MinimapVideoClip;
        if (clip == null)
        {
            Node next = GetNextInGroupPlaylist(_groupPlaylistRoot, target);
            if (next != target || skipDepth == 0)
                PlayGroupClip(next, skipDepth + 1);
            return;
        }

        mapVideoPlayer.clip = clip;
        mapVideoPlayer.isLooping = false;
        mapVideoPlayer.Play();
    }

    private void JumpGroupPlaylistTo(Node target)
    {
        if (_groupPlaylistRoot == null || mapVideoPlayer == null)
            return;
        if (!IsPartOfSelectedGroup(_groupPlaylistRoot, target))
            return;

        PlayGroupClip(target);
    }

    private static bool IsPartOfSelectedGroup(Node groupRoot, Node any)
    {
        if (groupRoot == null || any == null)
            return false;
        if (any == groupRoot)
            return true;

        IReadOnlyList<Node> list = groupRoot.OrderedChildNodes;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == any)
                return true;
        }

        return false;
    }

    private void RestartMinimapVideoFromStart()
    {
        if (mapVideoPlayer == null || mapVideoPlayer.clip == null)
            return;

        mapVideoPlayer.time = 0;
        mapVideoPlayer.Play();
    }

    private void StopMinimapVideo()
    {
        if (mapVideoPlayer == null)
            return;
        mapVideoPlayer.Stop();
        EndGroupPlaylist();
    }

    /// <summary>Одноразовый ролик перехода по ребру на общем <see cref="mapVideoPlayer"/> (без лупа).</summary>
    public bool TryPlayMinimapEdgeTravelVideo(VideoClip clip)
    {
        if (mapVideoPlayer == null || clip == null)
            return false;

        EndGroupPlaylist();
        mapVideoPlayer.Stop();
        mapVideoPlayer.clip = clip;
        mapVideoPlayer.isLooping = false;
        mapVideoPlayer.Play();
        return true;
    }

    /// <summary>Остановка ролика ребра перед роликом выбранной ноды.</summary>
    public void StopMinimapEdgeTravelVideo()
    {
        if (mapVideoPlayer == null)
            return;
        mapVideoPlayer.Stop();
    }

    /// <summary>
    /// Показывать ли на этой ноде оставшееся время до конца текущего клипа (синхронно с <see cref="mapVideoPlayer"/>).
    /// Для дочерней ноды группы — когда играет её ролик; для корня — когда выбрана на карте и играет её ролик.
    /// </summary>
    public bool ShouldShowRemainingTimeOnNode(Node node)
    {
        if (node == null || mapVideoPlayer == null)
            return false;

        VideoClip nodeClip = node.MinimapVideoClip;
        if (nodeClip == null || mapVideoPlayer.clip != nodeClip)
            return false;
        if (CurrentSelectedMapNode != node.SelectionOwner)
            return false;

        // Одиночная нода или родитель группы: только в Selected (в т.ч. пусто в Deselected / Visible).
        if (node.GroupParent == null && node.CurrentState != NodeMapState.Selected)
            return false;

        return true;
    }

    /// <summary>Оставшееся время в секундах (для UI); false если таймер для этой ноды не показываем.</summary>
    public bool TryGetRemainingTimeForNodeDisplay(Node node, out float remainingSeconds)
    {
        remainingSeconds = 0f;
        if (!ShouldShowRemainingTimeOnNode(node))
            return false;

        remainingSeconds = Mathf.Max(0f, (float)(mapVideoPlayer.length - mapVideoPlayer.time));
        return true;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _lastDebugRevealFullMinimap = debugRevealFullMinimap;
    }

    private IEnumerator Start()
    {
        yield return null;
        yield return null;
        if (this != null)
            RefreshMinimapDiscoveryFromStartNodes();
    }

    private void OnDestroy()
    {
        ClearMapTravelFromRootPostFullyBlockedListeners();

        if (_mapTravelCoroutine != null)
        {
            StopCoroutine(_mapTravelCoroutine);
            _mapTravelCoroutine = null;
        }

        EndGroupPlaylist();
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (debugRevealFullMinimap != _lastDebugRevealFullMinimap)
        {
            _lastDebugRevealFullMinimap = debugRevealFullMinimap;
            RefreshMinimapDiscoveryFromStartNodes();
        }

        if (!enableNodeStateHotkeys)
            return;

        TryDebugNodeStateHotkeys();
    }

    private void ApplyMinimapRulesAfterMapNotify()
    {
        if (!Application.isPlaying)
            return;

        if (_mapSelectionTravelInProgress)
            return;

        if (_suppressMinimapPostNotify)
            return;

        _suppressMinimapPostNotify = true;
        try
        {
            if (debugRevealFullMinimap)
            {
                ApplyDebugFullMinimapLayout();
                ResetAllMinimapEdgesIdle();
                RefreshMinimapEdgeRegistryLines();
                return;
            }

            if (CurrentSelectedMapNode != null &&
                CurrentSelectedMapNode.IsMinimapStartNode &&
                CountMinimapStartRoots() > 1)
            {
                ApplyNonChosenMinimapStartsBlocked(CurrentSelectedMapNode);
                RefreshMinimapEdgeRegistryLines();
                return;
            }

            RefreshMinimapDiscoveryFromStartNodes();
        }
        finally
        {
            _suppressMinimapPostNotify = false;
        }
    }

    private static bool ShouldSkipStateOverrideBecauseSelected(Node root) =>
        root.CurrentState == NodeMapState.Selected;

    private void ApplyDebugFullMinimapLayout()
    {
        var mapRoots = CollectMapRootNodes();
        for (var i = 0; i < mapRoots.Count; i++)
        {
            var r = mapRoots[i];
            if (r == null)
                continue;
            if (ShouldSkipStateOverrideBecauseSelected(r))
                continue;
            r.ForceMapState(NodeMapState.Visible);
        }

        if (logMinimapDiscovery)
            Debug.Log($"[GameManager] debugRevealFullMinimap: корней карты {mapRoots.Count} → Visible (кроме выбранной).", this);
    }

    private int CountMinimapStartRoots()
    {
        var mapRoots = CollectMapRootNodes();
        var n = 0;
        for (var i = 0; i < mapRoots.Count; i++)
        {
            if (mapRoots[i] != null && mapRoots[i].IsMinimapStartNode)
                n++;
        }

        return n;
    }

    private void ApplyNonChosenMinimapStartsBlocked(Node selectedStartRoot)
    {
        if (selectedStartRoot == null || !selectedStartRoot.IsMinimapStartNode)
            return;

        ResetAllMinimapEdgesIdle();

        var mapRoots = CollectMapRootNodes();
        var otherStarts = new HashSet<Node>();
        for (var i = 0; i < mapRoots.Count; i++)
        {
            var r = mapRoots[i];
            if (r != null && r.IsMinimapStartNode && r != selectedStartRoot)
                otherStarts.Add(r);
        }

        if (otherStarts.Count == 0)
            return;

        foreach (var os in otherStarts)
            os.ForceMapState(NodeMapState.Blocked);

        // Рёбра, касающиеся не выбранных стартов: Disabled (линия скрыта), не Blocked — иначе ApplyCombinedVisual всегда показывает Blocked-ребро.
        var registry = ResolveMinimapEdgeRegistry();
        if (registry != null && registry.Edges != null)
        {
            for (var i = 0; i < registry.Edges.Count; i++)
            {
                var e = registry.Edges[i];
                if (e == null)
                    continue;
                if (MinimapEdgeTouchesAnyRoot(e, otherStarts))
                    e.SetEdgeState(MinimapEdgeState.Disabled, forceLog: false);
            }
        }

        var visited = new HashSet<Node>();
        var queue = new Queue<Node>();
        foreach (var os in otherStarts)
        {
            if (visited.Add(os))
                queue.Enqueue(os);
        }

        var neigh = new HashSet<Node>();
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            neigh.Clear();
            if (registry != null)
                registry.AddUndirectedNeighbors(cur, neigh);
            foreach (var nb in neigh)
            {
                if (nb == null)
                    continue;
                if (nb.SelectionOwner == selectedStartRoot)
                    continue;
                if (!visited.Add(nb))
                    continue;
                queue.Enqueue(nb);
            }
        }

        foreach (var v in visited)
        {
            var root = v.SelectionOwner;
            if (root == null || root == selectedStartRoot)
                continue;
            if (root.CurrentState == NodeMapState.Inactive)
                continue;
            root.ForceMapState(NodeMapState.Blocked);
        }

        if (logMinimapDiscovery)
            Debug.Log(
                $"[GameManager] Выбран старт «{selectedStartRoot.name}»: остальные старты и их компонента заблокированы (узлов в обходе {visited.Count}).",
                selectedStartRoot);
    }

    private static bool MinimapEdgeTouchesAnyRoot(MinimapEdge edge, HashSet<Node> roots)
    {
        if (edge == null || roots == null || roots.Count == 0)
            return false;
        Node a = edge.FromNode != null ? edge.FromNode.SelectionOwner : null;
        Node b = edge.ToNode != null ? edge.ToNode.SelectionOwner : null;
        if (a != null && roots.Contains(a))
            return true;
        if (b != null && roots.Contains(b))
            return true;
        return false;
    }

    private static List<Node> CollectMapRootNodes()
    {
        var all = FindObjectsOfType<Node>(true);
        var list = new List<Node>();
        for (var i = 0; i < all.Length; i++)
        {
            var n = all[i];
            if (n != null && n.GroupParent == null)
                list.Add(n);
        }

        return list;
    }

    private MinimapEdgeRegistry ResolveMinimapEdgeRegistry()
    {
        if (minimapEdgeRegistry != null)
            return minimapEdgeRegistry;
        return FindObjectOfType<MinimapEdgeRegistry>();
    }

    private void ResetAllMinimapEdgesIdle()
    {
        var reg = ResolveMinimapEdgeRegistry();
        reg?.SetAllEdgesVisualStateIdle();
    }

    private void RefreshMinimapEdgeRegistryLines()
    {
        ResolveMinimapEdgeRegistry()?.RefreshOutgoingLineVisibilityForMapSelection();
    }

    /// <summary>Нода для дебаг-горячих клавиш: последняя из <see cref="NotifyNodeMapStateChanged"/>, иначе <see cref="debugTargetNode"/>.</summary>
    public Node GetDebugHotkeyTargetNode()
    {
        if (LastMapStateSourceNode != null)
            return LastMapStateSourceNode;
        return debugTargetNode;
    }

    /// <summary>
    /// Дебаг: 1–6 — <see cref="NodeMapState"/> по порядку enum; 7–9 — зарезервировано (лог в консоль).
    /// Работает и с цифрами над буквами, и с NumPad. Цель — <see cref="GetDebugHotkeyTargetNode"/>.
    /// </summary>
    private void TryDebugNodeStateHotkeys()
    {
        if (WasReservedDebugKeyPressed(out string reservedLabel))
        {
            Debug.Log(
                $"[GameManager][Debug] Клавиша {reservedLabel} зарезервирована (пока нет состояния). Доступны 1–6 под текущие {nameof(NodeMapState)}.",
                this);
            return;
        }

        Node target = GetDebugHotkeyTargetNode();
        if (target == null)
            return;

        if (TryGetPressedDebugStateKey(out NodeMapState state, out string keyLabel))
        {
            string source = LastMapStateSourceNode != null ? nameof(LastMapStateSourceNode) : nameof(debugTargetNode);
            Debug.Log(
                $"[GameManager][Debug] Клавиша {keyLabel} → {nameof(Node.SetState)}({state}) на «{target.name}» (источник: {source}).",
                target);
            target.SetState(state);
        }
    }

    private static bool TryGetPressedDebugStateKey(out NodeMapState state, out string keyLabel)
    {
        if (WasDigitKeyPressed(1)) { state = NodeMapState.Inactive; keyLabel = "1"; return true; }
        if (WasDigitKeyPressed(2)) { state = NodeMapState.Appearing; keyLabel = "2"; return true; }
        if (WasDigitKeyPressed(3)) { state = NodeMapState.Visible; keyLabel = "3"; return true; }
        if (WasDigitKeyPressed(4)) { state = NodeMapState.Selected; keyLabel = "4"; return true; }
        if (WasDigitKeyPressed(5)) { state = NodeMapState.Deselected; keyLabel = "5"; return true; }
        if (WasDigitKeyPressed(6)) { state = NodeMapState.Blocked; keyLabel = "6"; return true; }

        state = default;
        keyLabel = null;
        return false;
    }

    private static bool WasReservedDebugKeyPressed(out string keyLabel)
    {
        if (WasDigitKeyPressed(7)) { keyLabel = "7"; return true; }
        if (WasDigitKeyPressed(8)) { keyLabel = "8"; return true; }
        if (WasDigitKeyPressed(9)) { keyLabel = "9"; return true; }

        keyLabel = null;
        return false;
    }

    private static bool WasDigitKeyPressed(int digit)
    {
        var alpha = digit switch
        {
            1 => KeyCode.Alpha1,
            2 => KeyCode.Alpha2,
            3 => KeyCode.Alpha3,
            4 => KeyCode.Alpha4,
            5 => KeyCode.Alpha5,
            6 => KeyCode.Alpha6,
            7 => KeyCode.Alpha7,
            8 => KeyCode.Alpha8,
            9 => KeyCode.Alpha9,
            _ => KeyCode.None,
        };

        var keypad = digit switch
        {
            1 => KeyCode.Keypad1,
            2 => KeyCode.Keypad2,
            3 => KeyCode.Keypad3,
            4 => KeyCode.Keypad4,
            5 => KeyCode.Keypad5,
            6 => KeyCode.Keypad6,
            7 => KeyCode.Keypad7,
            8 => KeyCode.Keypad8,
            9 => KeyCode.Keypad9,
            _ => KeyCode.None,
        };

        return Input.GetKeyDown(alpha) || Input.GetKeyDown(keypad);
    }
}

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
    /// При отсутствии выбранной ноды: показывать ребро, если оно исходит от любой стартовой ноды (<see cref="Node.IsMinimapStartNode"/> у корня <see cref="Node.SelectionOwner"/> от <see cref="MinimapEdge.FromNode"/>).
    /// </summary>
    public bool ShouldShowMinimapEdgeWhenNothingSelected(MinimapEdge edge) =>
        edge != null &&
        edge.FromNode != null &&
        _minimapStartRootCache.Contains(edge.FromNode.SelectionOwner);

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

            if (registry != null && registry.Edges != null)
            {
                for (var i = 0; i < registry.Edges.Count; i++)
                {
                    var e = registry.Edges[i];
                    if (e == null || e.FromNode == null)
                        continue;
                    var fromOwner = e.FromNode.SelectionOwner;
                    if (fromOwner == null || !_minimapStartRootCache.Contains(fromOwner))
                        continue;
                    if (e.ToNode == null)
                        continue;
                    var toOwner = e.ToNode.SelectionOwner;
                    if (toOwner != null)
                        _scratchNodesB.Add(toOwner);
                }
            }

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
                    root.ForceMapState(NodeMapState.Visible);
                }
                else
                    root.ForceMapState(NodeMapState.Inactive);
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
    /// Исходящие от <paramref name="fromRoot"/> рёбра, кроме <paramref name="chosenEdge"/>: ребро <see cref="MinimapEdgeState.Blocked"/>;
    /// корень конца (<see cref="Node.SelectionOwner"/>) — <see cref="NodeMapState.Blocked"/>, если нода сейчас «как доступный сосед» (Visible / Deselected / Appearing).
    /// </summary>
    private static void BlockAlternateOutgoingPathsForMapTravel(MinimapEdgeRegistry registry, Node fromRoot, MinimapEdge chosenEdge)
    {
        if (registry == null || fromRoot == null || chosenEdge == null)
            return;

        var outgoing = registry.GetEdgesFrom(fromRoot);
        for (var i = 0; i < outgoing.Count; i++)
        {
            var e = outgoing[i];
            if (e == null || ReferenceEquals(e, chosenEdge))
                continue;

            e.SetEdgeState(MinimapEdgeState.Blocked, forceLog: false);

            if (e.ToNode == null)
                continue;

            Node neighborRoot = e.ToNode.SelectionOwner;
            if (neighborRoot == null || !IsActiveAlternateNeighborForMapTravel(neighborRoot))
                continue;

            neighborRoot.ForceMapState(NodeMapState.Blocked);
        }
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
    /// После прибытия по ребру: исходящие от новой выбранной ноды рёбра в <see cref="MinimapEdgeState.Disabled"/> или <see cref="MinimapEdgeState.Idle"/>
    /// (после <see cref="MinimapEdgeRegistry.SetAllEdgesVisualStateIdle"/>) с дальним <see cref="NodeMapState.Inactive"/> — ребро Appearing → Idle;
    /// дальний корень — Appearing → Visible (таймер на ноде).
    /// </summary>
    private static void RevealOutgoingDisabledFrontierAfterMapTravel(MinimapEdgeRegistry registry, Node selectedRoot)
    {
        if (registry == null || selectedRoot == null)
            return;

        var outgoing = registry.GetEdgesFrom(selectedRoot);
        for (var i = 0; i < outgoing.Count; i++)
        {
            MinimapEdge e = outgoing[i];
            if (e == null)
                continue;

            if (e.ToNode == null)
                continue;

            Node farRoot = e.ToNode.SelectionOwner;
            if (farRoot == null || farRoot == selectedRoot)
                continue;

            if (farRoot.CurrentState != NodeMapState.Inactive)
                continue;

            var es = e.CurrentEdgeState;
            if (es == MinimapEdgeState.Disabled || es == MinimapEdgeState.Idle)
                e.SetEdgeState(MinimapEdgeState.Appearing, forceLog: false);

            farRoot.ForceMapState(NodeMapState.Appearing);
        }
    }

    private IEnumerator CoMapSelectionTravel(MinimapEdgeRegistry registry, Node fromRoot, Node toRoot, MinimapEdge edge)
    {
        _mapSelectionTravelInProgress = true;
        _suppressMapTravelSelectionClear = true;
        try
        {
            BlockAlternateOutgoingPathsForMapTravel(registry, fromRoot, edge);
            fromRoot.SetState(NodeMapState.Deselected);
            float ring = fromRoot.SelectionRingDisappearDuration;
            if (ring > 0f)
                yield return new WaitForSeconds(ring);
            fromRoot.ForceMapState(NodeMapState.Blocked);
            edge.SetEdgeState(MinimapEdgeState.MovingAlongEdge, forceLog: false, MinimapEdgeState.Blocked);
            while (edge.CurrentEdgeState == MinimapEdgeState.MovingAlongEdge)
                yield return null;
            _suppressMapTravelSelectionClear = false;
            toRoot.SetState(NodeMapState.Selected);
            RefreshMinimapEdgeRegistryLines();
            RevealOutgoingDisabledFrontierAfterMapTravel(registry, toRoot);
        }
        finally
        {
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

        var registry = ResolveMinimapEdgeRegistry();
        if (registry != null && registry.Edges != null)
        {
            for (var i = 0; i < registry.Edges.Count; i++)
            {
                var e = registry.Edges[i];
                if (e == null)
                    continue;
                if (MinimapEdgeTouchesAnyRoot(e, otherStarts))
                    e.SetEdgeState(MinimapEdgeState.Blocked, forceLog: false);
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

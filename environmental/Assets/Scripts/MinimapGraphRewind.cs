using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Стек раскрытий карты (ребро → нода при frontier-reveal). По R — отмотка в обратном порядке, затем сброс к стартовой разметке через <see cref="GameManager"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class MinimapGraphRewind : MonoBehaviour
{
    private enum RewindEntryKind
    {
        Edge,
        Node,
    }

    private struct RewindEntry
    {
        public RewindEntryKind Kind;
        public MinimapEdge Edge;
        public Node MapRoot;
    }

    [Tooltip("Клавиша тестовой отмотки графа.")]
    [SerializeField]
    private KeyCode rewindKey = KeyCode.R;

    [Tooltip("Пауза между шагами отмотки (сек, unscaled).")]
    [SerializeField]
    [Min(0f)]
    private float stepDelaySeconds = 0.07f;

    [Tooltip("Выкл. — отмотка только через код (RequestRewind).")]
    [SerializeField]
    private bool enableRewindKey = true;

    private readonly List<RewindEntry> _discoveryStack = new List<RewindEntry>();

    private Coroutine _rewindRoutine;
    private bool _suppressRecording;

    /// <summary>
    /// Флаг ставится ДО запуска корутины: её синхронная часть (снятие выбора в <see cref="GameManager.AbortMapActivityForGraphRewind"/>)
    /// выполняется раньше, чем StartCoroutine вернёт значение, и без флага GameManager считал бы, что отмотки нет, — и разом гасил граф.
    /// </summary>
    private bool _rewindActive;

    /// <summary>Идёт отмотка хоть в одном компоненте (для мест, где ссылки на компонент нет).</summary>
    public static bool IsAnyRewinding { get; private set; }

    public bool IsRewinding => _rewindActive;

    public bool ShouldRecord =>
        Application.isPlaying && !_suppressRecording && !_rewindActive;

    private void OnDisable()
    {
        if (_rewindActive)
        {
            _rewindActive = false;
            IsAnyRewinding = false;
        }
    }

    private void Update()
    {
        if (!enableRewindKey || !Application.isPlaying || _rewindActive)
            return;
        if (!Input.GetKeyDown(rewindKey))
            return;
        RequestRewind();
    }

    /// <summary>Ребро ушло в Appearing при раскрытии frontier (до ноды).</summary>
    public void RecordEdgeAppeared(MinimapEdge edge)
    {
        if (!ShouldRecord || edge == null)
            return;
        _discoveryStack.Add(new RewindEntry { Kind = RewindEntryKind.Edge, Edge = edge });
    }

    /// <summary>Корень карты ушёл в Appearing при раскрытии (после ребра или напрямую).</summary>
    public void RecordNodeAppeared(Node mapRoot)
    {
        if (!ShouldRecord || mapRoot == null || mapRoot.GroupParent != null)
            return;
        _discoveryStack.Add(new RewindEntry { Kind = RewindEntryKind.Node, MapRoot = mapRoot });
    }

    public void RequestRewind()
    {
        if (!Application.isPlaying || _rewindActive)
            return;
        var gm = GameManager.Instance;
        if (gm == null)
            return;
        _rewindActive = true;
        IsAnyRewinding = true;
        _rewindRoutine = StartCoroutine(CoRewindGraph(gm));
    }

    private void AppendDiscoveryPair(MinimapEdge edge, Node mapRoot)
    {
        if (edge != null)
            _discoveryStack.Add(new RewindEntry { Kind = RewindEntryKind.Edge, Edge = edge });
        if (mapRoot != null && mapRoot.GroupParent == null)
            _discoveryStack.Add(new RewindEntry { Kind = RewindEntryKind.Node, MapRoot = mapRoot });
    }

    private IEnumerator CoRewindGraph(GameManager gm)
    {
        RebuildDiscoveryStackFromMapLayout(gm);
        _suppressRecording = true;
        gm.AbortMapActivityForGraphRewind();
        gm.StopAllPendingFrontierRevealCoroutines();

        for (var i = _discoveryStack.Count - 1; i >= 0; i--)
        {
            var entry = _discoveryStack[i];
            var changed = false;
            switch (entry.Kind)
            {
                case RewindEntryKind.Node:
                    if (entry.MapRoot != null && entry.MapRoot.CurrentState != NodeMapState.Inactive)
                    {
                        entry.MapRoot.ForceMapState(NodeMapState.Inactive);
                        changed = true;
                    }
                    break;
                case RewindEntryKind.Edge:
                    if (entry.Edge != null && entry.Edge.CurrentEdgeState != MinimapEdgeState.Disabled)
                    {
                        changed = entry.Edge.IsLineVisible;
                        entry.Edge.SetEdgeState(MinimapEdgeState.Disabled, forceLog: false);
                    }
                    break;
            }

            // Уже скрытое — без паузы, чтобы не было «пустой» отмотки.
            if (!changed)
                continue;

            if (stepDelaySeconds > 0f)
                yield return new WaitForSecondsRealtime(stepDelaySeconds);
            else
                yield return null;
        }

        _discoveryStack.Clear();
        gm.ApplyGraphRewindBaseline();
        _suppressRecording = false;
        _rewindRoutine = null;
        _rewindActive = false;
        IsAnyRewinding = false;
    }

    /// <summary>
    /// Перед отмоткой: актуальный порядок раскрытого фронтира (BFS от стартов), не только записи во время stagger.
    /// </summary>
    private void RebuildDiscoveryStackFromMapLayout(GameManager gm)
    {
        _discoveryStack.Clear();
        if (gm == null)
            return;

        var registry = gm.GetMinimapEdgeRegistry();
        if (registry == null)
            return;

        var allNodes = Object.FindObjectsOfType<Node>(true);
        var startNodes = new List<Node>();
        for (var i = 0; i < allNodes.Length; i++)
        {
            var n = allNodes[i];
            if (n != null && n.GroupParent == null && n.IsMinimapStartNode)
                startNodes.Add(n);
        }

        if (startNodes.Count == 0)
            return;

        var startSet = new HashSet<Node>(startNodes);
        var reachableFromStarts = CollectReachableMapRootsFromStarts(startSet, registry);
        var orderedRoots = OrderRootsByBfsDepth(startSet, reachableFromStarts, registry);

        // Все показанные рёбра, ведущие в корень (в т.ч. второй и дальше входящий путь в узел-слияние), — перед самим корнем:
        // при обратном проходе сначала гаснет нода, затем её входящие рёбра.
        var edgesByRoot = new Dictionary<Node, List<MinimapEdge>>();
        var leftovers = new List<MinimapEdge>();
        var edges = registry.Edges;
        if (edges != null)
        {
            for (var i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                // Только реально видимые линии: на старте все рёбра в Idle, но скрыты — иначе отмотка «проходит» невидимую часть графа с паузами.
                if (edge == null || !edge.IsLineVisible)
                    continue;

                if (TryGetDiscoveryPair(edge, startSet, reachableFromStarts, out var toRoot))
                {
                    if (!edgesByRoot.TryGetValue(toRoot, out var list))
                        edgesByRoot[toRoot] = list = new List<MinimapEdge>();
                    list.Add(edge);
                }
                else
                {
                    leftovers.Add(edge);
                }
            }
        }

        for (var i = 0; i < orderedRoots.Count; i++)
        {
            var root = orderedRoots[i];
            if (edgesByRoot.TryGetValue(root, out var list))
            {
                for (var k = 0; k < list.Count; k++)
                    _discoveryStack.Add(new RewindEntry { Kind = RewindEntryKind.Edge, Edge = list[k] });
            }

            if (root.GroupParent == null && IsDiscoveredMapRootForRewind(root))
                _discoveryStack.Add(new RewindEntry { Kind = RewindEntryKind.Node, MapRoot = root });
        }

        // Показанные рёбра вне раскрытия от стартов (например, в стартовую ноду) — гаснут первыми.
        for (var i = 0; i < leftovers.Count; i++)
            _discoveryStack.Add(new RewindEntry { Kind = RewindEntryKind.Edge, Edge = leftovers[i] });
    }

    private static HashSet<Node> CollectReachableMapRootsFromStarts(
        HashSet<Node> startSet,
        MinimapEdgeRegistry registry)
    {
        var reachable = new HashSet<Node>();
        var edges = registry.Edges;
        if (edges == null)
            return reachable;

        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (!TryGetDiscoveryPair(edge, startSet, reachable, out var toRoot))
                    continue;

                if (reachable.Add(toRoot))
                    changed = true;
            }
        }

        return reachable;
    }

    private static List<Node> OrderRootsByBfsDepth(
        HashSet<Node> startSet,
        HashSet<Node> reachableFromStarts,
        MinimapEdgeRegistry registry)
    {
        var depthByRoot = new Dictionary<Node, int>();
        foreach (var s in startSet)
            depthByRoot[s] = 0;

        var edges = registry.Edges;
        var changed = true;
        while (changed)
        {
            changed = false;
            if (edges == null)
                break;

            for (var i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (!TryGetDiscoveryPair(edge, startSet, reachableFromStarts, out var toRoot))
                    continue;

                var fromRoot = ResolveMapRoot(edge.FromNode);
                if (fromRoot == null || !depthByRoot.TryGetValue(fromRoot, out var fromDepth))
                    continue;

                var nextDepth = fromDepth + 1;
                if (depthByRoot.TryGetValue(toRoot, out var knownDepth) && knownDepth <= nextDepth)
                    continue;

                depthByRoot[toRoot] = nextDepth;
                changed = true;
            }
        }

        var orderedRoots = new List<Node>(reachableFromStarts);
        orderedRoots.Sort((a, b) =>
        {
            depthByRoot.TryGetValue(a, out var da);
            depthByRoot.TryGetValue(b, out var db);
            var cmp = da.CompareTo(db);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.name, b.name);
        });

        return orderedRoots;
    }

    private static bool TryGetDiscoveryPair(
        MinimapEdge edge,
        HashSet<Node> startSet,
        HashSet<Node> reachableOrActiveFrom,
        out Node toRoot)
    {
        toRoot = null;
        if (edge?.FromNode == null || edge.ToNode == null)
            return false;

        toRoot = edge.ToNode.SelectionOwner;
        if (toRoot == null || toRoot.IsMinimapStartNode)
            return false;

        var fromRoot = ResolveMapRoot(edge.FromNode);
        if (fromRoot == null)
            return false;

        var fromOk = startSet.Contains(fromRoot) || reachableOrActiveFrom.Contains(fromRoot);
        if (!fromOk)
            return false;

        var discoveredRoot = IsDiscoveredMapRootForRewind(toRoot);
        if (discoveredRoot || IsRewindableFrontierEdge(edge))
            return true;

        toRoot = null;
        return false;
    }

    private static Node ResolveMapRoot(Node node) =>
        node == null ? null : node.SelectionOwner != null ? node.SelectionOwner : node;

    private static bool IsDiscoveredMapRootForRewind(Node root) =>
        root != null && root.CurrentState != NodeMapState.Inactive;

    private static bool IsRewindableFrontierEdge(MinimapEdge edge)
    {
        if (edge == null)
            return false;

        var s = edge.CurrentEdgeState;
        return s != MinimapEdgeState.Disabled && s != MinimapEdgeState.MovingAlongEdge;
    }
}

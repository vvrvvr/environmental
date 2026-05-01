using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Реестр рёбер мини-карты. Кэш исходящих рёбер по <see cref="MinimapEdge.FromNode"/>.
/// Для запросов «от какой ноды» по умолчанию используется <see cref="Node.SelectionOwner"/> (дочерняя нода → родитель группы).
/// </summary>
[DisallowMultipleComponent]
public class MinimapEdgeRegistry : MonoBehaviour
{
    [Tooltip("Все рёбра мини-карты. В инспекторе есть кнопка «Собрать все рёбра со сцены».")]
    [SerializeField] private List<MinimapEdge> edges = new List<MinimapEdge>();

    [Header("Selection → line visibility")]
    [Tooltip(
        "В Play Mode: при выбранной ноде — LineRenderer только у исходящих от неё рёбер. Без выбора — исходящие от всех стартовых нод (см. GameManager). В редакторе без Play линии всегда вкл.")]
    [SerializeField]
    private bool showOutgoingLinesOnlyForSelectedMapNode = true;

    [Header("Debug")]
    [Tooltip("Рисовать линии FromNode → ToNode в Scene-вью (для проверки связей).")]
    [SerializeField] private bool debugDrawEdgeGraph;

    [Tooltip("В Play: клавиши 1–7 и NumPad — задать состояние всем рёбрам из списка (MinimapEdgeState).")]
    [SerializeField] private bool debugDigitKeysSetAllEdgeStates;

    [Header("Visual")]
    [Tooltip("Общая палитра: ссылка на рёбрах для кнопки A/B/C в ассете палитры + ноды на сцене. Раздаётся при RebuildEdgeCache (рёбра) и кнопками в инспекторе.")]
    [FormerlySerializedAs("sharedLineColorPalette")]
    [SerializeField] private MinimapGraphVisualPalette sharedMapVisualPalette;

    private readonly Dictionary<Node, List<MinimapEdge>> _outgoingByFromNode = new Dictionary<Node, List<MinimapEdge>>();

    private bool _subscribedToGameManager;
    private Coroutine _blockedAlternateBatchSecondPhaseCoroutine;

    public IReadOnlyList<MinimapEdge> Edges => edges;

    /// <summary>Общая палитра (рёбра + ноды).</summary>
    public MinimapGraphVisualPalette SharedMapVisualPalette => sharedMapVisualPalette;

    /// <summary>
    /// Проставить палитру (или null) всем ненулевым рёбрам из <see cref="edges"/> и обновить линии.
    /// </summary>
    public void ApplySharedLinePaletteToAllEdges()
    {
        if (edges == null)
            return;
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (edge != null)
                edge.SetLineColorPalette(sharedMapVisualPalette);
        }
    }

    /// <summary>
    /// Проставить ту же палитру всем <see cref="Node"/> на загруженных сценах и применить A/B/C к их <see cref="SpriteRendererGradientPropertyDriver"/> (если есть).
    /// </summary>
    public void ApplySharedMapVisualPaletteToAllNodes()
    {
        var nodes = Object.FindObjectsOfType<Node>(true);
        for (var i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            if (n != null)
                n.SetMapVisualPalette(sharedMapVisualPalette);
        }
    }

    private void Awake() => RebuildEdgeCache();

    private void OnEnable()
    {
        RebuildEdgeCache();
        if (Application.isPlaying)
        {
            TrySubscribeGameManager();
            RefreshOutgoingLineVisibilityForMapSelection();
        }
    }

    private void OnDisable()
    {
        StopBlockedAlternateBatchSecondPhaseCoroutine();
        UnsubscribeGameManager();
    }

    private void Start()
    {
        if (Application.isPlaying)
        {
            TrySubscribeGameManager();
            RefreshOutgoingLineVisibilityForMapSelection();
        }
    }

    private void Update()
    {
        if (!Application.isPlaying || !debugDigitKeysSetAllEdgeStates || edges == null)
            return;

        MinimapEdgeState? hotkeyState = null;
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            hotkeyState = MinimapEdgeState.Disabled;
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            hotkeyState = MinimapEdgeState.Appearing;
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            hotkeyState = MinimapEdgeState.MovingAlongEdge;
        else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
            hotkeyState = MinimapEdgeState.Idle;
        else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
            hotkeyState = MinimapEdgeState.Blocked;
        else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6))
            hotkeyState = MinimapEdgeState.IdleRevealed;
        else if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7))
            hotkeyState = MinimapEdgeState.Selected;

        if (hotkeyState == null)
            return;

        var s = hotkeyState.Value;
        var n = 0;
        if (s == MinimapEdgeState.Blocked)
        {
            var batch = new List<MinimapEdge>();
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e == null)
                    continue;
                e.SetEdgeState(s, forceLog: true, suppressBlockedSlidersRampOnEnter: true);
                batch.Add(e);
                n++;
            }

            ScheduleBlockedSlidersSecondPhaseAfterRampDuration(batch);
        }
        else
        {
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e == null)
                    continue;
                e.SetEdgeState(s, forceLog: true);
                n++;
            }
        }

        Debug.Log($"[MinimapEdgeRegistry] Дебаг: всем рёбрам в списке ({n}) задано состояние {s}", this);
    }

    private void TrySubscribeGameManager()
    {
        if (_subscribedToGameManager || !Application.isPlaying)
            return;
        var gm = GameManager.Instance;
        if (gm == null)
            return;
        gm.MapNodeStateChanged += OnMapNodeStateChanged;
        _subscribedToGameManager = true;
    }

    private void UnsubscribeGameManager()
    {
        if (!_subscribedToGameManager)
            return;
        var gm = GameManager.Instance;
        if (gm != null)
            gm.MapNodeStateChanged -= OnMapNodeStateChanged;
        _subscribedToGameManager = false;
    }

    private void OnMapNodeStateChanged(Node node, NodeMapState newState, NodeMapState? previousState)
    {
        RefreshOutgoingLineVisibilityForMapSelection();
    }

    /// <summary>
    /// В Play Mode выставить видимость линий по <see cref="GameManager.CurrentSelectedMapNode"/> и <see cref="Node.SelectionOwner"/> у <see cref="MinimapEdge.FromNode"/>.
    /// Вне Play — все линии вкл (для редактирования).
    /// </summary>
    public void RefreshOutgoingLineVisibilityForMapSelection()
    {
        if (edges == null)
            return;

        if (Application.isPlaying && GameManager.Instance != null && GameManager.Instance.DebugRevealFullMinimap)
        {
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e != null)
                    e.SetMapOutgoingLineVisible(true);
            }

            RefreshEdgesEndMatchesSelectedMapNode();
            return;
        }

        if (!Application.isPlaying)
        {
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e != null)
                    e.SetMapOutgoingLineVisible(true);
            }

            return;
        }

        if (!showOutgoingLinesOnlyForSelectedMapNode)
        {
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e != null)
                    e.SetMapOutgoingLineVisible(true);
            }

            RefreshEdgesEndMatchesSelectedMapNode();
            return;
        }

        var gm = GameManager.Instance;
        var selected = gm != null ? gm.CurrentSelectedMapNode : null;

        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e == null)
                continue;

            bool show;
            if (selected != null)
            {
                show = e.FromNode != null &&
                       e.FromNode.SelectionOwner == selected;
            }
            else
            {
                show = gm != null && gm.ShouldShowMinimapEdgeWhenNothingSelected(e);
            }

            e.SetMapOutgoingLineVisible(show);
        }

        RefreshEdgesEndMatchesSelectedMapNode();
    }

    /// <summary>
    /// В Play: для рёбер, у которых конец — текущая выбранная нода карты, стейт <see cref="MinimapEdgeState.Selected"/>; иначе с <see cref="MinimapEdgeState.Selected"/> — в <see cref="MinimapEdgeState.IdleRevealed"/>.
    /// Не трогает Appearing / MovingAlongEdge / Blocked / Disabled.
    /// </summary>
    public void RefreshEdgesEndMatchesSelectedMapNode()
    {
        if (edges == null || !Application.isPlaying)
            return;

        var gm = GameManager.Instance;
        var selected = gm != null ? gm.CurrentSelectedMapNode : null;

        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e == null)
                continue;

            var s = e.CurrentEdgeState;
            if (s == MinimapEdgeState.Appearing ||
                s == MinimapEdgeState.MovingAlongEdge ||
                s == MinimapEdgeState.Blocked ||
                s == MinimapEdgeState.Disabled)
                continue;

            bool endIsSelected = selected != null &&
                                 e.ToNode != null &&
                                 e.ToNode.SelectionOwner == selected;

            if (endIsSelected)
            {
                if (s != MinimapEdgeState.Selected)
                    e.SetEdgeState(MinimapEdgeState.Selected, forceLog: false);
            }
            else if (s == MinimapEdgeState.Selected)
            {
                e.SetEdgeState(MinimapEdgeState.IdleRevealed, forceLog: false);
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RebuildEdgeCache();
        if (edges == null)
            return;
        if (!Application.isPlaying)
        {
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e != null)
                    e.SetMapOutgoingLineVisible(true);
            }
        }
        else
            RefreshOutgoingLineVisibilityForMapSelection();
    }
#endif

    /// <summary>
    /// Соседи в неориентированном графе рёбер (и from, и to).
    /// </summary>
    public void AddUndirectedNeighbors(Node node, HashSet<Node> into)
    {
        if (node == null || into == null || edges == null)
            return;

        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e == null)
                continue;
            Node a = e.FromNode;
            Node b = e.ToNode;
            if (a == node && b != null)
                into.Add(b);
            else if (b == node && a != null)
                into.Add(a);
        }
    }

    /// <summary>
    /// Все рёбра, у которых <see cref="MinimapEdge.ToNode"/> даёт тот же корень карты, что и <paramref name="endMapRoot"/> (<see cref="Node.SelectionOwner"/>),
    /// перевести в <see cref="MinimapEdgeState.Blocked"/>. Ноду <paramref name="endMapRoot"/> к этому моменту уже должны перевести в Blocked на карте.
    /// </summary>
    /// <param name="suppressBlockedSlidersRampOnEnter">Не запускать ramp на каждом ребре; ожидается <see cref="ScheduleBlockedSlidersSecondPhaseAfterRampDuration"/> для пакета.</param>
    public void SetAllEdgesEndingAtMapRootToBlocked(Node endMapRoot, bool suppressBlockedSlidersRampOnEnter = false)
    {
        if (edges == null || endMapRoot == null)
            return;

        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e == null || e.ToNode == null)
                continue;
            if (e.ToNode.SelectionOwner != endMapRoot)
                continue;
            e.SetEdgeState(
                MinimapEdgeState.Blocked,
                forceLog: false,
                suppressBlockedSlidersRampOnEnter: suppressBlockedSlidersRampOnEnter);
        }
    }

    /// <summary>Все рёбра с концом на <paramref name="endMapRoot"/> (по <see cref="Node.SelectionOwner"/> у <see cref="MinimapEdge.ToNode"/>).</summary>
    public void CollectEdgesEndingAtMapRoot(Node endMapRoot, HashSet<MinimapEdge> into)
    {
        if (edges == null || endMapRoot == null || into == null)
            return;

        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e == null || e.ToNode == null)
                continue;
            if (e.ToNode.SelectionOwner != endMapRoot)
                continue;
            into.Add(e);
        }
    }

    /// <summary>
    /// Одна задержка <c>max(ребро.BlockedSlidersRampDurationSeconds)</c>, затем одновременный ramp второй фазы для всех рёбер в пакете (альтернативы при travel / дебаг).
    /// </summary>
    public void ScheduleBlockedSlidersSecondPhaseAfterRampDuration(IReadOnlyList<MinimapEdge> edges)
    {
        if (!Application.isPlaying || edges == null || edges.Count == 0)
            return;

        StopBlockedAlternateBatchSecondPhaseCoroutine();

        float wait = 0.01f;
        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e == null)
                continue;
            wait = Mathf.Max(wait, e.BlockedSlidersRampDurationSeconds);
        }

        var snapshot = new List<MinimapEdge>(edges.Count);
        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e != null)
                snapshot.Add(e);
        }

        if (snapshot.Count == 0)
            return;

        _blockedAlternateBatchSecondPhaseCoroutine = StartCoroutine(CoBlockedAlternateBatchSecondPhaseAfterWait(snapshot, wait));
    }

    /// <summary>
    /// Сразу запускает вторую фазу ramp Blocked для рёбер (без ожидания из <see cref="ScheduleBlockedSlidersSecondPhaseAfterRampDuration"/>).
    /// Для travel: «первая фаза» по смыслу уже отыграна (brown ноды-источника), затем альтернативы и ноды концов получают ramp в одном пакете.
    /// </summary>
    public void RunBlockedSlidersSecondPhaseForEdgesNow(IReadOnlyList<MinimapEdge> edges)
    {
        if (!Application.isPlaying || edges == null || edges.Count == 0)
            return;

        StopBlockedAlternateBatchSecondPhaseCoroutine();
        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e == null || e.CurrentEdgeState != MinimapEdgeState.Blocked)
                continue;
            e.TryBeginBlockedSlidersRampSecondPhase();
        }
    }

    private void StopBlockedAlternateBatchSecondPhaseCoroutine()
    {
        if (_blockedAlternateBatchSecondPhaseCoroutine == null)
            return;
        StopCoroutine(_blockedAlternateBatchSecondPhaseCoroutine);
        _blockedAlternateBatchSecondPhaseCoroutine = null;
    }

    private IEnumerator CoBlockedAlternateBatchSecondPhaseAfterWait(List<MinimapEdge> snapshot, float waitSeconds)
    {
        yield return new WaitForSeconds(waitSeconds);
        _blockedAlternateBatchSecondPhaseCoroutine = null;
        for (var i = 0; i < snapshot.Count; i++)
        {
            var e = snapshot[i];
            if (e == null || e.CurrentEdgeState != MinimapEdgeState.Blocked)
                continue;
            e.TryBeginBlockedSlidersRampSecondPhase();
        }
    }

    /// <summary>
    /// Сбросить визуальное состояние рёбер в <see cref="MinimapEdgeState.Idle"/> (без логов).
    /// Пропускает рёбра в анимации / заблокированные, <see cref="MinimapEdgeState.IdleRevealed"/> / <see cref="MinimapEdgeState.Selected"/>, и с <see cref="MinimapEdge.PendingOutgoingAppearStagger"/> (задержка перед Appearing у стартовой ноды).
    /// </summary>
    public void SetAllEdgesVisualStateIdle()
    {
        if (edges == null)
            return;
        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            if (e == null)
                continue;
            var s = e.CurrentEdgeState;
            if (s == MinimapEdgeState.Appearing ||
                s == MinimapEdgeState.MovingAlongEdge ||
                s == MinimapEdgeState.Blocked)
                continue;
            if (s == MinimapEdgeState.IdleRevealed || s == MinimapEdgeState.Selected)
                continue;
            if (e.PendingOutgoingAppearStagger)
                continue;
            e.SetEdgeState(MinimapEdgeState.Idle, forceLog: false);
        }
    }

    /// <summary>Пересобрать кэш после смены списка рёбер в инспекторе или кода.</summary>
    public void RebuildEdgeCache()
    {
        _outgoingByFromNode.Clear();
        if (edges == null)
            return;

        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (edge == null)
                continue;

            Node from = edge.FromNode;
            if (from == null)
                continue;

            if (!_outgoingByFromNode.TryGetValue(from, out var list))
            {
                list = new List<MinimapEdge>();
                _outgoingByFromNode[from] = list;
            }

            list.Add(edge);
        }

        ApplySharedLinePaletteToAllEdges();
    }

    /// <summary>
    /// Все рёбра, у которых <see cref="MinimapEdge.FromNode"/> совпадает с ключом.
    /// При <paramref name="useSelectionOwner"/> ключом считается <see cref="Node.SelectionOwner"/> (для дочерней ноды группы — родитель).
    /// </summary>
    public IReadOnlyList<MinimapEdge> GetEdgesFrom(Node node, bool useSelectionOwner = true)
    {
        Node key = ResolveMapKey(node, useSelectionOwner);
        if (key == null)
            return System.Array.Empty<MinimapEdge>();

        return _outgoingByFromNode.TryGetValue(key, out var list)
            ? list
            : (IReadOnlyList<MinimapEdge>)System.Array.Empty<MinimapEdge>();
    }

    /// <summary>
    /// Есть ли ориентированное ребро from → to.
    /// По умолчанию «from» сопоставляется через SelectionOwner, «to» — точная ссылка на ноду конца ребра.
    /// </summary>
    public bool HasDirectedEdge(
        Node from,
        Node to,
        bool fromUseSelectionOwner = true,
        bool toUseSelectionOwner = false)
    {
        if (from == null || to == null)
            return false;

        Node fromKey = ResolveMapKey(from, fromUseSelectionOwner);
        Node toKey = ResolveMapKey(to, toUseSelectionOwner);
        if (fromKey == null || toKey == null)
            return false;

        if (!_outgoingByFromNode.TryGetValue(fromKey, out var list))
            return false;

        for (var i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null)
                continue;
            if (e.ToNode == toKey)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ориентированное ребро fromRoot → toRoot: «from» по <see cref="ResolveMapKey"/> с owner, конец по <see cref="Node.SelectionOwner"/> у <see cref="MinimapEdge.ToNode"/>.
    /// </summary>
    public MinimapEdge TryFindDirectedEdgeBetweenMapRoots(Node fromRoot, Node toRoot, bool fromUseSelectionOwner = true)
    {
        if (fromRoot == null || toRoot == null || edges == null)
            return null;

        Node fromKey = ResolveMapKey(fromRoot, fromUseSelectionOwner);
        if (fromKey == null)
            return null;

        if (!_outgoingByFromNode.TryGetValue(fromKey, out var list))
            return null;

        for (var i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null || e.ToNode == null)
                continue;
            if (e.ToNode.SelectionOwner == toRoot)
                return e;
        }

        return null;
    }

    /// <summary>
    /// Уникальные ноды-концы исходящих рёбер (поле <see cref="MinimapEdge.ToNode"/>), порядок по имени.
    /// Ключ «from» через <see cref="Node.SelectionOwner"/> при <paramref name="useSelectionOwner"/>.
    /// </summary>
    public IReadOnlyList<Node> GetOutgoingNeighborNodes(Node node, bool useSelectionOwner = true)
    {
        var fromEdges = GetEdgesFrom(node, useSelectionOwner);
        if (fromEdges.Count == 0)
            return System.Array.Empty<Node>();

        var acc = new List<Node>();
        for (var i = 0; i < fromEdges.Count; i++)
        {
            var t = fromEdges[i]?.ToNode;
            if (t == null)
                continue;
            if (!acc.Contains(t))
                acc.Add(t);
        }

        if (acc.Count == 0)
            return System.Array.Empty<Node>();

        acc.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return acc;
    }

    private static Node ResolveMapKey(Node node, bool useSelectionOwner)
    {
        if (node == null)
            return null;
        return useSelectionOwner ? node.SelectionOwner : node;
    }

    private void OnDrawGizmos()
    {
        if (!debugDrawEdgeGraph || edges == null)
            return;

        Gizmos.color = new Color(0f, 0.85f, 1f, 0.65f);
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (edge == null || edge.FromNode == null || edge.ToNode == null)
                continue;

            Vector3 a = edge.FromNode.transform.position;
            Vector3 b = edge.ToNode.transform.position;
            Gizmos.DrawLine(a, b);
        }
    }
}

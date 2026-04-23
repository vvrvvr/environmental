using System.Collections;
using UnityEngine;
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

    [Header("Edge state")]
    [Tooltip("Длительность «перемещение по ребру» перед автопереходом в Idle (только Play Mode).")]
    [SerializeField, Min(0.01f)] private float movingAlongEdgeDuration = 2f;

    [Tooltip("Длительность Appearing перед переходом в Idle (Play). 0 — считается как 1 с (заглушка).")]
    [SerializeField, Min(0f)] private float appearingToIdleDuration;

    [Tooltip("Цвет линии в состоянии Blocked (start/end).")]
    [SerializeField] private Color blockedLineColor = new Color(0.55f, 0.2f, 0.2f, 1f);

    [Header("Nodes")]
    [Tooltip("Начало ориентированного ребра на карте. Для группы — только родительская нода, не дочерняя.")]
    [SerializeField] private Node fromNode;

    [SerializeField] private Node toNode;

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

    public MinimapEdgeState CurrentEdgeState => _currentState;

    /// <summary>Длительность состояния <see cref="MinimapEdgeState.MovingAlongEdge"/> (оркестратор карты ждёт это время).</summary>
    public float MovingAlongEdgeDuration => movingAlongEdgeDuration;

    /// <summary>В Play: разрешение по выбору на карте (<see cref="MinimapEdgeRegistry"/>).</summary>
    public bool MapOutgoingLineVisible => _mapOutgoingLineVisible;

    private MinimapEdgeState _currentState = MinimapEdgeState.Idle;
    private MinimapEdgeState _stateAfterMovingCompletes = MinimapEdgeState.Idle;
    private bool _mapOutgoingLineVisible;
    private Color _defaultStart = Color.white;
    private Color _defaultEnd = Color.white;
    private bool _capturedDefaultLineColors;
    private Coroutine _movingCoroutine;
    private Coroutine _appearingCoroutine;

    private void Awake()
    {
        CacheLineDefaultColorsIfNeeded();
    }

    private void Start()
    {
        if (!Application.isPlaying)
            return;
        _currentState = MinimapEdgeState.Idle;
        ApplyCombinedVisual();
    }

    private void OnEnable()
    {
        CacheLineDefaultColorsIfNeeded();
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

        CacheLineDefaultColorsIfNeeded();
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

    /// <summary>Смена состояния ребра (в т.ч. дебаг с реестра по цифрам 1–5).</summary>
    /// <param name="stateAfterMovingCompletes">Только для <see cref="MinimapEdgeState.MovingAlongEdge"/>: во что перейти после таймера (по умолчанию <see cref="MinimapEdgeState.Idle"/>).</param>
    public void SetEdgeState(MinimapEdgeState next, bool forceLog = true, MinimapEdgeState stateAfterMovingCompletes = MinimapEdgeState.Idle)
    {
        if (!Application.isPlaying)
        {
            var prev = _currentState;
            _currentState = next;
            if (forceLog && prev != next)
                Debug.Log($"[{name}] MinimapEdge state: {prev} → {next} (не Play — таймер Moving не запускается)", this);
            ApplyCombinedVisual();
            return;
        }

        if (_currentState == next && next != MinimapEdgeState.MovingAlongEdge && next != MinimapEdgeState.Appearing)
            return;

        StopEdgePlayCoroutines();
        var prevPlay = _currentState;
        _currentState = next;
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
                _appearingCoroutine = StartCoroutine(CoAppearingThenIdle());
        }

        ApplyCombinedVisual();
    }

    private IEnumerator CoMovingAlongEdge()
    {
        Debug.Log($"[{name}] Moving along edge: {movingAlongEdgeDuration:0.##} s…", this);
        float t = 0f;
        while (t < movingAlongEdgeDuration)
        {
            t += Time.deltaTime;
            yield return null;
        }

        _movingCoroutine = null;
        var landed = _stateAfterMovingCompletes;
        _stateAfterMovingCompletes = MinimapEdgeState.Idle;
        _currentState = landed;
        Debug.Log($"[{name}] MinimapEdge state: MovingAlongEdge → {landed} (timer done)", this);
        ApplyCombinedVisual();
    }

    private IEnumerator CoAppearingThenIdle()
    {
        float d = appearingToIdleDuration <= 0f ? 1f : appearingToIdleDuration;
        yield return new WaitForSeconds(d);
        _appearingCoroutine = null;
        if (_currentState != MinimapEdgeState.Appearing)
            yield break;
        _currentState = MinimapEdgeState.Idle;
        ApplyCombinedVisual();
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
        StopCoroutine(_movingCoroutine);
        _movingCoroutine = null;
    }

    private void StopAppearingIfAny()
    {
        if (_appearingCoroutine == null)
            return;
        StopCoroutine(_appearingCoroutine);
        _appearingCoroutine = null;
    }

    private void CacheLineDefaultColorsIfNeeded()
    {
        if (lineRenderer == null || _capturedDefaultLineColors)
            return;
        _defaultStart = lineRenderer.startColor;
        _defaultEnd = lineRenderer.endColor;
        _capturedDefaultLineColors = true;
    }

    /// <summary>
    /// Карта вне Play всегда «разрешает» линию для вёрстки.
    /// В Play: линия по <see cref="_mapOutgoingLineVisible"/> (исходящие от выбранной / стартов без выбора) или всегда при <see cref="MinimapEdgeState.Blocked"/>, чтобы заблокированные маршруты не пропадали, а рисовались цветом <see cref="blockedLineColor"/>.
    /// <see cref="MinimapEdgeState.Disabled"/> выключает линию всегда.
    /// </summary>
    private void ApplyCombinedVisual()
    {
        if (lineRenderer == null)
            return;

        bool mapAllows = !Application.isPlaying ||
                         _mapOutgoingLineVisible ||
                         _currentState == MinimapEdgeState.Blocked;
        bool stateShowsLine = _currentState != MinimapEdgeState.Disabled;
        lineRenderer.enabled = mapAllows && stateShowsLine;

        if (!lineRenderer.enabled)
            return;

        if (_currentState == MinimapEdgeState.Blocked)
        {
            lineRenderer.startColor = blockedLineColor;
            lineRenderer.endColor = blockedLineColor;
        }
        else
        {
            lineRenderer.startColor = _defaultStart;
            lineRenderer.endColor = _defaultEnd;
        }
    }

    [ContextMenu("Refresh line")]
    public void RefreshLinePositions()
    {
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
        lineRenderer.SetPosition(0, p0);
        lineRenderer.SetPosition(1, p1);
        ApplyMiddle((p0 + p1) * 0.5f);
    }

    private void ApplyMiddle(Vector3 worldMid)
    {
        if (middlePoint == null)
            return;
        middlePoint.position = worldMid;
    }
}

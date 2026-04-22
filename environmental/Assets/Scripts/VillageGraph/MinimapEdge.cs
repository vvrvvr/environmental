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

    [Tooltip("Цвет линии в состоянии Blocked (start/end).")]
    [SerializeField] private Color blockedLineColor = new Color(0.55f, 0.2f, 0.2f, 1f);

    [Header("Nodes")]
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

    private MinimapEdgeState _currentState = MinimapEdgeState.Idle;
    private Color _defaultStart = Color.white;
    private Color _defaultEnd = Color.white;
    private bool _capturedDefaultLineColors;
    private Coroutine _movingCoroutine;

    private void Awake()
    {
        CacheLineDefaultColorsIfNeeded();
    }

    private void Start()
    {
        if (!Application.isPlaying)
            return;
        _currentState = MinimapEdgeState.Idle;
        ApplyStateVisuals();
    }

    private void OnEnable()
    {
        CacheLineDefaultColorsIfNeeded();
        RefreshLinePositions();
#if UNITY_EDITOR
        EditorApplication.update += EditorPoll;
#endif
        ApplyStateVisuals();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorPoll;
#endif
        StopMovingIfAny();
    }

#if UNITY_EDITOR
    private void EditorPoll()
    {
        if (!Application.isPlaying)
            RefreshLinePositions();
    }

    private void OnValidate()
    {
        CacheLineDefaultColorsIfNeeded();
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            RefreshLinePositions();
        ApplyStateVisuals();
    }
#endif

    private void LateUpdate()
    {
        if (Application.isPlaying)
            RefreshLinePositions();
    }

    /// <summary>Смена состояния (в т.ч. дебаг с реестра по цифрам 1–5).</summary>
    public void SetEdgeState(MinimapEdgeState next, bool forceLog = true)
    {
        if (!Application.isPlaying)
        {
            var prev = _currentState;
            _currentState = next;
            if (forceLog && prev != next)
                Debug.Log($"[{name}] MinimapEdge state: {prev} → {next} (не Play Mode — таймер Moving не запускается)", this);
            ApplyStateVisuals();
            return;
        }

        if (_currentState == next && next != MinimapEdgeState.MovingAlongEdge)
            return;

        StopMovingIfAny();
        var prevPlay = _currentState;
        _currentState = next;
        if (forceLog)
            Debug.Log($"[{name}] MinimapEdge state: {prevPlay} → {next}", this);

        if (next == MinimapEdgeState.MovingAlongEdge)
            _movingCoroutine = StartCoroutine(CoMovingAlongEdge());

        ApplyStateVisuals();
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
        _currentState = MinimapEdgeState.Idle;
        Debug.Log($"[{name}] MinimapEdge state: MovingAlongEdge → Idle (timer done)", this);
        ApplyStateVisuals();
    }

    private void StopMovingIfAny()
    {
        if (_movingCoroutine == null)
            return;
        StopCoroutine(_movingCoroutine);
        _movingCoroutine = null;
    }

    private void CacheLineDefaultColorsIfNeeded()
    {
        if (lineRenderer == null || _capturedDefaultLineColors)
            return;
        _defaultStart = lineRenderer.startColor;
        _defaultEnd = lineRenderer.endColor;
        _capturedDefaultLineColors = true;
    }

    private void ApplyStateVisuals()
    {
        if (lineRenderer == null)
            return;

        bool lineOn = _currentState != MinimapEdgeState.Disabled;
        lineRenderer.enabled = lineOn;

        if (!lineOn)
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

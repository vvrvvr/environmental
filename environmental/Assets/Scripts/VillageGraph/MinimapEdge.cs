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

    private void OnEnable()
    {
        RefreshLinePositions();
#if UNITY_EDITOR
        EditorApplication.update += EditorPoll;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorPoll;
#endif
    }

#if UNITY_EDITOR
    private void EditorPoll()
    {
        if (!Application.isPlaying)
            RefreshLinePositions();
    }

    private void OnValidate()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            RefreshLinePositions();
    }
#endif

    private void LateUpdate()
    {
        if (Application.isPlaying)
            RefreshLinePositions();
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

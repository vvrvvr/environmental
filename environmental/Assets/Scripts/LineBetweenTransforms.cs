using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Keeps a <see cref="LineRenderer"/> between two GameObjects along the segment A→B, with optional
/// world-space insets so the line does not touch the transform centers. LineRenderer width/material/etc. stay as configured.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public class LineBetweenTransforms : MonoBehaviour
{
    [SerializeField] private GameObject startPoint;
    [SerializeField] private GameObject endPoint;

    [Header("Middle")]
    [Tooltip("Placed at the midpoint of the rendered line segment (after insets), in world space.")]
    [SerializeField] private GameObject middlePoint;

    [Header("Insets (world units along A→B)")]
    [Tooltip("Distance from the start object's center toward the end — line begins here.")]
    [SerializeField] [Min(0f)] private float startInset;
    [Tooltip("Distance from the end object's center back toward the start — line ends here.")]
    [SerializeField] [Min(0f)] private float endInset;

    private LineRenderer line;

    public GameObject StartPoint
    {
        get => startPoint;
        set => startPoint = value;
    }

    public GameObject EndPoint
    {
        get => endPoint;
        set => endPoint = value;
    }

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

    public GameObject MiddlePoint
    {
        get => middlePoint;
        set => middlePoint = value;
    }

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
    }

    private void OnEnable()
    {
        if (line == null)
            line = GetComponent<LineRenderer>();
        RefreshLine();
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
            RefreshLine();
    }
#endif

    private void LateUpdate()
    {
        if (Application.isPlaying)
            RefreshLine();
    }

    /// <summary>Updates positions from current references (also usable from editor).</summary>
    [ContextMenu("Refresh line")]
    public void RefreshLine()
    {
        if (line == null)
            line = GetComponent<LineRenderer>();

        if (line == null || startPoint == null || endPoint == null)
            return;

        var a = startPoint.transform.position;
        var b = endPoint.transform.position;
        var delta = b - a;
        var len = delta.magnitude;

        line.positionCount = 2;
        line.useWorldSpace = true;

        if (len < 1e-6f)
        {
            line.SetPosition(0, a);
            line.SetPosition(1, a);
            ApplyMiddle(a);
            return;
        }

        var dir = delta / len;

        // Along the segment from A: first vertex at +startInset, second at len - endInset.
        var along0 = Mathf.Clamp(startInset, 0f, len);
        var along1 = len - Mathf.Clamp(endInset, 0f, len);
        if (along0 >= along1)
        {
            var mid = len * 0.5f;
            var eps = Mathf.Max(1e-5f, len * 1e-4f);
            along0 = Mathf.Max(0f, mid - eps);
            along1 = Mathf.Min(len, mid + eps);
            if (along0 >= along1)
                along1 = Mathf.Min(len, along0 + 1e-5f);
        }

        var p0 = a + dir * along0;
        var p1 = a + dir * along1;
        line.SetPosition(0, p0);
        line.SetPosition(1, p1);
        ApplyMiddle((p0 + p1) * 0.5f);
    }

    private void ApplyMiddle(Vector3 worldMid)
    {
        if (middlePoint == null)
            return;
        middlePoint.transform.position = worldMid;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (line == null)
            line = GetComponent<LineRenderer>();
        RefreshLine();
    }
#endif
}

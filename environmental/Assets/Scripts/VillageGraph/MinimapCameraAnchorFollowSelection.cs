using DG.Tweening;
using UnityEngine;

/// <summary>
/// Якорь камеры мини-карты: при выборе ноды, если расстояние по XY от якоря до ноды больше порога,
/// якорь плавно смещается в плоскости XY к целевой точке (центр ноды), Z мировой позиции якоря сохраняется.
/// В редакторе вне Play Mode в Scene рисуется wire-сфера радиуса <see cref="maxDistanceXY"/> вокруг якоря (ориентир границы).
/// </summary>
[DisallowMultipleComponent]
public sealed class MinimapCameraAnchorFollowSelection : MonoBehaviour
{
    [Tooltip("Объект-якорь, к которому привязана камера мини-карты (его Transform двигается).")]
    [SerializeField]
    private Transform minimapCameraAnchor;

    [Tooltip("Источник событий выбора на карте.")]
    [SerializeField]
    private GameManager gameManager;

    [Tooltip("Если расстояние по осям X и Y от якоря до ноды не больше этого значения — движение не выполняется.")]
    [SerializeField]
    [Min(0f)]
    private float maxDistanceXY = 3f;

    [Tooltip("Длительность перемещения якоря (сек). При 0 — мгновенный скачок к цели.")]
    [SerializeField]
    [Min(0f)]
    private float moveDurationSeconds = 0.45f;

    [Tooltip("Unscaled — не зависит от Time.timeScale.")]
    [SerializeField]
    private bool unscaledTime = true;

    [SerializeField]
    private Ease moveEase = Ease.OutQuad;

    [Tooltip("Цель по позиции: корень группы (родитель) вместо той ноды, что пришла в Selected (например дочерняя в группе).")]
    [SerializeField]
    private bool useSelectionOwnerTransform;

    private void OnEnable()
    {
        if (gameManager != null)
            gameManager.MapNodeStateChanged += OnMapNodeStateChanged;
    }

    private void OnDisable()
    {
        if (gameManager != null)
            gameManager.MapNodeStateChanged -= OnMapNodeStateChanged;

        DOTween.Kill(this, complete: false);
    }

    private void OnMapNodeStateChanged(Node node, NodeMapState newState, NodeMapState? previousState)
    {
        if (newState != NodeMapState.Selected || node == null)
            return;
        if (minimapCameraAnchor == null)
            return;

        var targetNode = useSelectionOwnerTransform ? node.SelectionOwner : node;
        if (targetNode == null)
            return;

        var anchor = minimapCameraAnchor;
        var targetPos = targetNode.transform.position;

        var a = new Vector2(anchor.position.x, anchor.position.y);
        var b = new Vector2(targetPos.x, targetPos.y);
        if (Vector2.Distance(a, b) <= maxDistanceXY)
            return;

        DOTween.Kill(this, complete: false);

        var end = new Vector3(targetPos.x, targetPos.y, anchor.position.z);
        var duration = Mathf.Max(0f, moveDurationSeconds);
        if (duration <= 0f)
        {
            anchor.position = end;
            return;
        }

        anchor.DOMove(end, duration)
            .SetEase(moveEase)
            .SetId(this)
            .SetLink(gameObject)
            .SetUpdate(unscaledTime);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (Application.isPlaying)
            return;
        if (minimapCameraAnchor == null)
            return;

        var r = Mathf.Max(0f, maxDistanceXY);
        if (r <= 0f)
            return;

        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.85f);
        Gizmos.DrawWireSphere(minimapCameraAnchor.position, r);
    }
#endif
}

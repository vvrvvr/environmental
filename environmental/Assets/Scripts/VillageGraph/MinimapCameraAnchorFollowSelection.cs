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

    private Vector3 _initialAnchorWorldPosition;
    private bool _hasInitialAnchorPosition;

    private void Awake()
    {
        CacheInitialAnchorPosition();
    }

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

    /// <summary>Запомнить текущую мировую позицию якоря как стартовую (вызывается в Awake).</summary>
    public void CacheInitialAnchorPosition()
    {
        if (minimapCameraAnchor == null)
        {
            _hasInitialAnchorPosition = false;
            return;
        }

        _initialAnchorWorldPosition = minimapCameraAnchor.position;
        _hasInitialAnchorPosition = true;
    }

    /// <summary>Плавно вернуть якорь к стартовой позиции (тот же moveEase / unscaledTime, что при следовании за нодой).</summary>
    public Tween TweenAnchorToInitialPosition(float duration)
    {
        if (!_hasInitialAnchorPosition)
            CacheInitialAnchorPosition();
        if (!_hasInitialAnchorPosition)
            return null;

        return TweenAnchorMoveTo(_initialAnchorWorldPosition, duration);
    }

    private Tween TweenAnchorMoveTo(Vector3 endWorldPosition, float duration)
    {
        if (minimapCameraAnchor == null)
            return null;

        var anchor = minimapCameraAnchor;
        DOTween.Kill(this, complete: false);

        if ((anchor.position - endWorldPosition).sqrMagnitude < 1e-6f)
            return null;

        duration = Mathf.Max(0f, duration);
        if (duration <= 0f)
        {
            anchor.position = endWorldPosition;
            return null;
        }

        return anchor.DOMove(endWorldPosition, duration)
            .SetEase(moveEase)
            .SetId(this)
            .SetLink(gameObject)
            .SetUpdate(unscaledTime);
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

        var end = new Vector3(targetPos.x, targetPos.y, anchor.position.z);
        TweenAnchorMoveTo(end, moveDurationSeconds);
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

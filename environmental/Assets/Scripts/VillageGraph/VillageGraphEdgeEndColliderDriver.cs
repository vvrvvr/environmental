using UnityEngine;
using DG.Tweening;

/// <summary>
/// Префаб/инстанс коллайдера конца ребра: кеширует <see cref="MinimapEdge"/> (ToNode на end anchor).
/// В фазе <see cref="MinimapEdgeState.Appearing"/>: <see cref="DOScaleY"/> и позиция — рост к конечному якорю + сдвиг «назад» вдоль ребра
/// от <c>длина_ребра × (процент/100)</c> к нулю за то же время (процент задаётся из <see cref="VillageGraphPhysicsSetup"/>).
/// </summary>
[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public sealed class VillageGraphEdgeEndColliderDriver : MonoBehaviour
{
    [Tooltip("Заполняется из VillageGraphPhysicsSetup при построении графа.")]
    [SerializeField]
    private MinimapEdge boundMinimapEdge;

    [Header("Debug cache (при первом Appearing)")]
    [SerializeField]
    private float cachedPlannedLocalScaleY;

    [SerializeField]
    private float cachedAppearDurationSeconds;

    [SerializeField]
    private float configuredShiftBackPercentOfEdgeLength;

    private Collider[] _colliders;
    private float _sourceScaleYAtBuild;
    private bool _capturedForCurrentAppearSession;

    private float _growWorldLengthFull;
    private float _shiftBackPercentOfEdgeLength;

    private MinimapEdgeState? _previousEdgeState;

    /// <summary>Кеш ребра, процента сдвига назад по длине ребра (0–100), выключенные коллайдеры.</summary>
    public void Configure(MinimapEdge edge, float shiftBackPercentOfEdgeLength)
    {
        boundMinimapEdge = edge;
        configuredShiftBackPercentOfEdgeLength = Mathf.Clamp(shiftBackPercentOfEdgeLength, 0f, 100f);
        _shiftBackPercentOfEdgeLength = configuredShiftBackPercentOfEdgeLength;
        _colliders = GetComponentsInChildren<Collider>(true);
        _sourceScaleYAtBuild = transform.localScale.y;
        _capturedForCurrentAppearSession = false;
        _previousEdgeState = null;
        _growWorldLengthFull = 0f;
        transform.DOKill(complete: false);
        SetCollidersEnabled(false);
    }

    private void OnDisable()
    {
        transform.DOKill(complete: false);
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || boundMinimapEdge == null)
            return;

        var state = boundMinimapEdge.CurrentEdgeState;

        if (_previousEdgeState == MinimapEdgeState.Appearing && state != MinimapEdgeState.Appearing)
        {
            transform.DOKill(complete: false);
            if (_growWorldLengthFull > 1e-6f)
                SnapFullGrowTowardEndAnchor();
        }

        switch (state)
        {
            case MinimapEdgeState.Appearing:
                if (!_capturedForCurrentAppearSession)
                {
                    _capturedForCurrentAppearSession = true;

                    var plannedY = transform.localScale.y;
                    if (plannedY < 1e-6f)
                        plannedY = _sourceScaleYAtBuild;

                    cachedPlannedLocalScaleY = plannedY;
                    cachedAppearDurationSeconds = boundMinimapEdge.AppearingPhaseDurationSeconds;

                    _growWorldLengthFull = MeasureColliderWorldExtentAlongAxis(transform, transform.up);

                    var s = transform.localScale;
                    s.y = 0f;
                    transform.localScale = s;

                    transform.DOKill(complete: false);
                    transform.DOScaleY(cachedPlannedLocalScaleY, cachedAppearDurationSeconds);

                    Debug.Log(
                        $"[VillageGraphEdgeEndColliderDriver] '{name}': planned localScale.y = {cachedPlannedLocalScaleY}, " +
                        $"duration = {cachedAppearDurationSeconds}s, shiftBack%OfEdge = {_shiftBackPercentOfEdgeLength}, edge = '{boundMinimapEdge.name}'",
                        this);

                    SetCollidersEnabled(true);
                }
                else
                {
                    SetCollidersEnabled(true);
                }

                if (_capturedForCurrentAppearSession && cachedPlannedLocalScaleY > 1e-8f)
                {
                    var ty = Mathf.Clamp01(transform.localScale.y / cachedPlannedLocalScaleY);
                    UpdateGrowTransform(ty);
                }

                break;

            case MinimapEdgeState.Disabled:
                transform.DOKill(complete: false);
                SetCollidersEnabled(false);
                break;
        }

        if (state != MinimapEdgeState.Appearing)
            _capturedForCurrentAppearSession = false;

        _previousEdgeState = state;
    }

    private void SnapFullGrowTowardEndAnchor()
    {
        var sc = transform.localScale;
        sc.y = cachedPlannedLocalScaleY;
        transform.localScale = sc;
        UpdateGrowTransform(1f);
    }

    /// <param name="ty01">Прогресс роста по высоте 0…1 (как доля целевого localScale.y).</param>
    private void UpdateGrowTransform(float ty01)
    {
        if (boundMinimapEdge == null)
            return;

        var sa = boundMinimapEdge.StartAnchor;
        var ea = boundMinimapEdge.EndAnchor;
        if (sa == null || ea == null)
            return;

        var startW = sa.position;
        var endW = ea.position;
        var dir = endW - startW;
        if (dir.sqrMagnitude < 1e-10f)
            return;
        dir.Normalize();

        var edgeLen = Vector3.Distance(startW, endW);
        var midW = (startW + endW) * 0.5f;
        var ty = Mathf.Clamp01(ty01);

        // Если коллайдеры не дали extent (0), опираемся на длину ребра между якорями — иначе позиция не менялась бы вообще.
        var lengthAlong = _growWorldLengthFull > 1e-6f ? _growWorldLengthFull : Mathf.Max(edgeLen, 1e-5f);

        var halfL = lengthAlong * 0.5f * ty;
        var basePos = midW + dir * (halfL - lengthAlong * 0.5f);

        var shiftBackWorld = -dir * (edgeLen * (_shiftBackPercentOfEdgeLength * 0.01f) * (1f - ty));
        transform.SetPositionAndRotation(basePos + shiftBackWorld, Quaternion.FromToRotation(Vector3.up, dir));
    }

    private void SetCollidersEnabled(bool value)
    {
        if (_colliders == null)
            _colliders = GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < _colliders.Length; i++)
        {
            var c = _colliders[i];
            if (c != null)
                c.enabled = value;
        }
    }

    private static float MeasureColliderWorldExtentAlongAxis(Transform root, Vector3 axisWorld)
    {
        if (root == null)
            return 0f;

        var cols = root.GetComponentsInChildren<Collider>(true);
        if (cols == null || cols.Length == 0)
            return 0f;

        var w = cols[0].bounds;
        for (var i = 1; i < cols.Length; i++)
            w.Encapsulate(cols[i].bounds);

        var up = axisWorld.normalized;
        var c = w.center;
        var e = w.extents;
        var minP = float.MaxValue;
        var maxP = float.MinValue;
        for (var dx = -1; dx <= 1; dx += 2)
        for (var dy = -1; dy <= 1; dy += 2)
        for (var dz = -1; dz <= 1; dz += 2)
        {
            var corner = c + new Vector3(dx * e.x, dy * e.y, dz * e.z);
            var p = Vector3.Dot(corner, up);
            if (p < minP)
                minP = p;
            if (p > maxP)
                maxP = p;
        }

        return Mathf.Max(0f, maxP - minP);
    }
}

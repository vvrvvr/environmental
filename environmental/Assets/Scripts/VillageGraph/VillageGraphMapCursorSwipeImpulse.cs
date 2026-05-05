using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// При быстром движении курсора по карте (камера <see cref="GameManager.MapCamera"/> или своё поле)
/// луч попадает в коллайдер ноды или коллайдера ребра — на связанный <see cref="Rigidbody"/> даётся импульс
/// в мировой плоскости XY (компонента Z у силы нет). Скорость курсора измеряется в той же плоскости Z = центр коллайдера.
/// </summary>
[DisallowMultipleComponent]
public sealed class VillageGraphMapCursorSwipeImpulse : MonoBehaviour
{
    /// <summary>
    /// Fired when this component applies impulse to graph rigidbody.
    /// Args: impulseMagnitude, impulseDirectionWorldXY(normalized, z=0), targetRigidbody.
    /// </summary>
    public static event Action<float, Vector3, Rigidbody> GraphImpulseApplied;

    [Header("Refs")]
    [Tooltip("Если пусто — GameManager.Instance.MapCamera.")]
    [SerializeField]
    private Camera mapCameraOverride;

    [Header("Raycast")]
    [SerializeField]
    private LayerMask raycastLayers = ~0;

    [SerializeField]
    private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;

    [Header("Speed → impulse (world XY)")]
    [Tooltip("Ниже этой скорости (юнит/с в плоскости XY при фиксированном Z центра коллайдера) импульс не даётся.")]
    [SerializeField, Min(0f)]
    private float minCursorSpeedWorldXY = 1.2f;

    [Tooltip("При этой скорости и выше достигается maxImpulse (линейный рост от min до max).")]
    [SerializeField, Min(0.0001f)]
    private float maxCursorSpeedWorldXY = 12f;

    [Tooltip("Максимальная величина импульса (AddForce, ForceMode.Impulse).")]
    [SerializeField, Min(0f)]
    private float maxImpulse = 4f;

    [Tooltip("Дополнительный множитель к величине импульса после нормализации по скорости.")]
    [SerializeField, Min(0f)]
    private float impulseStrengthMultiplier = 1f;

    [Header("Throttle")]
    [Tooltip("Минимум секунд между импульсами на один и тот же Rigidbody (анти-спам при дрожании на пороге).")]
    [SerializeField, Min(0f)]
    private float minSecondsBetweenImpulsesPerBody = 0.08f;

    [Header("Toggle")]
    [SerializeField]
    private bool enableSwipeImpulse = true;

    private Camera _cachedCamera;
    private Collider _trackedCollider;
    private Vector3 _lastWorldPointOnPlane;
    private bool _hasLastSample;
    private readonly Dictionary<int, float> _lastImpulseTimeByRb = new Dictionary<int, float>(32);

    private void Update()
    {
        if (!enableSwipeImpulse || !Application.isPlaying)
            return;

        TryResolveCamera();
        if (_cachedCamera == null)
            return;

        var ray = _cachedCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, Mathf.Infinity, raycastLayers, triggerInteraction))
        {
            ClearTracking();
            return;
        }

        var col = hit.collider;
        if (col == null)
        {
            ClearTracking();
            return;
        }

        var rb = ResolveRigidbody(col);
        if (rb == null || rb.isKinematic)
        {
            ClearTracking();
            return;
        }

        var planeZ = col.bounds.center.z;
        if (!TryRayIntersectWorldXYPlaneAtZ(ray, planeZ, out var worldOnPlane))
        {
            ClearTracking();
            return;
        }

        if (_trackedCollider != col)
        {
            _trackedCollider = col;
            _lastWorldPointOnPlane = worldOnPlane;
            _hasLastSample = true;
            return;
        }

        if (!_hasLastSample)
        {
            _lastWorldPointOnPlane = worldOnPlane;
            _hasLastSample = true;
            return;
        }

        var dt = Time.unscaledDeltaTime;
        var delta = worldOnPlane - _lastWorldPointOnPlane;
        _lastWorldPointOnPlane = worldOnPlane;

        if (dt < 1e-6f)
            return;

        var vel = delta / dt;
        var speedXY = new Vector3(vel.x, vel.y, 0f).magnitude;

        if (speedXY < minCursorSpeedWorldXY)
            return;

        var speedHi = Mathf.Max(maxCursorSpeedWorldXY, minCursorSpeedWorldXY + 1e-4f);
        var t = Mathf.InverseLerp(minCursorSpeedWorldXY, speedHi, Mathf.Clamp(speedXY, minCursorSpeedWorldXY, speedHi));
        var impulseMag = t * maxImpulse * impulseStrengthMultiplier;
        if (impulseMag <= 0f)
            return;

        var dir = new Vector3(delta.x, delta.y, 0f);
        if (dir.sqrMagnitude < 1e-12f)
            return;
        dir.Normalize();

        var rbId = rb.GetInstanceID();
        var now = Time.unscaledTime;
        if (minSecondsBetweenImpulsesPerBody > 0f &&
            _lastImpulseTimeByRb.TryGetValue(rbId, out var lastT) &&
            now - lastT < minSecondsBetweenImpulsesPerBody)
            return;

        rb.AddForce(dir * impulseMag, ForceMode.Impulse);
        GraphImpulseApplied?.Invoke(impulseMag, dir, rb);
        _lastImpulseTimeByRb[rbId] = now;
    }

    private void ClearTracking()
    {
        _trackedCollider = null;
        _hasLastSample = false;
    }

    private void TryResolveCamera()
    {
        if (_cachedCamera != null)
            return;
        if (mapCameraOverride != null)
        {
            _cachedCamera = mapCameraOverride;
            return;
        }
        if (GameManager.Instance != null)
            _cachedCamera = GameManager.Instance.MapCamera;
    }

    private static Rigidbody ResolveRigidbody(Collider col)
    {
        var rb = col.attachedRigidbody;
        if (rb != null)
            return rb;
        return col.GetComponentInParent<Rigidbody>();
    }

    /// <summary>Пересечение луча с мировой плоскостью Z = const (движение курсора в координатах XY на этой глубине).</summary>
    private static bool TryRayIntersectWorldXYPlaneAtZ(Ray ray, float zPlane, out Vector3 hit)
    {
        var dz = ray.direction.z;
        if (Mathf.Abs(dz) < 1e-7f)
        {
            hit = default;
            return false;
        }

        var t = (zPlane - ray.origin.z) / dz;
        if (t < 0f)
        {
            hit = default;
            return false;
        }

        hit = ray.origin + ray.direction * t;
        hit.z = zPlane;
        return true;
    }
}

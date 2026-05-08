using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Быстрый свайп мыши по карте: импульс по <see cref="Rigidbody"/> ноды или ребра.
/// Между кадрами делается несколько raycast вдоль отрезка экранной позиции мыши, чтобы не «перепрыгивать» тонкие коллайдеры рёбер.
/// Скорость считается по смещению курсора на фиксированной мировой плоскости Z (глубина попадания).
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

    [Header("Sweep (fast mouse)")]
    [Tooltip("Шаг в пикселях экрана между промежуточными лучами вдоль движения мыши (меньше — надёжнее для тонких рёбер).")]
    [SerializeField, Min(1f)]
    private float sweepStepPixels = 5f;

    [Tooltip("Верхняя граница числа лучей за кадр.")]
    [SerializeField, Min(1)]
    private int maxSweepSteps = 48;

    [Header("Speed → impulse (world XY)")]
    [Tooltip("Ниже этой скорости (юнит/с в плоскости XY) импульс не даётся.")]
    [SerializeField, Min(0f)]
    private float minCursorSpeedWorldXY = 1.2f;

    [Tooltip("При этой скорости и выше нормализация скорости к 1 перед умножением на maxImpulse.")]
    [SerializeField, Min(0.0001f)]
    private float maxCursorSpeedWorldXY = 12f;

    [Tooltip("Жёсткий потолок величины импульса (AddForce, ForceMode.Impulse). Итог не превышает это значение.")]
    [SerializeField, Min(0f)]
    private float maxImpulse = 4f;

    [Tooltip("Множитель к вычисленной величине до применения потолка maxImpulse.")]
    [SerializeField, Min(0f)]
    private float impulseStrengthMultiplier = 1f;

    [Header("Throttle")]
    [Tooltip("Минимум секунд между импульсами на один и тот же Rigidbody.")]
    [SerializeField, Min(0f)]
    private float minSecondsBetweenImpulsesPerBody = 0.08f;

    [Header("Toggle")]
    [SerializeField]
    private bool enableSwipeImpulse = true;

    public float MinSwipeSpeedWorldXY => minCursorSpeedWorldXY;
    public float MaxSwipeSpeedWorldXY => maxCursorSpeedWorldXY;

    private Camera _cachedCamera;
    private Vector3 _prevMouseScreen;
    private bool _hasPrevMouse;
    private readonly Dictionary<int, float> _lastImpulseTimeByRb = new Dictionary<int, float>(32);

    private void Update()
    {
        if (!enableSwipeImpulse || !Application.isPlaying)
            return;

        TryResolveCamera();
        if (_cachedCamera == null)
            return;

        var currScreen = Input.mousePosition;
        if (!_hasPrevMouse)
        {
            _prevMouseScreen = currScreen;
            _hasPrevMouse = true;
            return;
        }

        var prevScreen = _prevMouseScreen;
        _prevMouseScreen = currScreen;

        var pixelDist = Vector2.Distance(
            new Vector2(prevScreen.x, prevScreen.y),
            new Vector2(currScreen.x, currScreen.y));

        var steps = pixelDist < 0.5f
            ? 1
            : Mathf.Clamp(Mathf.CeilToInt(pixelDist / Mathf.Max(1f, sweepStepPixels)), 1, maxSweepSteps);

        Rigidbody wasGraphRb = null;
        var rayPrevProbe = _cachedCamera.ScreenPointToRay(prevScreen);
        if (Physics.Raycast(rayPrevProbe, out var prevProbeHit, Mathf.Infinity, raycastLayers, triggerInteraction) &&
            prevProbeHit.collider != null)
            TryGetGraphRigidbody(prevProbeHit.collider, out wasGraphRb);

        Collider graphCol = null;
        Rigidbody graphRb = null;
        var firstGraphHitStep = -1;

        for (var i = 0; i <= steps; i++)
        {
            var u = steps <= 1 ? 1f : i / (float)steps;
            var sp = Vector3.Lerp(prevScreen, currScreen, u);
            var ray = _cachedCamera.ScreenPointToRay(sp);
            if (!Physics.Raycast(ray, out var hit, Mathf.Infinity, raycastLayers, triggerInteraction))
                continue;
            if (hit.collider == null)
                continue;
            if (!TryGetGraphRigidbody(hit.collider, out var rb))
                continue;
            graphCol = hit.collider;
            graphRb = rb;
            firstGraphHitStep = i;
            break;
        }

        if (graphCol == null || graphRb == null)
            return;

        // Не считать ударом движение, начинающееся уже над тем же графом (в т.ч. резкий выход — первое попадание вдоль sweep в точке prev).
        if (wasGraphRb != null && graphRb == wasGraphRb && firstGraphHitStep == 0)
            return;

        var planeZ = graphCol.bounds.center.z;
        var rayPrev = _cachedCamera.ScreenPointToRay(prevScreen);
        var rayCurr = _cachedCamera.ScreenPointToRay(currScreen);
        if (!TryRayIntersectWorldXYPlaneAtZ(rayPrev, planeZ, out var worldPrev) ||
            !TryRayIntersectWorldXYPlaneAtZ(rayCurr, planeZ, out var worldCurr))
            return;

        var dt = Time.unscaledDeltaTime;
        if (dt < 1e-6f)
            return;

        var delta = worldCurr - worldPrev;
        var vel = delta / dt;
        var speedXY = new Vector3(vel.x, vel.y, 0f).magnitude;

        if (speedXY < minCursorSpeedWorldXY)
            return;

        var speedHi = Mathf.Max(maxCursorSpeedWorldXY, minCursorSpeedWorldXY + 1e-4f);
        var tn = Mathf.InverseLerp(minCursorSpeedWorldXY, speedHi, Mathf.Clamp(speedXY, minCursorSpeedWorldXY, speedHi));
        var rawMag = tn * maxImpulse * impulseStrengthMultiplier;
        var impulseMag = Mathf.Min(rawMag, maxImpulse);
        if (impulseMag <= 0f)
            return;

        var dir = new Vector3(delta.x, delta.y, 0f);
        if (dir.sqrMagnitude < 1e-12f)
            return;
        dir.Normalize();

        var rbId = graphRb.GetInstanceID();
        var now = Time.unscaledTime;
        if (minSecondsBetweenImpulsesPerBody > 0f &&
            _lastImpulseTimeByRb.TryGetValue(rbId, out var lastT) &&
            now - lastT < minSecondsBetweenImpulsesPerBody)
            return;

        graphRb.AddForce(dir * impulseMag, ForceMode.Impulse);
        GraphImpulseApplied?.Invoke(impulseMag, dir, graphRb);
        _lastImpulseTimeByRb[rbId] = now;
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

    private static bool TryGetGraphRigidbody(Collider col, out Rigidbody rb)
    {
        rb = ResolveRigidbody(col);
        if (rb == null || rb.isKinematic)
            return false;
        if (col.GetComponentInParent<Node>() != null)
            return true;
        if (col.GetComponentInParent<VillageGraphEdgeEndColliderDriver>() != null)
            return true;
        return false;
    }

    private static Rigidbody ResolveRigidbody(Collider col)
    {
        var rb = col.attachedRigidbody;
        if (rb != null)
            return rb;
        return col.GetComponentInParent<Rigidbody>();
    }

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

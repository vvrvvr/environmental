using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(VillageGraphMapCursorSwipeImpulse))]
public sealed class VillageGraphMapCursorSwipeTrail : MonoBehaviour
{
    private const int MaxLineGradientKeys = 8;

    [Header("Refs")]
    [SerializeField]
    private LineRenderer lineRenderer;

    [Tooltip("Пусто — GameManager.MapCamera. Только для проекции точек трейла под курсор.")]
    [SerializeField]
    private Camera mapCameraOverride;

    [Header("Plane (визуал под мышью)")]
    [SerializeField]
    private float referencePlaneZ;

    [Header("Trail")]
    [Tooltip("Добавляется к порогу скорости импульса только для трейла: < 0 — раньше, > 0 — позже (юнит/с в плоскости XY).")]
    [SerializeField, Range(-3f, 3f)]
    private float trailSpeedThresholdOffset;

    [Tooltip("Ниже порога трейла на столько скорость должна упасть, чтобы выключить (гистерезис, меньше дёрганья на низах).")]
    [SerializeField, Min(0f)]
    private float trailSpeedDeactivateHysteresis = 0.28f;

    [Tooltip("После реального импульса по графу держать трейл ещё столько секунд (unscaled), если курсор всё ещё над графом.")]
    [SerializeField, Min(0f)]
    private float trailHoldAfterImpulseSeconds = 0.12f;

    [SerializeField, Min(2)]
    private int maxPoints = 96;

    [SerializeField, Min(0f)]
    private float minPointDistance = 0.015f;

    [SerializeField, Min(0f)]
    private float widthAtMinStrength = 0.02f;

    [SerializeField, Min(0f)]
    private float widthAtMaxStrength = 0.14f;

    [Tooltip("0 — у курсора (голова), 1 — конец хвоста. LineRenderer идёт от 0 к n−1.")]
    [SerializeField]
    private AnimationCurve tailWidthAlongLine = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(1f, 0.22f));

    [SerializeField, Min(0.01f)]
    private float fadeOutDuration = 0.14f;

    [SerializeField]
    private Gradient strengthGradient;

    [SerializeField]
    private Material lineMaterial;

    [SerializeField]
    private bool enableTrail = true;

    private VillageGraphMapCursorSwipeImpulse _impulse;
    private Camera _cam;
    private Vector3 _prevMouseScreen;
    private bool _hasPrev;
    private bool _trailSpeedGateOpen;
    private float _trailImpulseHoldUntilUnscaled;
    private readonly List<Vector3> _pts = new List<Vector3>(128);
    private readonly List<float> _str = new List<float>(128);
    private float _lastStrength01;
    private float _lineFade = 1f;

    private void Awake()
    {
        _impulse = GetComponent<VillageGraphMapCursorSwipeImpulse>();
        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
            lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.numCapVertices = 2;
        lineRenderer.numCornerVertices = 2;
        if (lineMaterial != null)
            lineRenderer.material = lineMaterial;
        lineRenderer.widthCurve = tailWidthAlongLine;
        if (strengthGradient == null || strengthGradient.colorKeys == null || strengthGradient.colorKeys.Length == 0)
        {
            strengthGradient = new Gradient();
            strengthGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 1f, 1f, 0.35f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        }
    }

    private void OnEnable()
    {
        VillageGraphMapCursorSwipeImpulse.GraphImpulseApplied += OnGraphSwipeImpulseApplied;
    }

    private void OnDisable()
    {
        VillageGraphMapCursorSwipeImpulse.GraphImpulseApplied -= OnGraphSwipeImpulseApplied;
    }

    private void OnGraphSwipeImpulseApplied(float _, Vector3 __, Rigidbody ___)
    {
        _trailImpulseHoldUntilUnscaled = Time.unscaledTime + trailHoldAfterImpulseSeconds;
    }

    private void Update()
    {
        if (!enableTrail || !Application.isPlaying)
            return;

        ResolveCam();
        if (_cam == null)
            return;

        var curr = Input.mousePosition;
        if (!_hasPrev)
        {
            _prevMouseScreen = curr;
            _hasPrev = true;
            ClearTrail();
            return;
        }

        var prev = _prevMouseScreen;
        _prevMouseScreen = curr;

        var onGraph = _impulse.TryGetTrailWorldPositions(prev, curr, out _, out _);

        var rayP = _cam.ScreenPointToRay(prev);
        var rayC = _cam.ScreenPointToRay(curr);
        var ok0 = TryPlaneHit(rayP, out var w0);
        var ok1 = TryPlaneHit(rayC, out var w1);
        var geomRenderOk = ok0 && ok1;
        var geomOk = onGraph && geomRenderOk;

        var dt = Time.unscaledDeltaTime;
        var speed = 0f;
        if (geomOk && dt >= 1e-6f)
        {
            var vel = (w1 - w0) / dt;
            speed = new Vector2(vel.x, vel.y).magnitude;
        }

        var vMinImpulse = _impulse.MinSwipeSpeedWorldXY;
        var vMinTrail = vMinImpulse + trailSpeedThresholdOffset;
        var vRelease = Mathf.Max(0f, vMinTrail - trailSpeedDeactivateHysteresis);

        if (!geomOk)
            _trailSpeedGateOpen = false;
        else if (dt >= 1e-6f)
        {
            if (speed >= vMinTrail)
                _trailSpeedGateOpen = true;
            else if (speed < vRelease)
                _trailSpeedGateOpen = false;
        }

        var holdImpulse = Time.unscaledTime < _trailImpulseHoldUntilUnscaled;
        var swipeStrong = geomOk && (_trailSpeedGateOpen || holdImpulse);

        if (swipeStrong)
        {
            _lineFade = 1f;
            var vMax = Mathf.Max(_impulse.MaxSwipeSpeedWorldXY, vMinTrail + 1e-4f);
            var t01 = Mathf.InverseLerp(vMinTrail, vMax, Mathf.Clamp(speed, vMinTrail, vMax));
            _lastStrength01 = t01;

            if (_pts.Count == 0 || Vector3.Distance(w1, _pts[0]) >= minPointDistance)
            {
                _pts.Insert(0, w1);
                _str.Insert(0, t01);
                while (_pts.Count > maxPoints)
                {
                    _pts.RemoveAt(_pts.Count - 1);
                    _str.RemoveAt(_str.Count - 1);
                }
            }
            else if (_pts.Count > 0)
            {
                _pts[0] = w1;
                _str[0] = t01;
            }
        }
        else
        {
            if (_pts.Count < 2)
            {
                if (!geomOk)
                    ClearTrail();
                return;
            }

            _lineFade -= Time.unscaledDeltaTime / Mathf.Max(1e-4f, fadeOutDuration);
            if (_lineFade <= 0f)
            {
                ClearTrail();
                return;
            }
        }

        if (_pts.Count >= 2)
            ApplyLineRenderer();
    }

    private void ApplyLineRenderer()
    {
        var n = _pts.Count;
        if (n < 2)
        {
            lineRenderer.positionCount = 0;
            lineRenderer.enabled = false;
            return;
        }

        lineRenderer.enabled = true;
        lineRenderer.positionCount = n;
        for (var i = 0; i < n; i++)
            lineRenderer.SetPosition(i, _pts[i]);

        var baseW = Mathf.Lerp(widthAtMinStrength, widthAtMaxStrength, _lastStrength01) * _lineFade;
        lineRenderer.widthCurve = tailWidthAlongLine;
        lineRenderer.widthMultiplier = baseW;

        lineRenderer.colorGradient = BuildGradientForLine(n);
    }

    private Gradient BuildGradientForLine(int n)
    {
        if (n < 2)
            return strengthGradient;
        var keyCount = Mathf.Min(n, MaxLineGradientKeys);
        var ck = new GradientColorKey[keyCount];
        var ak = new GradientAlphaKey[keyCount];
        for (var k = 0; k < keyCount; k++)
        {
            var u = keyCount <= 1 ? 0f : k / (float)(keyCount - 1);
            // u=0 у курсора (_pts[0]), u=1 у хвоста (_pts[n-1])
            var i = Mathf.Clamp(Mathf.RoundToInt(u * (n - 1)), 0, n - 1);
            var s = _str[i];
            var c = strengthGradient.Evaluate(Mathf.Lerp(u, s, 0.65f));
            c.a *= _lineFade;
            ck[k] = new GradientColorKey(c, u);
            ak[k] = new GradientAlphaKey(c.a, u);
        }

        var g = new Gradient();
        g.SetKeys(ck, ak);
        return g;
    }

    private void ClearTrail()
    {
        _pts.Clear();
        _str.Clear();
        _lineFade = 1f;
        _trailSpeedGateOpen = false;
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
            lineRenderer.enabled = false;
        }
    }

    private void ResolveCam()
    {
        if (_cam != null)
            return;
        _cam = mapCameraOverride != null ? mapCameraOverride : GameManager.Instance != null ? GameManager.Instance.MapCamera : null;
    }

    private bool TryPlaneHit(Ray ray, out Vector3 hit)
    {
        var dz = ray.direction.z;
        if (Mathf.Abs(dz) < 1e-7f)
        {
            hit = default;
            return false;
        }

        var t = (referencePlaneZ - ray.origin.z) / dz;
        if (t < 0f)
        {
            hit = default;
            return false;
        }

        hit = ray.origin + ray.direction * t;
        hit.z = referencePlaneZ;
        return true;
    }
}

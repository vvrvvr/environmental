using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(VillageGraphMapCursorSwipeImpulse))]
[DefaultExecutionOrder(50)]
public sealed class VillageGraphMapCursorSwipeTrail : MonoBehaviour
{
    private const int MaxLineGradientKeys = 8;

    [Header("Refs")]
    [SerializeField]
    private LineRenderer lineRenderer;

    [Tooltip("Пусто — GameManager.MapCamera. Проекция курсора на плоскость трейла.")]
    [SerializeField]
    private Camera mapCameraOverride;

    [Header("Plane")]
    [SerializeField]
    private float referencePlaneZ;

    [Header("Рисование (по событию импульса)")]
    [Tooltip("Сколько секунд (unscaled) после удара трейл тянется за курсором. Каждый новый импульс обновляет отсчёт.")]
    [SerializeField, Min(0.01f)]
    private float trailDrawDurationSeconds = 0.35f;

    [SerializeField, Min(2)]
    private int maxPoints = 96;

    [SerializeField, Min(0f)]
    private float minPointDistance = 0.015f;

    [Tooltip("Минимальная длина хвоста (world), если пока одна точка.")]
    [SerializeField, Min(0f)]
    private float trailMinStubWorldLength = 0.004f;

    [Header("Толщина")]
    [SerializeField, Min(0f)]
    private float widthMin = 0.02f;

    [SerializeField, Min(0f)]
    private float widthMax = 0.14f;

    [Tooltip("За столько секунд (unscaled) с начала текущей «жизни» трейла толщина дорастает до widthMax. 0 — сразу widthMax.")]
    [SerializeField, Min(0f)]
    private float secondsToReachMaxWidth = 0.5f;

    [Tooltip("0 — у курсора (голова), 1 — конец хвоста.")]
    [SerializeField]
    private AnimationCurve tailWidthAlongLine = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(1f, 0.22f));

    [Header("Схлопывание длины")]
    [SerializeField, Min(0.01f)]
    private float shrinkToZeroDuration = 0.12f;

    [SerializeField]
    private Gradient strengthGradient;

    [SerializeField]
    private Material lineMaterial;

    [SerializeField]
    private bool enableTrail = true;

    private Camera _cam;
    private readonly List<Vector3> _pts = new List<Vector3>(128);
    private float _drawUntilUnscaled;
    private float _aliveSinceUnscaled;
    private float _shrinkStartedUnscaled;
    private int _shrinkStartPointCount;
    private Vector3 _prevW1;
    private bool _hasPrevW1;

    private void Awake()
    {
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
        VillageGraphMapCursorSwipeImpulse.GraphImpulseApplied += OnGraphImpulseApplied;
    }

    private void OnDisable()
    {
        VillageGraphMapCursorSwipeImpulse.GraphImpulseApplied -= OnGraphImpulseApplied;
    }

    private void OnGraphImpulseApplied(float _, Vector3 __, Rigidbody ___)
    {
        var now = Time.unscaledTime;
        if (_pts.Count == 0)
            _aliveSinceUnscaled = now;
        _drawUntilUnscaled = now + trailDrawDurationSeconds;
        _shrinkStartedUnscaled = 0f;
        _shrinkStartPointCount = 0;
    }

    private void Update()
    {
        if (!enableTrail || !Application.isPlaying)
            return;

        ResolveCam();
        if (_cam == null)
            return;

        var ray = _cam.ScreenPointToRay(Input.mousePosition);
        var w1Ok = TryPlaneHit(ray, out var w1);

        var drawing = Time.unscaledTime < _drawUntilUnscaled;

        if (drawing && w1Ok)
        {
            if (_pts.Count == 0 || Vector3.Distance(w1, _pts[0]) >= minPointDistance)
            {
                _pts.Insert(0, w1);
                while (_pts.Count > maxPoints)
                    _pts.RemoveAt(_pts.Count - 1);
            }
            else if (_pts.Count > 0)
                _pts[0] = w1;

            EnsureMinimumTwoWorldPoints(w1, w1Ok);
            _prevW1 = w1;
            _hasPrevW1 = true;
        }
        else if (!drawing)
        {
            if (_pts.Count < 2)
            {
                ClearTrail();
                return;
            }

            if (_shrinkStartedUnscaled <= 0f)
            {
                _shrinkStartedUnscaled = Time.unscaledTime;
                _shrinkStartPointCount = _pts.Count;
            }

            var shrinkDuration = Mathf.Max(1e-4f, shrinkToZeroDuration);
            var shrinkT = Mathf.Clamp01((Time.unscaledTime - _shrinkStartedUnscaled) / shrinkDuration);
            var targetVisible = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Lerp(_shrinkStartPointCount, 0f, shrinkT)),
                0,
                _pts.Count);
            while (_pts.Count > targetVisible)
                _pts.RemoveAt(_pts.Count - 1);

            if (_pts.Count < 2)
            {
                ClearTrail();
                return;
            }
        }

        if (_pts.Count >= 2)
            ApplyLineRenderer();
    }

    private float WidthForCurrentAge()
    {
        if (secondsToReachMaxWidth <= 1e-5f)
            return widthMax;
        var age = Mathf.Max(0f, Time.unscaledTime - _aliveSinceUnscaled);
        var t = Mathf.Clamp01(age / secondsToReachMaxWidth);
        return Mathf.Lerp(widthMin, widthMax, t);
    }

    private void ApplyLineRenderer()
    {
        var n = _pts.Count;
        lineRenderer.enabled = true;
        lineRenderer.positionCount = n;
        for (var i = 0; i < n; i++)
            lineRenderer.SetPosition(i, _pts[i]);

        lineRenderer.widthCurve = tailWidthAlongLine;
        lineRenderer.widthMultiplier = WidthForCurrentAge();
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
            var c = strengthGradient.Evaluate(u);
            ck[k] = new GradientColorKey(c, u);
            ak[k] = new GradientAlphaKey(c.a, u);
        }

        var g = new Gradient();
        g.SetKeys(ck, ak);
        return g;
    }

    private void EnsureMinimumTwoWorldPoints(Vector3 w1, bool w1Ok)
    {
        if (_pts.Count != 1)
            return;
        var head = _pts[0];
        Vector3 tail;
        if (w1Ok && _hasPrevW1)
        {
            var d = head - _prevW1;
            var len = d.magnitude;
            if (len > 1e-6f)
                tail = head - d * (Mathf.Max(trailMinStubWorldLength, len * 0.35f) / len);
            else
                tail = head + new Vector3(trailMinStubWorldLength, 0f, 0f);
        }
        else
            tail = head + new Vector3(trailMinStubWorldLength, 0f, 0f);

        tail.z = referencePlaneZ;
        _pts.Add(tail);
    }

    private void ClearTrail()
    {
        _pts.Clear();
        _drawUntilUnscaled = 0f;
        _shrinkStartedUnscaled = 0f;
        _shrinkStartPointCount = 0;
        _hasPrevW1 = false;
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

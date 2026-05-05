using UnityEngine;

/// <summary>
/// "Pull away" tilt reaction for graph hit impulses.
/// Listens to <see cref="VillageGraphMapCursorSwipeImpulse.GraphImpulseApplied"/> and quickly tilts to a random direction,
/// then returns slower to the initial orientation captured at runtime start.
/// </summary>
[DisallowMultipleComponent]
public sealed class GraphImpulseJellyTilt : MonoBehaviour
{
    [Header("Tilt amount (degrees)")]
    [Tooltip("Kick angle when impact strength is ignored.")]
    [SerializeField, Min(0f)] private float fixedKickAngle = 10f;

    [Tooltip("Minimum kick angle when impact strength is used.")]
    [SerializeField, Min(0f)] private float minKickAngle = 4f;

    [Tooltip("Maximum kick angle when impact strength is used.")]
    [SerializeField, Min(0f)] private float maxKickAngle = 16f;

    [Header("Kick from impact strength")]
    [Tooltip("Use applied graph impulse magnitude to scale kick amount.")]
    [SerializeField] private bool useImpactStrength = true;

    [Tooltip("Impulse magnitude that maps to max kick angle.")]
    [SerializeField, Min(0.001f)] private float impulseForMaxKick = 4f;

    [Header("Motion")]
    [Tooltip("How fast object jerks away to kick target.")]
    [SerializeField, Min(0.01f)] private float kickOutSharpness = 30f;

    [Tooltip("How fast object relaxes back to start orientation.")]
    [SerializeField, Min(0.01f)] private float returnSharpness = 8f;

    [Tooltip("Clamp total tilt angle from rest orientation. 0 = no clamp.")]
    [SerializeField, Min(0f)] private float maxTotalTiltAngle = 25f;

    [Tooltip("Random side spread multiplier along local X axis.")]
    [SerializeField, Min(0f)] private float randomPitchScale = 1f;

    [Tooltip("Random side spread multiplier along local Y axis.")]
    [SerializeField, Min(0f)] private float randomYawScale = 1f;

    private Quaternion _restWorldRotation;
    private Vector2 _currentTiltDeg;
    private Vector2 _targetTiltDeg;

    private void Awake()
    {
        _restWorldRotation = transform.rotation;
    }

    private void OnEnable()
    {
        VillageGraphMapCursorSwipeImpulse.GraphImpulseApplied += OnGraphImpulseApplied;
    }

    private void OnDisable()
    {
        VillageGraphMapCursorSwipeImpulse.GraphImpulseApplied -= OnGraphImpulseApplied;
    }

    private void Update()
    {
        var dt = Time.deltaTime;
        if (dt <= 1e-6f)
            return;

        // Fast kick-out when target has non-zero tilt, slower settle when target goes back to zero.
        var sharp = _targetTiltDeg.sqrMagnitude > 1e-5f ? kickOutSharpness : returnSharpness;
        var t = 1f - Mathf.Exp(-Mathf.Max(0.01f, sharp) * dt);
        _currentTiltDeg = Vector2.Lerp(_currentTiltDeg, _targetTiltDeg, t);

        if (_targetTiltDeg.sqrMagnitude > 1e-5f && (_targetTiltDeg - _currentTiltDeg).sqrMagnitude < 1e-4f)
            _targetTiltDeg = Vector2.zero;

        var tiltLocal =
            Quaternion.AngleAxis(-_currentTiltDeg.x, Vector3.right) *
            Quaternion.AngleAxis(_currentTiltDeg.y, Vector3.up);

        if (maxTotalTiltAngle > 0f)
        {
            var mag = Quaternion.Angle(Quaternion.identity, tiltLocal);
            if (mag > maxTotalTiltAngle)
                tiltLocal = Quaternion.RotateTowards(Quaternion.identity, tiltLocal, maxTotalTiltAngle);
        }

        transform.rotation = _restWorldRotation * tiltLocal;
    }

    private void OnGraphImpulseApplied(float impulseMagnitude, Vector3 impulseDirectionWorldXY, Rigidbody targetBody)
    {
        var kickAngle = ResolveKickAngle(impulseMagnitude);
        if (kickAngle <= 0.001f)
            return;

        var random2 = Random.insideUnitCircle;
        if (random2.sqrMagnitude < 1e-6f)
            random2 = Vector2.up;
        random2.Normalize();

        // "Pull away" feel: random reaction opposite to touch, with random side jitter.
        var away = -new Vector2(impulseDirectionWorldXY.x, impulseDirectionWorldXY.y);
        if (away.sqrMagnitude < 1e-6f)
            away = random2;
        else
            away.Normalize();

        var mixed = (away + random2 * 0.55f).normalized;
        var pitch = mixed.y * kickAngle * randomPitchScale;
        var yaw = mixed.x * kickAngle * randomYawScale;
        _targetTiltDeg = new Vector2(pitch, yaw);
    }

    private float ResolveKickAngle(float impulseMagnitude)
    {
        if (!useImpactStrength)
            return fixedKickAngle;

        var hi = Mathf.Max(impulseForMaxKick, 1e-4f);
        var s = Mathf.Clamp01(impulseMagnitude / hi);
        return Mathf.Lerp(minKickAngle, maxKickAngle, s);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (maxKickAngle < minKickAngle)
            maxKickAngle = minKickAngle;
    }
#endif
}

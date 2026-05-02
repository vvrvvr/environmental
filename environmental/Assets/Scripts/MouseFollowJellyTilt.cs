using UnityEngine;

/// <summary>
/// Лёгкий наклон объекта (оси как у камеры) в сторону курсора: ограничение по углам и сглаживание «как желе».
/// Камера смотрит вдоль своего forward; смещение мыши от центра экрана задаёт целевой наклон вокруг right/up камеры.
/// </summary>
[DisallowMultipleComponent]
public class MouseFollowJellyTilt : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Камера, с осями которой совпадает объект в покое.")]
    [SerializeField] private Camera targetCamera;

    [Header("Сила наклона (градусы в крайних точках экрана)")]
    [Tooltip("Максимальный поворот вокруг оси камеры Up (мышь влево/вправо).")]
    [SerializeField] private float maxYawDegrees = 12f;

    [Tooltip("Максимальный поворот вокруг оси камеры Right (мышь вверх/вниз).")]
    [SerializeField] private float maxPitchDegrees = 10f;

    [Tooltip("Множитель смещения от центра экрана (1 = полный наклон у края viewport).")]
    [SerializeField, Min(0f)] private float tiltInputScale = 1f;

    [Header("Ограничение")]
    [Tooltip("Максимальный угол между ориентацией «как у камеры» и текущим наклоном. 0 — без этого ограничения.")]
    [SerializeField, Min(0f)] private float maxTiltAngleDegrees = 18f;

    [Header("Желе")]
    [Tooltip("Время сглаживания целевого смещения курсора (SmoothDamp). Больше — сильнее запаздывание «желе».")]
    [SerializeField, Min(0.001f)] private float pointerSmoothTime = 0.12f;

    [Tooltip("Скорость догона фактического поворота до целевого (чем меньше, тем мягче второй слой сглаживания).")]
    [SerializeField, Min(0.01f)] private float rotationFollowSharpness = 14f;

    [Header("Поведение")]
    [Tooltip("Инвертировать вертикаль (мышь вверх наклоняет «подбородок» вниз).")]
    [SerializeField] private bool invertPitch;

    [Tooltip("Если включено — при скрытом курсоре (lock) наклон не считается.")]
    [SerializeField] private bool ignoreWhenCursorLocked = true;

    private Vector2 _smoothedOffset;
    private Vector2 _offsetVelocity;

    private void Reset()
    {
        targetCamera = GetComponentInParent<Camera>();
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (targetCamera == null)
            return;

        if (ignoreWhenCursorLocked && Cursor.lockState == CursorLockMode.Locked)
            return;

        Vector2 viewport = targetCamera.ScreenToViewportPoint(Input.mousePosition);
        Vector2 rawOffset = (viewport - new Vector2(0.5f, 0.5f)) * 2f * tiltInputScale;

        float dt = Time.deltaTime;
        _smoothedOffset = Vector2.SmoothDamp(
            _smoothedOffset,
            rawOffset,
            ref _offsetVelocity,
            pointerSmoothTime,
            Mathf.Infinity,
            dt);

        float pitchSign = invertPitch ? 1f : -1f;
        Quaternion tiltLocal = Quaternion.AngleAxis(-pitchSign * _smoothedOffset.y * maxPitchDegrees, Vector3.right)
            * Quaternion.AngleAxis(-_smoothedOffset.x * maxYawDegrees, Vector3.up);

        if (maxTiltAngleDegrees > 0f)
        {
            float tiltMag = Quaternion.Angle(Quaternion.identity, tiltLocal);
            if (tiltMag > maxTiltAngleDegrees)
                tiltLocal = Quaternion.RotateTowards(Quaternion.identity, tiltLocal, maxTiltAngleDegrees);
        }

        Quaternion baseWorld = targetCamera.transform.rotation;
        Quaternion targetWorld = baseWorld * tiltLocal;

        float t = 1f - Mathf.Exp(-rotationFollowSharpness * dt);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetWorld, t);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxYawDegrees = Mathf.Max(0f, maxYawDegrees);
        maxPitchDegrees = Mathf.Max(0f, maxPitchDegrees);
    }
#endif
}

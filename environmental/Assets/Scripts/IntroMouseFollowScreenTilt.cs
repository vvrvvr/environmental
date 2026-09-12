using UnityEngine;

/// <summary>
/// С <see cref="intro sequence"/> слегка наклоняет экран (<see cref="screenTarget"/>) в сторону курсора
/// в <b>локальных</b> осях экрана относительно исходного <see cref="Transform.localRotation"/> при включении.
/// </summary>
[DisallowMultipleComponent]
public sealed class IntroMouseFollowScreenTilt : MonoBehaviour
{
    public enum MouseReferenceMode
    {
        [Tooltip("Центр окна игры: Screen.width / Screen.height.")]
        ScreenCenter = 0,

        [Tooltip("Центр viewport камеры (учитывает letterbox и render texture).")]
        CameraViewport = 1,
    }

    [Header("Ссылки")]
    [Tooltip("Объект экрана, который наклоняется (например Main Screen).")]
    [SerializeField]
    private Transform screenTarget;

    [Tooltip("Только для Mouse Reference = Camera Viewport. Пусто — Camera.main.")]
    [SerializeField]
    private Camera referenceCamera;

    [Header("Сила наклона (градусы у края экрана)")]
    [Tooltip("Наклон в сторону мыши влево/вправо — локальная ось Forward (Z).")]
    [SerializeField]
    private float maxYawDegrees = 8f;

    [Tooltip("Наклон в сторону мыши вверх/вниз — локальная ось Right (X).")]
    [SerializeField]
    private float maxPitchDegrees = 6f;

    [Tooltip("Множитель смещения от центра (1 = полный наклон у края).")]
    [SerializeField]
    [Min(0f)]
    private float tiltInputScale = 1f;

    [Header("Ограничение")]
    [Tooltip("Максимальный суммарный угол наклона от исходного localRotation. 0 — без ограничения.")]
    [SerializeField]
    [Min(0f)]
    private float maxTotalTiltDegrees = 12f;

    [Header("Сглаживание")]
    [Tooltip("SmoothDamp смещения курсора от центра. Больше — плавнее и с большим запаздыванием.")]
    [SerializeField]
    [Min(0.001f)]
    private float pointerSmoothTime = 0.14f;

    [Tooltip("Скорость догона поворота экрана до цели (exp decay). Меньше — мягче.")]
    [SerializeField]
    [Min(0.01f)]
    private float rotationFollowSharpness = 12f;

    [Header("Поведение")]
    [SerializeField]
    private MouseReferenceMode mouseReference = MouseReferenceMode.ScreenCenter;

    [Tooltip("Инвертировать вертикаль (мышь вверх — наклон «подбородком» вниз).")]
    [SerializeField]
    private bool invertPitch;

    [Tooltip("Не считать наклон, пока курсор заблокирован (CursorLockMode.Locked).")]
    [SerializeField]
    private bool ignoreWhenCursorLocked = true;

    [Tooltip("Использовать unscaled delta time (пауза игры не замораживает наклон).")]
    [SerializeField]
    private bool useUnscaledTime = true;

    private Quaternion _restLocalRotation;
    private Vector2 _smoothedOffset;
    private Vector2 _offsetVelocity;

    private void OnEnable()
    {
        CaptureRestLocalRotation();
    }

    /// <summary>Запомнить текущий localRotation экрана как «ровное» положение без наклона от мыши.</summary>
    public void CaptureRestLocalRotation()
    {
        if (screenTarget != null)
            _restLocalRotation = screenTarget.localRotation;
    }

    private void LateUpdate()
    {
        if (screenTarget == null)
            return;

        if (ignoreWhenCursorLocked && Cursor.lockState == CursorLockMode.Locked)
            return;

        var rawOffset = ReadMouseOffsetFromCenter();
        if (mouseReference == MouseReferenceMode.CameraViewport && referenceCamera == null)
            return;

        var dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 1e-6f)
            return;

        _smoothedOffset = Vector2.SmoothDamp(
            _smoothedOffset,
            rawOffset,
            ref _offsetVelocity,
            pointerSmoothTime,
            Mathf.Infinity,
            dt);

        // X мыши — влево/вправо (Forward / Z), Y — вверх/вниз (Right / X).
        var pitchSign = invertPitch ? 1f : -1f;
        var tiltOffset =
            Quaternion.AngleAxis(-_smoothedOffset.x * maxYawDegrees, Vector3.forward) *
            Quaternion.AngleAxis(-pitchSign * _smoothedOffset.y * maxPitchDegrees, Vector3.right);

        if (maxTotalTiltDegrees > 0f)
        {
            var tiltMag = Quaternion.Angle(Quaternion.identity, tiltOffset);
            if (tiltMag > maxTotalTiltDegrees)
                tiltOffset = Quaternion.RotateTowards(Quaternion.identity, tiltOffset, maxTotalTiltDegrees);
        }

        var targetLocal = _restLocalRotation * tiltOffset;

        var t = 1f - Mathf.Exp(-rotationFollowSharpness * dt);
        screenTarget.localRotation = Quaternion.Slerp(screenTarget.localRotation, targetLocal, t);
    }

    private Vector2 ReadMouseOffsetFromCenter()
    {
        Vector2 normalizedFromCenter;
        if (mouseReference == MouseReferenceMode.CameraViewport)
        {
            if (referenceCamera == null)
                referenceCamera = Camera.main;
            if (referenceCamera == null)
                return Vector2.zero;

            var viewport = referenceCamera.ScreenToViewportPoint(Input.mousePosition);
            normalizedFromCenter = (new Vector2(viewport.x, viewport.y) - new Vector2(0.5f, 0.5f)) * 2f;
        }
        else
        {
            var w = Mathf.Max(1, Screen.width);
            var h = Mathf.Max(1, Screen.height);
            normalizedFromCenter = new Vector2(
                Input.mousePosition.x / w - 0.5f,
                Input.mousePosition.y / h - 0.5f) * 2f;
        }

        return normalizedFromCenter * tiltInputScale;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxYawDegrees = Mathf.Max(0f, maxYawDegrees);
        maxPitchDegrees = Mathf.Max(0f, maxPitchDegrees);
    }
#endif
}

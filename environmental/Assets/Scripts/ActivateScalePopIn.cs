using DG.Tweening;
using UnityEngine;

/// <summary>
/// При включении объекта: <see cref="Transform.localScale"/> → 0, затем после случайной паузы [0 … <see cref="maxRandomDelaySeconds"/>]
/// плавно возвращается к исходному scale (запомненному в <see cref="Awake"/>).
/// </summary>
[DisallowMultipleComponent]
public sealed class ActivateScalePopIn : MonoBehaviour
{
    [Tooltip("Чей scale анимируется; пусто — этот объект.")]
    [SerializeField]
    private Transform target;

    [Tooltip("Верхняя граница случайной задержки перед появлением (сек, unscaled).")]
    [SerializeField]
    [Min(0f)]
    private float maxRandomDelaySeconds = 0.35f;

    [Tooltip("Длительность роста scale до исходного (сек, unscaled). 0 — мгновенно после задержки.")]
    [SerializeField]
    [Min(0f)]
    private float scaleInDurationSeconds = 0.4f;

    [SerializeField]
    private Ease scaleInEase = Ease.OutBack;

    [Tooltip("Unscaled — не зависит от Time.timeScale.")]
    [SerializeField]
    private bool unscaledTime = true;

    private Vector3 _restLocalScale;
    private bool _hasRestLocalScale;

    private Transform Target => target != null ? target : transform;

    private void Awake()
    {
        CacheRestLocalScale();
    }

    private void OnEnable()
    {
        var tr = Target;
        if (tr == null)
            return;

        if (!_hasRestLocalScale)
            CacheRestLocalScale();

        DOTween.Kill(this, complete: false);

        tr.localScale = Vector3.zero;

        var delay = maxRandomDelaySeconds > 0f ? Random.Range(0f, maxRandomDelaySeconds) : 0f;
        var duration = Mathf.Max(0f, scaleInDurationSeconds);

        if (delay <= 0f && duration <= 0f)
        {
            tr.localScale = _restLocalScale;
            return;
        }

        var seq = DOTween.Sequence()
            .SetId(this)
            .SetLink(gameObject)
            .SetUpdate(unscaledTime);

        if (delay > 0f)
            seq.AppendInterval(delay);

        if (duration > 0f)
            seq.Append(tr.DOScale(_restLocalScale, duration).SetEase(scaleInEase));
        else
            seq.AppendCallback(() => tr.localScale = _restLocalScale);
    }

    private void OnDisable()
    {
        DOTween.Kill(this, complete: false);
    }

    private void CacheRestLocalScale()
    {
        var tr = Target;
        if (tr == null)
        {
            _hasRestLocalScale = false;
            return;
        }

        _restLocalScale = tr.localScale;
        _hasRestLocalScale = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (target == null)
            target = transform;
    }
#endif
}

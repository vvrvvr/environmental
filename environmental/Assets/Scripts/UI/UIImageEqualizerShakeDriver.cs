using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Драйвер для материала <c>UI/EqualizerShake</c> на <see cref="Image"/> / <see cref="RawImage"/>.
/// Пока пользователь нажимает/тянет <see cref="shakeSlider"/>, тряска включена; сила — от значения слайдера (0 = мин., 1 = макс.).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public sealed class UIImageEqualizerShakeDriver : MonoBehaviour
{
    private static readonly int ShakeBlendId = Shader.PropertyToID("_ShakeBlend");
    private static readonly int ShakeIntensityId = Shader.PropertyToID("_ShakeIntensity");
    private static readonly int ShakeMaxOffsetId = Shader.PropertyToID("_ShakeMaxOffset");

    [SerializeField]
    private Graphic targetGraphic;

    [Tooltip("Слайдер: при нажатии на ползунок/дорожку тряска включается, сила = значению 0…1.")]
    [SerializeField]
    private Slider shakeSlider;

    [Tooltip("Сила тряски при значении слайдера 0 (нормализовано 0…1 для шейдера).")]
    [SerializeField]
    [Range(0f, 1f)]
    private float minShakeIntensity = 0.08f;

    [Tooltip("Сила тряски при значении слайдера 1.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float maxShakeIntensity = 1f;

    [Tooltip("Максимальное смещение вершин по Y при полной силе.")]
    [SerializeField]
    [Min(0f)]
    private float shakeMaxOffset = 8f;

    [Tooltip("Фейд входа/выхода тряски (сек, unscaled).")]
    [SerializeField]
    [Min(0f)]
    private float shakeFadeSeconds = 0.2f;

    private Material _materialInstance;
    private Tweener _blendTween;
    private bool _sliderInteracting;
    private EventTrigger _sliderEventTrigger;
    private readonly List<EventTrigger.Entry> _sliderTriggerEntries = new();

    private void Reset()
    {
        targetGraphic = GetComponent<Graphic>();
    }

    private void Awake()
    {
        if (targetGraphic == null)
            targetGraphic = GetComponent<Graphic>();

        CacheMaterialInstance();
        ApplyShakeMaxOffset();
        ApplyShakeBlendImmediate(0f);
        BindSlider();
    }

    private void OnDestroy()
    {
        _blendTween?.Kill();
        UnbindSlider();
    }

    private void OnValidate()
    {
        if (targetGraphic == null)
            targetGraphic = GetComponent<Graphic>();

        if (!Application.isPlaying)
            return;

        CacheMaterialInstance();
        ApplyShakeMaxOffset();
        if (_sliderInteracting)
            ApplyIntensityFromSlider();
    }

    public void SetShakeMaxOffset(float maxOffset)
    {
        shakeMaxOffset = Mathf.Max(0f, maxOffset);
        ApplyShakeMaxOffset();
    }

    private void BindSlider()
    {
        UnbindSlider();
        if (shakeSlider == null)
            return;

        shakeSlider.onValueChanged.AddListener(OnSliderValueChanged);

        _sliderEventTrigger = shakeSlider.GetComponent<EventTrigger>();
        if (_sliderEventTrigger == null)
            _sliderEventTrigger = shakeSlider.gameObject.AddComponent<EventTrigger>();

        AddTriggerEntry(EventTriggerType.PointerDown, OnSliderInteractionBegan);
        AddTriggerEntry(EventTriggerType.PointerUp, OnSliderInteractionEnded);
        AddTriggerEntry(EventTriggerType.EndDrag, OnSliderInteractionEnded);
    }

    private void UnbindSlider()
    {
        if (shakeSlider != null)
            shakeSlider.onValueChanged.RemoveListener(OnSliderValueChanged);

        if (_sliderEventTrigger != null)
        {
            foreach (var entry in _sliderTriggerEntries)
                _sliderEventTrigger.triggers.Remove(entry);
            _sliderTriggerEntries.Clear();
            _sliderEventTrigger = null;
        }
    }

    private void AddTriggerEntry(EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> callback)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(callback);
        _sliderEventTrigger.triggers.Add(entry);
        _sliderTriggerEntries.Add(entry);
    }

    private void OnSliderInteractionBegan(BaseEventData _)
    {
        _sliderInteracting = true;
        CacheMaterialInstance();
        ApplyIntensityFromSlider();
        AnimateShakeBlend(1f);
    }

    private void OnSliderInteractionEnded(BaseEventData _)
    {
        if (!_sliderInteracting)
            return;

        _sliderInteracting = false;
        AnimateShakeBlend(0f);
    }

    private void OnSliderValueChanged(float _)
    {
        if (!_sliderInteracting)
            return;

        ApplyIntensityFromSlider();
    }

    private void ApplyIntensityFromSlider()
    {
        if (_materialInstance == null || shakeSlider == null)
            return;

        var t = Mathf.Clamp01(shakeSlider.value);
        var intensity = Mathf.Lerp(minShakeIntensity, maxShakeIntensity, t);
        _materialInstance.SetFloat(ShakeIntensityId, intensity);
    }

    private void ApplyShakeMaxOffset()
    {
        if (_materialInstance == null)
            return;
        _materialInstance.SetFloat(ShakeMaxOffsetId, shakeMaxOffset);
    }

    private void CacheMaterialInstance()
    {
        if (targetGraphic == null)
            return;

        _materialInstance = targetGraphic.material;
        if (_materialInstance != null && _materialInstance.shader != null &&
            !_materialInstance.shader.name.Contains("EqualizerShake"))
        {
            Debug.LogWarning(
                $"[{nameof(UIImageEqualizerShakeDriver)}] Материал на «{name}» не использует UI/EqualizerShake.",
                this);
        }
    }

    private void ApplyShakeBlendImmediate(float blend)
    {
        if (_materialInstance == null)
            return;
        _materialInstance.SetFloat(ShakeBlendId, Mathf.Clamp01(blend));
    }

    private void AnimateShakeBlend(float target)
    {
        _blendTween?.Kill();
        if (_materialInstance == null)
            return;

        target = Mathf.Clamp01(target);
        var fade = Mathf.Max(0f, shakeFadeSeconds);
        if (fade <= 0f)
        {
            ApplyShakeBlendImmediate(target);
            return;
        }

        var current = _materialInstance.GetFloat(ShakeBlendId);
        _blendTween = DOTween.To(
                () => current,
                v =>
                {
                    current = v;
                    ApplyShakeBlendImmediate(v);
                },
                target,
                fade)
            .SetUpdate(isIndependentUpdate: true)
            .SetLink(gameObject);
    }
}

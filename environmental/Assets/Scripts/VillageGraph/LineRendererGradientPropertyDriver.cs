using UnityEngine;

/// <summary>
/// Задаёт свойства градиентного шейдера на <see cref="LineRenderer"/> через <see cref="MaterialPropertyBlock"/>,
/// чтобы один общий материал не менялся глобально — значения только у этого объекта.
/// Ожидаются имена из <c>Custom/URP/UnlitGradientTwoSliders</c>: _ColorA…_SliderAC; виньетка по материалу: _EdgeVignetteStrength, _EdgeVignetteSoftness, _EndsVignetteStrength, _EndsVignetteSoftness.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public class LineRendererGradientPropertyDriver : MonoBehaviour
{
    private static readonly int IdColorA = Shader.PropertyToID("_ColorA");
    private static readonly int IdColorB = Shader.PropertyToID("_ColorB");
    private static readonly int IdColorC = Shader.PropertyToID("_ColorC");
    private static readonly int IdBlendAB = Shader.PropertyToID("_BlendAB");
    private static readonly int IdBlendAC = Shader.PropertyToID("_BlendAC");
    private static readonly int IdSliderAB = Shader.PropertyToID("_SliderAB");
    private static readonly int IdSliderAC = Shader.PropertyToID("_SliderAC");

    [Header("Colors (shader)")]
    [SerializeField] private Color colorA = Color.white;
    [SerializeField] private Color colorB = Color.red;
    [SerializeField] private Color colorC = Color.blue;

    [Header("Blend widths")]
    [SerializeField, Range(0.001f, 1f)] private float blendAB = 0.1f;
    [SerializeField, Range(0.001f, 1f)] private float blendAC = 0.1f;

    [Header("Sliders (0–100, shader)")]
    [SerializeField, Range(0f, 100f)] private float sliderAB;
    [SerializeField, Range(0f, 100f)] private float sliderAC;

    private LineRenderer _lineRenderer;
    private MaterialPropertyBlock _propertyBlock;

    private void Reset()
    {
        _lineRenderer = GetComponent<LineRenderer>();
    }

    private void OnEnable()
    {
        CacheLineRenderer();
        PushPropertyBlock();
    }

    private void OnValidate()
    {
        CacheLineRenderer();
        PushPropertyBlock();
    }

    private void OnDisable()
    {
        if (_lineRenderer != null)
            _lineRenderer.SetPropertyBlock(null);
    }

    private void CacheLineRenderer()
    {
        if (_lineRenderer == null)
            _lineRenderer = GetComponent<LineRenderer>();
    }

    /// <summary>Задать три цвета градиента и обновить MPB (например, с <see cref="MinimapGraphVisualPalette"/>).</summary>
    public void SetColorsABC(Color a, Color b, Color c)
    {
        colorA = a;
        colorB = b;
        colorC = c;
        PushPropertyBlock();
    }

    /// <summary>Текущее значение слайдера A–B в шейдере (0–100).</summary>
    public float GetSliderAB() => sliderAB;

    /// <summary>Задать <see cref="IdSliderAB"/> (0–100) и обновить MPB.</summary>
    public void SetSliderAB(float value)
    {
        sliderAB = Mathf.Clamp(value, 0f, 100f);
        PushPropertyBlock();
    }

    /// <summary>Текущее значение слайдера A–C в шейдере (0–100).</summary>
    public float GetSliderAC() => sliderAC;

    /// <summary>Задать <see cref="IdSliderAC"/> (0–100) и обновить MPB.</summary>
    public void SetSliderAC(float value)
    {
        sliderAC = Mathf.Clamp(value, 0f, 100f);
        PushPropertyBlock();
    }

    /// <summary>Применить текущие поля к MPB (можно вызвать из кода после смены значений).</summary>
    public void PushPropertyBlock()
    {
        if (_lineRenderer == null)
            _lineRenderer = GetComponent<LineRenderer>();
        if (_lineRenderer == null)
            return;

        _propertyBlock ??= new MaterialPropertyBlock();
        _propertyBlock.SetColor(IdColorA, colorA);
        _propertyBlock.SetColor(IdColorB, colorB);
        _propertyBlock.SetColor(IdColorC, colorC);
        _propertyBlock.SetFloat(IdBlendAB, blendAB);
        _propertyBlock.SetFloat(IdBlendAC, blendAC);
        _propertyBlock.SetFloat(IdSliderAB, sliderAB);
        _propertyBlock.SetFloat(IdSliderAC, sliderAC);
        _lineRenderer.SetPropertyBlock(_propertyBlock);
    }

    /// <summary>Снять MPB (снова будут видны дефолты из общего материала).</summary>
    public void ClearPropertyBlock()
    {
        if (_lineRenderer == null)
            _lineRenderer = GetComponent<LineRenderer>();
        if (_lineRenderer != null)
            _lineRenderer.SetPropertyBlock(null);
    }
}

using UnityEngine;

/// <summary>
/// Задаёт свойства градиентного шейдера на указанном <see cref="SpriteRenderer"/> через <see cref="MaterialPropertyBlock"/>,
/// чтобы один общий материал не менялся глобально — значения только у этого рендерера.
/// Ожидаются те же имена, что у <c>Custom/URP/UnlitGradientRadialTwoSliders</c> (и линейного варианта): _ColorA, _ColorB, _ColorC, _BlendAB, _BlendAC, _SliderAB, _SliderAC.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class SpriteRendererGradientPropertyDriver : MonoBehaviour
{
    private static readonly int IdColorA = Shader.PropertyToID("_ColorA");
    private static readonly int IdColorB = Shader.PropertyToID("_ColorB");
    private static readonly int IdColorC = Shader.PropertyToID("_ColorC");
    private static readonly int IdBlendAB = Shader.PropertyToID("_BlendAB");
    private static readonly int IdBlendAC = Shader.PropertyToID("_BlendAC");
    private static readonly int IdSliderAB = Shader.PropertyToID("_SliderAB");
    private static readonly int IdSliderAC = Shader.PropertyToID("_SliderAC");

    [Header("Target")]
    [Tooltip("Спрайт-рендерер с материалом градиентного шейдера (может быть на другом объекте).")]
    public SpriteRenderer targetSpriteRenderer;

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

    private MaterialPropertyBlock _propertyBlock;

    private void OnEnable()
    {
        PushPropertyBlock();
    }

    private void OnValidate()
    {
        PushPropertyBlock();
    }

    private void OnDisable()
    {
        if (targetSpriteRenderer != null)
            targetSpriteRenderer.SetPropertyBlock(null);
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
        if (targetSpriteRenderer == null)
            return;

        _propertyBlock ??= new MaterialPropertyBlock();
        _propertyBlock.SetColor(IdColorA, colorA);
        _propertyBlock.SetColor(IdColorB, colorB);
        _propertyBlock.SetColor(IdColorC, colorC);
        _propertyBlock.SetFloat(IdBlendAB, blendAB);
        _propertyBlock.SetFloat(IdBlendAC, blendAC);
        _propertyBlock.SetFloat(IdSliderAB, sliderAB);
        _propertyBlock.SetFloat(IdSliderAC, sliderAC);
        targetSpriteRenderer.SetPropertyBlock(_propertyBlock);
    }

    /// <summary>Снять MPB (снова будут видны дефолты из общего материала).</summary>
    public void ClearPropertyBlock()
    {
        if (targetSpriteRenderer != null)
            targetSpriteRenderer.SetPropertyBlock(null);
    }
}

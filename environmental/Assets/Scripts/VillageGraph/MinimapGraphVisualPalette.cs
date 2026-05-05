using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Общая палитра визуала графа карты: единые цвета градиента A/B/C для рёбер и нод.
/// На сцене цвета задаются через <see cref="LineRendererGradientPropertyDriver"/> и <see cref="SpriteRendererGradientPropertyDriver"/>;
/// кнопки в инспекторе ассета копируют A/B/C в драйверы всех рёбер/нод, ссылающихся на эту палитру.
/// </summary>
[CreateAssetMenu(fileName = "MinimapGraphVisualPalette", menuName = "Environmental/Minimap Graph Visual Palette", order = 10)]
public class MinimapGraphVisualPalette : ScriptableObject
{
    [Header("Градиент (Color A / B / C для шейдера)")]
    [FormerlySerializedAs("edgeGradientColorA")]
    [FormerlySerializedAs("nodeGradientColorA")]
    [SerializeField] private Color gradientColorA = Color.white;

    [FormerlySerializedAs("edgeGradientColorB")]
    [FormerlySerializedAs("nodeGradientColorB")]
    [SerializeField] private Color gradientColorB = new Color(1f, 0.25f, 0.25f, 1f);

    [FormerlySerializedAs("edgeGradientColorC")]
    [FormerlySerializedAs("nodeGradientColorC")]
    [SerializeField] private Color gradientColorC = new Color(0.25f, 0.45f, 1f, 1f);

    public Color GradientColorA => gradientColorA;
    public Color GradientColorB => gradientColorB;
    public Color GradientColorC => gradientColorC;

    public void GetGradientColors(out Color colorA, out Color colorB, out Color colorC)
    {
        colorA = gradientColorA;
        colorB = gradientColorB;
        colorC = gradientColorC;
    }

    // Совместимость со старым API: теперь ноды и рёбра читают один и тот же набор A/B/C.
    public Color EdgeGradientColorA => gradientColorA;
    public Color EdgeGradientColorB => gradientColorB;
    public Color EdgeGradientColorC => gradientColorC;
    public Color NodeGradientColorA => gradientColorA;
    public Color NodeGradientColorB => gradientColorB;
    public Color NodeGradientColorC => gradientColorC;

    public void GetEdgeGradientColors(out Color colorA, out Color colorB, out Color colorC) =>
        GetGradientColors(out colorA, out colorB, out colorC);

    public void GetNodeGradientColors(out Color colorA, out Color colorB, out Color colorC) =>
        GetGradientColors(out colorA, out colorB, out colorC);

    private void Reset()
    {
        gradientColorA = Color.white;
        gradientColorB = new Color(1f, 0.25f, 0.25f, 1f);
        gradientColorC = new Color(0.25f, 0.45f, 1f, 1f);
    }
}

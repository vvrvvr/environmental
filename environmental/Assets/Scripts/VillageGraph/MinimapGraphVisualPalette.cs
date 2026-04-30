using UnityEngine;

/// <summary>
/// Общая палитра визуала графа карты: общие цвета градиента рёбер (A/B/C) и нод (A/B/C).
/// На сцене цвета задаются через <see cref="LineRendererGradientPropertyDriver"/> и <see cref="SpriteRendererGradientPropertyDriver"/>;
/// кнопки в инспекторе ассета копируют A/B/C в драйверы всех рёбер/нод, ссылающихся на эту палитру.
/// </summary>
[CreateAssetMenu(fileName = "MinimapGraphVisualPalette", menuName = "Environmental/Minimap Graph Visual Palette", order = 10)]
public class MinimapGraphVisualPalette : ScriptableObject
{
    [Header("Рёбра — градиент (Color A / B / C для шейдера)")]
    [SerializeField] private Color edgeGradientColorA = Color.white;
    [SerializeField] private Color edgeGradientColorB = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] private Color edgeGradientColorC = new Color(0.25f, 0.45f, 1f, 1f);

    public Color EdgeGradientColorA => edgeGradientColorA;
    public Color EdgeGradientColorB => edgeGradientColorB;
    public Color EdgeGradientColorC => edgeGradientColorC;

    public void GetEdgeGradientColors(out Color colorA, out Color colorB, out Color colorC)
    {
        colorA = edgeGradientColorA;
        colorB = edgeGradientColorB;
        colorC = edgeGradientColorC;
    }

    [Header("Ноды — градиент (Color A / B / C для шейдера)")]
    [SerializeField] private Color nodeGradientColorA = Color.white;
    [SerializeField] private Color nodeGradientColorB = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] private Color nodeGradientColorC = new Color(0.25f, 0.45f, 1f, 1f);

    public Color NodeGradientColorA => nodeGradientColorA;
    public Color NodeGradientColorB => nodeGradientColorB;
    public Color NodeGradientColorC => nodeGradientColorC;

    public void GetNodeGradientColors(out Color colorA, out Color colorB, out Color colorC)
    {
        colorA = nodeGradientColorA;
        colorB = nodeGradientColorB;
        colorC = nodeGradientColorC;
    }

    private void Reset()
    {
        edgeGradientColorA = Color.white;
        edgeGradientColorB = new Color(1f, 0.25f, 0.25f, 1f);
        edgeGradientColorC = new Color(0.25f, 0.45f, 1f, 1f);

        nodeGradientColorA = Color.white;
        nodeGradientColorB = new Color(1f, 0.25f, 0.25f, 1f);
        nodeGradientColorC = new Color(0.25f, 0.45f, 1f, 1f);
    }
}

using UnityEngine;

/// <summary>
/// Общая палитра визуала графа карты: общие цвета градиента рёбер (A/B/C) и ноды (mainSprite / selection ring).
/// Цвета рёбер на сцене задаются через <see cref="LineRendererGradientPropertyDriver"/>; кнопка в инспекторе палитры
/// копирует A/B/C из ассета в драйверы всех рёбер, ссылающихся на эту палитру.
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

    [Header("Ноды — цвет mainSprite по NodeMapState")]
    [SerializeField] private Color nodeMainSpriteInactive = new Color(0.5f, 0.5f, 0.5f, 0.6f);
    [SerializeField] private Color nodeMainSpriteAppearing = Color.white;
    [SerializeField] private Color nodeMainSpriteVisible = Color.white;
    [SerializeField] private Color nodeMainSpriteSelected = Color.white;
    [SerializeField] private Color nodeMainSpriteDeselected = Color.white;
    [SerializeField] private Color nodeMainSpriteBlocked = new Color(0.75f, 0.55f, 0.55f, 1f);

    [Header("Ноды — selection ring (один цвет)")]
    [SerializeField] private Color nodeSelectionRingColor = new Color(0.35f, 0.85f, 1f, 1f);

    /// <summary>Цвет <see cref="UnityEngine.SpriteRenderer"/> основного спрайта для состояния карты.</summary>
    public Color GetNodeMainSpriteColor(NodeMapState state) =>
        state switch
        {
            NodeMapState.Inactive => nodeMainSpriteInactive,
            NodeMapState.Appearing => nodeMainSpriteAppearing,
            NodeMapState.Visible => nodeMainSpriteVisible,
            NodeMapState.Selected => nodeMainSpriteSelected,
            NodeMapState.Deselected => nodeMainSpriteDeselected,
            NodeMapState.Blocked => nodeMainSpriteBlocked,
            _ => nodeMainSpriteVisible
        };

    /// <summary>Единый цвет кольца выбора (когда оно показывается).</summary>
    public Color NodeSelectionRingColor => nodeSelectionRingColor;

    private void Reset()
    {
        edgeGradientColorA = Color.white;
        edgeGradientColorB = new Color(1f, 0.25f, 0.25f, 1f);
        edgeGradientColorC = new Color(0.25f, 0.45f, 1f, 1f);

        nodeMainSpriteInactive = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        nodeMainSpriteAppearing = Color.white;
        nodeMainSpriteVisible = Color.white;
        nodeMainSpriteSelected = new Color(1f, 0.95f, 0.75f, 1f);
        nodeMainSpriteDeselected = Color.white;
        nodeMainSpriteBlocked = new Color(0.75f, 0.55f, 0.55f, 1f);
        nodeSelectionRingColor = new Color(0.35f, 0.85f, 1f, 1f);
    }
}

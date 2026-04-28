using UnityEngine;

/// <summary>
/// Общая палитра визуала графа карты: рёбра (LineRenderer) и ноды (mainSprite / selection ring).
/// </summary>
[CreateAssetMenu(fileName = "MinimapGraphVisualPalette", menuName = "Environmental/Minimap Graph Visual Palette", order = 10)]
public class MinimapGraphVisualPalette : ScriptableObject
{
    [System.Serializable]
    public struct EdgeLineColorPair
    {
        public Color startColor;
        public Color endColor;
    }

    [Header("Рёбра — LineRenderer по MinimapEdgeState")]
    [SerializeField] private EdgeLineColorPair disabled;
    [SerializeField] private EdgeLineColorPair appearing;
    [SerializeField] private EdgeLineColorPair movingAlongEdge;
    [SerializeField] private EdgeLineColorPair idle;
    [SerializeField] private EdgeLineColorPair blocked;

    /// <summary>Цвета линии для состояния ребра; всегда успешно (поля заданы в ассете).</summary>
    public void GetEdgeLineColors(MinimapEdgeState state, out Color startColor, out Color endColor)
    {
        EdgeLineColorPair p = state switch
        {
            MinimapEdgeState.Disabled => disabled,
            MinimapEdgeState.Appearing => appearing,
            MinimapEdgeState.MovingAlongEdge => movingAlongEdge,
            MinimapEdgeState.Idle => idle,
            MinimapEdgeState.Blocked => blocked,
            _ => idle
        };
        startColor = p.startColor;
        endColor = p.endColor;
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
        disabled = new EdgeLineColorPair
        {
            startColor = new Color(0.35f, 0.35f, 0.35f, 0.35f),
            endColor = new Color(0.35f, 0.35f, 0.35f, 0.35f)
        };
        appearing = new EdgeLineColorPair
        {
            startColor = new Color(0.4f, 0.85f, 1f, 1f),
            endColor = new Color(0.65f, 0.95f, 1f, 1f)
        };
        movingAlongEdge = new EdgeLineColorPair
        {
            startColor = new Color(1f, 0.92f, 0.35f, 1f),
            endColor = new Color(1f, 0.75f, 0.2f, 1f)
        };
        idle = new EdgeLineColorPair
        {
            startColor = Color.white,
            endColor = Color.white
        };
        blocked = new EdgeLineColorPair
        {
            startColor = new Color(0.55f, 0.2f, 0.2f, 1f),
            endColor = new Color(0.55f, 0.2f, 0.2f, 1f)
        };

        nodeMainSpriteInactive = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        nodeMainSpriteAppearing = Color.white;
        nodeMainSpriteVisible = Color.white;
        nodeMainSpriteSelected = new Color(1f, 0.95f, 0.75f, 1f);
        nodeMainSpriteDeselected = Color.white;
        nodeMainSpriteBlocked = new Color(0.75f, 0.55f, 0.55f, 1f);
        nodeSelectionRingColor = new Color(0.35f, 0.85f, 1f, 1f);
    }
}

using UnityEngine;

/// <summary>
/// Общая палитра визуала графа карты (рёбра LineRenderer; позже — ноды).
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
    }
}

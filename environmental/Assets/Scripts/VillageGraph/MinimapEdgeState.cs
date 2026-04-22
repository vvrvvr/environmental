/// <summary>
/// Визуальное состояние ребра мини-карты (отдельно от <see cref="NodeMapState"/> ноды).
/// </summary>
public enum MinimapEdgeState
{
    /// <summary>Линия выключена.</summary>
    Disabled = 0,

    /// <summary>Заглушка: линия включена.</summary>
    Appearing = 1,

    /// <summary>Заглушка: таймер, затем переход в <see cref="Idle"/> (только в Play Mode).</summary>
    MovingAlongEdge = 2,

    /// <summary>Обычный вид линии.</summary>
    Idle = 3,

    /// <summary>Линия с цветом «заблокировано» из инспектора.</summary>
    Blocked = 4,
}

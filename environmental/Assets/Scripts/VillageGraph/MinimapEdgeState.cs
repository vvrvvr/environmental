/// <summary>
/// Визуальное состояние ребра мини-карты (отдельно от <see cref="NodeMapState"/> ноды).
/// </summary>
public enum MinimapEdgeState
{
    /// <summary>Линия выключена (игнорирует слой видимости по карте).</summary>
    Disabled = 0,

    /// <summary>Заглушка: линия включена (если разрешено выбором на карте).</summary>
    Appearing = 1,

    /// <summary>Заглушка: таймер в Play, затем <see cref="Idle"/>.</summary>
    MovingAlongEdge = 2,

    /// <summary>Обычный вид линии.</summary>
    Idle = 3,

    /// <summary>Линия с цветом «заблокировано» из инспектора.</summary>
    Blocked = 4,
}

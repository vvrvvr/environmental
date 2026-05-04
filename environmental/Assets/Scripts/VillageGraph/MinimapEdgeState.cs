/// <summary>
/// Визуальное состояние ребра мини-карты (отдельно от <see cref="NodeMapState"/> ноды).
/// </summary>
public enum MinimapEdgeState
{
    /// <summary>Линия выключена (игнорирует слой видимости по карте).</summary>
    Disabled = 0,

    /// <summary>В Play: линия «растёт» от старта к концу (см. <see cref="MinimapEdge"/>), затем <see cref="IdleRevealed"/>.</summary>
    Appearing = 1,

    /// <summary>Заглушка: таймер в Play, затем <see cref="Idle"/>.</summary>
    MovingAlongEdge = 2,

    /// <summary>Обычный вид линии.</summary>
    Idle = 3,

    /// <summary>Линия с цветом «заблокировано» из инспектора.</summary>
    Blocked = 4,

    /// <summary>Как <see cref="Idle"/>: полная линия, но после завершения <see cref="Appearing"/> (ребро уже «показалось»).</summary>
    IdleRevealed = 5,

    /// <summary>Конец ребра (<see cref="MinimapEdge.ToNode"/>) — текущая выбранная на карте нода (корень через <see cref="Node.SelectionOwner"/>).</summary>
    Selected = 6,
}

/// <summary>Вспомогательные проверки по <see cref="MinimapEdgeState"/>.</summary>
public static class MinimapEdgeStateUtil
{
    /// <summary>Состояния с полной линией как у Idle (до скрытия под Appearing / stagger).</summary>
    public static bool IsFullLineIdleLike(MinimapEdgeState s) =>
        s == MinimapEdgeState.Idle || s == MinimapEdgeState.IdleRevealed || s == MinimapEdgeState.Selected;

    /// <summary>
    /// В Play: коллайдер конца ребра на карте не должен ловить указатель в фазах без стабильной линии
    /// (<see cref="MinimapEdgeState.Disabled"/>, <see cref="MinimapEdgeState.Appearing"/>, <see cref="MinimapEdgeState.MovingAlongEdge"/>).
    /// </summary>
    public static bool AllowsMapEdgeEndColliderPointer(MinimapEdgeState s) =>
        s != MinimapEdgeState.Disabled &&
        s != MinimapEdgeState.Appearing &&
        s != MinimapEdgeState.MovingAlongEdge;
}

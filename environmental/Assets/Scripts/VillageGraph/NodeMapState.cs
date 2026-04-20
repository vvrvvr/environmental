/// <summary>
/// Состояние ноды на карте. Добавляй новые значения в конец, не переупорядочивай числа без миграции сцен/сейвов.
/// Логику входа/выхода см. <see cref="Node"/> (partial: StateMachine).
/// </summary>
public enum NodeMapState
{
    /// <summary>Не на карте: визуал скрыт, <see cref="UnityEngine.Behaviour.enabled"/> выключен — <c>Update</c> не выполняется.</summary>
    Inactive = 0,

    /// <summary>Появление (заготовка под анимацию; по умолчанию уходит в <see cref="Visible"/> после задержки).</summary>
    Appearing = 1,

    /// <summary>Видима, интерактивна: ховер/клик как раньше.</summary>
    Visible = 2,

    /// <summary>Выбрана: кольцо-селектор, интерактивна.</summary>
    Selected = 3,

    /// <summary>Перестала быть выбранной: заготовка под короткий переход, затем обычно <see cref="Visible"/>.</summary>
    Deselected = 4,

    /// <summary>Видима, не кликабельна: другой спрайт (заблокирована).</summary>
    Blocked = 5,
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Группа нод: родитель владеет селектором и задаёт порядок дочерних для будущего видео.
/// </summary>
public partial class Node
{
    [Header("Node group")]
    [Tooltip("Родитель группы: на этой ноде кольцо-селектор и список дочерних в порядке обхода.")]
    [SerializeField] private bool isGroupParent;

    [Tooltip("Если нода входит в группу — ссылка на родителя с включённым «Is Group Parent». Оставь пустым для обычной ноды.")]
    [SerializeField] private Node groupParent;

    [Tooltip("Дочерние ноды в порядке будущего переключения (видео и т.д.). Имеет смысл только при Is Group Parent.")]
    [SerializeField] private List<Node> orderedChildNodes = new List<Node>();

    /// <summary>Эта нода — корень группы (селектор и порядок детей).</summary>
    public bool IsGroupParent => isGroupParent;

    /// <summary>Родитель группы, если нода дочерняя; иначе null.</summary>
    public Node GroupParent => groupParent;

    /// <summary>Дочерние ноды в порядке, заданном в инспекторе (для будущего плейлиста / обхода).</summary>
    public IReadOnlyList<Node> OrderedChildNodes => orderedChildNodes;

    /// <summary>Логический «владелец» выбора: для дочерней ноды — родитель, иначе сама нода.</summary>
    public Node SelectionOwner => groupParent != null ? groupParent : this;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (isGroupParent && groupParent != null)
            Debug.LogWarning($"{name}: включён Is Group Parent, но задана ссылка Group Parent — очищай ссылку на родителя у корня группы.", this);

        if (!isGroupParent && orderedChildNodes != null && orderedChildNodes.Count > 0)
            Debug.LogWarning($"{name}: задан список Ordered Child Nodes при выключенном Is Group Parent.", this);

        if (groupParent == this)
            Debug.LogWarning($"{name}: Group Parent указывает на саму себя.", this);
    }
#endif
}

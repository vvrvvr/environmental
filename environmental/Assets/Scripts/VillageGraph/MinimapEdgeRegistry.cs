using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Реестр всех рёбер мини-карты на сцене. Заполни список в инспекторе (или позже — автосбор).
/// </summary>
[DisallowMultipleComponent]
public class MinimapEdgeRegistry : MonoBehaviour
{
    [Tooltip("Все рёбра мини-карты. В инспекторе есть кнопка «Собрать все рёбра со сцены».")]
    [SerializeField] private List<MinimapEdge> edges = new List<MinimapEdge>();

    public IReadOnlyList<MinimapEdge> Edges => edges;
}

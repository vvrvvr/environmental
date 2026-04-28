using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MinimapGraphVisualPalette))]
public class MinimapGraphVisualPaletteEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("Применить цвета к рёбрам с этой палитрой"))
            PushColorsToReferencingEdges();
        if (GUILayout.Button("Применить цвета к нодам с этой палитрой"))
            PushColorsToReferencingNodes();
    }

    private void PushColorsToReferencingEdges()
    {
        var palette = (MinimapGraphVisualPalette)target;
        var edges = Object.FindObjectsOfType<MinimapEdge>(true);
        var n = 0;
        for (var i = 0; i < edges.Length; i++)
        {
            var e = edges[i];
            if (e == null || e.LineColorPalette != palette)
                continue;
            e.ApplyCombinedVisual();
            n++;
        }

        EditorUtility.DisplayDialog(
            "Палитра",
            n > 0
                ? $"Обновлено рёбер: {n} (в т.ч. в Play Mode)."
                : "Ни одно MinimapEdge на загруженных сценах не ссылается на эту палитру. Назначь её в инспекторе ребра.",
            "OK");
    }

    private void PushColorsToReferencingNodes()
    {
        var palette = (MinimapGraphVisualPalette)target;
        var nodes = Object.FindObjectsOfType<Node>(true);
        var n = 0;
        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            if (node == null || node.MapVisualPalette != palette)
                continue;
            node.RefreshMapVisualPaletteFromCurrentState();
            n++;
        }

        EditorUtility.DisplayDialog(
            "Палитра",
            n > 0
                ? $"Обновлено нод: {n} (в т.ч. в Play Mode)."
                : "Ни одна Node на загруженных сценах не ссылается на эту палитру. Назначь её на ноде или через реестр.",
            "OK");
    }
}

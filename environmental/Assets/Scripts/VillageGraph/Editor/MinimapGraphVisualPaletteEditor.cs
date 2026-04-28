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
}

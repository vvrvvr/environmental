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
        palette.GetEdgeGradientColors(out var ca, out var cb, out var cc);

        var edges = Object.FindObjectsByType<MinimapEdge>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var drivers = new System.Collections.Generic.List<LineRendererGradientPropertyDriver>();
        var edgeCount = 0;
        for (var i = 0; i < edges.Length; i++)
        {
            var e = edges[i];
            if (e == null || e.LineColorPalette != palette)
                continue;
            edgeCount++;
            var driver = e.GetComponent<LineRendererGradientPropertyDriver>();
            if (driver != null)
                drivers.Add(driver);
        }

        if (drivers.Count > 0)
        {
            Undo.RecordObjects(drivers.ToArray(), "Градиент рёбер (палитра A/B/C)");
            for (var i = 0; i < drivers.Count; i++)
            {
                drivers[i].SetColorsABC(ca, cb, cc);
                EditorUtility.SetDirty(drivers[i]);
            }
        }

        var missing = edgeCount - drivers.Count;
        string msg;
        if (edgeCount == 0)
            msg = "Ни одно MinimapEdge на загруженных сценах не ссылается на эту палитру. Назначь её в инспекторе ребра.";
        else if (drivers.Count == 0)
            msg = $"Найдено рёбер с палитрой: {edgeCount}, но ни на одном нет LineRendererGradientPropertyDriver — добавь компонент на объект ребра.";
        else
            msg =
                $"Записано в LineRendererGradientPropertyDriver: {drivers.Count} из {edgeCount} рёбер (цвета A/B/C из палитры)." +
                (missing > 0 ? $"\n\nБез драйвера: {missing}." : "");

        EditorUtility.DisplayDialog("Палитра", msg, "OK");
    }

    private void PushColorsToReferencingNodes()
    {
        var palette = (MinimapGraphVisualPalette)target;
        palette.GetNodeGradientColors(out var ca, out var cb, out var cc);

        var nodes = Object.FindObjectsByType<Node>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var drivers = new System.Collections.Generic.List<SpriteRendererGradientPropertyDriver>();
        var nodeCount = 0;
        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            if (node == null || node.MapVisualPalette != palette)
                continue;
            nodeCount++;
            var onNode = node.GetComponentsInChildren<SpriteRendererGradientPropertyDriver>(true);
            for (var j = 0; j < onNode.Length; j++)
            {
                if (onNode[j] != null)
                    drivers.Add(onNode[j]);
            }
        }

        if (drivers.Count > 0)
        {
            Undo.RecordObjects(drivers.ToArray(), "Градиент нод (палитра A/B/C)");
            for (var i = 0; i < drivers.Count; i++)
            {
                drivers[i].SetColorsABC(ca, cb, cc);
                EditorUtility.SetDirty(drivers[i]);
            }
        }

        string msg;
        if (nodeCount == 0)
            msg = "Ни одна Node на загруженных сценах не ссылается на эту палитру. Назначь её на ноде или через реестр.";
        else if (drivers.Count == 0)
            msg =
                $"Найдено нод с палитрой: {nodeCount}, но ни на одной нет SpriteRendererGradientPropertyDriver в иерархии — добавь компонент и назначь target SpriteRenderer.";
        else
            msg =
                $"Записано в SpriteRendererGradientPropertyDriver: {drivers.Count} (на {nodeCount} нодах с этой палитрой, цвета A/B/C из ассета).";

        EditorUtility.DisplayDialog("Палитра", msg, "OK");
    }
}

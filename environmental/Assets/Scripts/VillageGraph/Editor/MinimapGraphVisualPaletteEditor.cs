using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MinimapGraphVisualPalette))]
public class MinimapGraphVisualPaletteEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("Применить цвета к нодам и рёбрам с этой палитрой"))
            PushColorsToReferencingGraph();
    }

    private void PushColorsToReferencingGraph()
    {
        var palette = (MinimapGraphVisualPalette)target;
        palette.GetGradientColors(out var ca, out var cb, out var cc);

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
            Undo.RecordObjects(drivers.ToArray(), "Градиент рёбер (общая палитра A/B/C)");
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
        var nodes = Object.FindObjectsByType<Node>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var nodesWithPalette = new System.Collections.Generic.List<Node>();
        var nodeDrivers = new System.Collections.Generic.List<SpriteRendererGradientPropertyDriver>();
        var nodeSelectionRings = new System.Collections.Generic.List<SpriteRenderer>();
        var nodeCount = 0;
        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            if (node == null || node.MapVisualPalette != palette)
                continue;
            nodeCount++;
            nodesWithPalette.Add(node);
            var driversOnNode = node.GetComponentsInChildren<SpriteRendererGradientPropertyDriver>(true);
            for (var j = 0; j < driversOnNode.Length; j++)
            {
                if (driversOnNode[j] != null)
                    nodeDrivers.Add(driversOnNode[j]);
            }
            if (node.SelectionRingRenderer != null)
                nodeSelectionRings.Add(node.SelectionRingRenderer);
        }

        if (nodesWithPalette.Count > 0 || nodeDrivers.Count > 0 || nodeSelectionRings.Count > 0)
        {
            var undoTargets = new System.Collections.Generic.List<Object>(nodesWithPalette.Count + nodeDrivers.Count + nodeSelectionRings.Count);
            undoTargets.AddRange(nodesWithPalette);
            undoTargets.AddRange(nodeDrivers);
            undoTargets.AddRange(nodeSelectionRings);
            Undo.RecordObjects(undoTargets.ToArray(), "Градиент нод (общая палитра A/B/C)");
            for (var i = 0; i < nodesWithPalette.Count; i++)
            {
                nodesWithPalette[i].ApplyPaletteGradientDriversFromPalette();
                EditorUtility.SetDirty(nodesWithPalette[i]);
            }
            for (var i = 0; i < nodeDrivers.Count; i++)
                EditorUtility.SetDirty(nodeDrivers[i]);
            for (var i = 0; i < nodeSelectionRings.Count; i++)
                EditorUtility.SetDirty(nodeSelectionRings[i]);
        }

        var nodeDriverCount = nodeDrivers.Count;

        string nodeMsg;
        if (nodeCount == 0)
            nodeMsg = "Ни одна Node на загруженных сценах не ссылается на эту палитру. Назначь её на ноде или через реестр.";
        else if (nodeDriverCount == 0)
            nodeMsg =
                $"Найдено нод с палитрой: {nodeCount}, но ни на одной нет SpriteRendererGradientPropertyDriver в иерархии — добавь компонент и назначь target SpriteRenderer.";
        else
            nodeMsg =
                $"Обновлено нод: {nodesWithPalette.Count} (драйверов SpriteRendererGradientPropertyDriver: {nodeDriverCount}; selector.color = Gradient B).";

        EditorUtility.DisplayDialog("Палитра", $"{msg}\n\n{nodeMsg}", "OK");
    }
}

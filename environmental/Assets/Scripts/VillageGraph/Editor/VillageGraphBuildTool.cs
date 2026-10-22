using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Сборка графа одной командой и вспомогательные действия для расстановки рёбер.
/// Меню: Tools / Граф / … (горячие клавиши в названиях пунктов).
/// </summary>
public static class VillageGraphBuildTool
{
    private const string MenuRoot = "Tools/Граф/";

    // ------------------------------------------------------------------ Собрать граф

    [MenuItem(MenuRoot + "Собрать граф %#g", priority = 0)]
    public static void BuildGraphMenu() => BuildGraph(showDialogWhenClean: true);

    [MenuItem(MenuRoot + "Собрать граф %#g", validate = true)]
    private static bool BuildGraphMenuValidate() => !Application.isPlaying;

    /// <summary>
    /// 1) все рёбра сцены → реестр; 2) новые/отвязанные рёбра — связать по коллайдерам якорей; 3) концы рёбер следуют за нодами;
    /// 4) палитра на рёбра и ноды; 5) физика пересобрана по текущим позициям; 6) отчёт.
    /// </summary>
    public static void BuildGraph(bool showDialogWhenClean)
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Собрать граф", "Выйди из Play Mode.", "OK");
            return;
        }

        var registry = Object.FindObjectOfType<MinimapEdgeRegistry>(true);
        if (registry == null)
        {
            EditorUtility.DisplayDialog("Собрать граф", "В сцене нет MinimapEdgeRegistry.", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Собрать граф");

        var errors = new List<(string text, Object ctx)>();
        var warnings = new List<(string text, Object ctx)>();

        // 1. Рёбра сцены → реестр.
        var edges = Object.FindObjectsOfType<MinimapEdge>(true).Where(e => e != null).ToList();
        edges.Sort((a, b) => string.CompareOrdinal(GetPath(a.transform), GetPath(b.transform)));
        WriteRegistryEdges(registry, edges);

        // 2–3. Связать и «приклеить» концы к нодам.
        int linkedNew = 0, kept = 0;
        foreach (var edge in edges)
        {
            bool needsLink = !edge.AnchorsFollowLinkedNodes || edge.FromNode == null || edge.ToNode == null;
            if (needsLink)
            {
                if (!MinimapEdgeLinkEditorUtility.ConnectAnchorsToNodesByCollider(edge, out var err))
                {
                    errors.Add(($"{edge.name}: {err}", edge));
                    continue;
                }

                linkedNew++;
            }
            else
            {
                kept++;
            }

            if (!edge.AnchorsFollowLinkedNodes)
            {
                if (!MinimapEdgeLinkEditorUtility.BindAnchorsToFollowLinkedNodes(edge, out var bindErr))
                    errors.Add(($"{edge.name}: {bindErr}", edge));
            }

            edge.RefreshLinePositions();
        }

        // Проверки рёбер.
        var pairs = new Dictionary<(Node, Node), MinimapEdge>();
        foreach (var edge in edges)
        {
            var from = edge.FromNode;
            var to = edge.ToNode;
            if (from == null || to == null)
                continue;

            if (from == to)
                errors.Add(($"{edge.name}: оба конца в одной ноде «{from.name}».", edge));
            if (from.GroupParent != null)
                warnings.Add(($"{edge.name}: начинается у дочерней ноды группы «{from.name}» — рёбра должны идти от родителя «{from.GroupParent.name}».", edge));

            var key = (from.SelectionOwner, to.SelectionOwner);
            if (pairs.TryGetValue(key, out var other))
                warnings.Add(($"{edge.name}: дубль ребра {from.name} → {to.name} (уже есть «{other.name}»). Если это копия — «Отвязать выделенные рёбра» и переставь.", edge));
            else
                pairs[key] = edge;
        }

        // Проверки нод.
        var nodes = Object.FindObjectsOfType<Node>(true).Where(n => n != null).ToList();
        var roots = nodes.Where(n => n.GroupParent == null).ToList();
        var touched = new HashSet<Node>();
        foreach (var e in edges)
        {
            if (e.FromNode != null) touched.Add(e.FromNode.SelectionOwner);
            if (e.ToNode != null) touched.Add(e.ToNode.SelectionOwner);
        }

        int starts = roots.Count(r => r.IsMinimapStartNode);
        int finals = roots.Count(r => r.IsBranchFinalNode);
        if (starts == 0)
            errors.Add(("Нет ни одной стартовой ноды (Is Minimap Start Node).", null));
        foreach (var r in roots)
        {
            if (!touched.Contains(r))
                warnings.Add(($"Нода «{r.name}» ни с чем не соединена.", r));
            if (!r.IsBranchFinalNode && !r.IsMinimapStartNode && touched.Contains(r) &&
                !edges.Any(e => e.FromNode != null && e.FromNode.SelectionOwner == r))
                warnings.Add(($"Нода «{r.name}» — тупик без выхода, но не помечена финальной.", r));
        }

        // 4. Палитра.
        ApplyPalette(registry, edges, nodes);

        // 5. Кэш и физика.
        Undo.RecordObject(registry, "Собрать граф");
        registry.RebuildEdgeCache();
        EditorUtility.SetDirty(registry);

        var physics = registry.GetComponent<VillageGraphPhysicsSetup>();
        if (physics == null)
            physics = Object.FindObjectOfType<VillageGraphPhysicsSetup>(true);
        if (physics != null)
        {
            Undo.RecordObject(physics, "Собрать граф");
            physics.BuildPhysicalGraph();
            EditorUtility.SetDirty(physics);
        }
        else
        {
            warnings.Add(("Нет VillageGraphPhysicsSetup — физика не пересобрана.", null));
        }

        EditorSceneManager.MarkSceneDirty(registry.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);

        // 6. Отчёт.
        var summary = $"Нод: {roots.Count} (стартовых {starts}, финальных {finals}). Рёбер: {edges.Count} " +
                      $"(связано заново {linkedNew}, без изменений {kept}). " +
                      $"Физика: {(physics != null ? physics.GeneratedHinges.Count + " шарниров" : "—")}.";
        Debug.Log($"[Граф] Собран. {summary}", registry);
        foreach (var (text, ctx) in errors)
            Debug.LogError($"[Граф] {text}", ctx);
        foreach (var (text, ctx) in warnings)
            Debug.LogWarning($"[Граф] {text}", ctx);

        if (errors.Count > 0 || warnings.Count > 0)
        {
            var sb = new StringBuilder(summary).AppendLine().AppendLine();
            if (errors.Count > 0)
            {
                sb.AppendLine($"Ошибки ({errors.Count}):");
                foreach (var (text, _) in errors.Take(12))
                    sb.AppendLine("• " + text);
                if (errors.Count > 12)
                    sb.AppendLine($"… ещё {errors.Count - 12}");
                sb.AppendLine();
            }

            if (warnings.Count > 0)
            {
                sb.AppendLine($"Предупреждения ({warnings.Count}):");
                foreach (var (text, _) in warnings.Take(12))
                    sb.AppendLine("• " + text);
                if (warnings.Count > 12)
                    sb.AppendLine($"… ещё {warnings.Count - 12}");
            }

            sb.AppendLine().Append("Всё то же — в консоли, клик по строке выделит объект.");
            EditorUtility.DisplayDialog("Собрать граф", sb.ToString(), "OK");
        }
        else if (showDialogWhenClean)
        {
            EditorUtility.DisplayDialog("Собрать граф", summary + "\n\nОшибок нет. Сохрани сцену.", "OK");
        }
    }

    private static void WriteRegistryEdges(MinimapEdgeRegistry registry, List<MinimapEdge> edges)
    {
        var so = new SerializedObject(registry);
        var prop = so.FindProperty("edges");
        prop.ClearArray();
        for (var i = 0; i < edges.Count; i++)
        {
            prop.InsertArrayElementAtIndex(i);
            prop.GetArrayElementAtIndex(i).objectReferenceValue = edges[i];
        }

        so.ApplyModifiedProperties();
    }

    private static void ApplyPalette(MinimapEdgeRegistry registry, List<MinimapEdge> edges, List<Node> nodes)
    {
        var palette = registry.SharedMapVisualPalette;
        if (palette == null)
            return;

        palette.GetGradientColors(out var ca, out var cb, out var cc);

        foreach (var e in edges)
        {
            Undo.RecordObject(e, "Собрать граф");
            e.SetLineColorPalette(palette);
            var driver = e.GetComponent<LineRendererGradientPropertyDriver>();
            if (driver != null)
            {
                Undo.RecordObject(driver, "Собрать граф");
                driver.SetColorsABC(ca, cb, cc);
                EditorUtility.SetDirty(driver);
            }

            EditorUtility.SetDirty(e);
        }

        foreach (var n in nodes)
        {
            var drivers = n.GetComponentsInChildren<SpriteRendererGradientPropertyDriver>(true);
            var undoTargets = new List<Object> { n };
            undoTargets.AddRange(drivers);
            if (n.SelectionRingRenderer != null)
                undoTargets.Add(n.SelectionRingRenderer);
            Undo.RecordObjects(undoTargets.ToArray(), "Собрать граф");

            n.SetMapVisualPalette(palette);
            n.ApplyPaletteGradientDriversFromPalette();
            foreach (var t in undoTargets)
                EditorUtility.SetDirty(t);
            PrefabUtility.RecordPrefabInstancePropertyModifications(n);
        }
    }

    // ------------------------------------------------------------------ Соединить выбранные ноды

    [MenuItem(MenuRoot + "Соединить выбранные ноды ребром %#e", priority = 20)]
    public static void ConnectSelectedNodes()
    {
        var selectedNodes = Selection.gameObjects
            .Select(g => g.GetComponentInParent<Node>())
            .Where(n => n != null)
            .Distinct()
            .ToList();

        if (selectedNodes.Count != 2)
        {
            EditorUtility.DisplayDialog("Соединить ноды",
                "Выдели ровно две ноды: сначала ту, откуда ребро, затем с Ctrl — ту, куда.", "OK");
            return;
        }

        var activeNode = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<Node>() : null;
        var to = activeNode != null && selectedNodes.Contains(activeNode) ? activeNode : selectedNodes[1];
        var from = selectedNodes[0] == to ? selectedNodes[1] : selectedNodes[0];

        if (from.GroupParent != null)
        {
            Debug.LogWarning($"[Граф] «{from.name}» — дочерняя нода группы, ребро пойдёт от родителя «{from.GroupParent.name}».", from);
            from = from.GroupParent;
        }

        var registry = Object.FindObjectOfType<MinimapEdgeRegistry>(true);
        var existing = Object.FindObjectsOfType<MinimapEdge>(true)
            .FirstOrDefault(e => e.FromNode != null && e.ToNode != null &&
                                 e.FromNode.SelectionOwner == from.SelectionOwner && e.ToNode.SelectionOwner == to.SelectionOwner);
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing);
            EditorUtility.DisplayDialog("Соединить ноды", $"Ребро {from.name} → {to.name} уже есть: «{existing.name}».", "OK");
            return;
        }

        var prefab = FindEdgePrefab();
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Соединить ноды", "Не нашёл префаб ребра (префаб с компонентом MinimapEdge).", "OK");
            return;
        }

        var parent = FindEdgesParent(registry);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent != null ? parent.gameObject.scene : from.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(go, "Соединить ноды ребром");
        if (parent != null)
            Undo.SetTransformParent(go.transform, parent, "Соединить ноды ребром");
        go.name = $"Edge {from.name} → {to.name}";
        go.transform.position = (from.transform.position + to.transform.position) * 0.5f;

        var edge = go.GetComponent<MinimapEdge>();
        var so = new SerializedObject(edge);
        so.FindProperty("fromNode").objectReferenceValue = from;
        so.FindProperty("toNode").objectReferenceValue = to;
        so.ApplyModifiedProperties();

        if (edge.StartAnchor != null)
            edge.StartAnchor.position = from.transform.position;
        if (edge.EndAnchor != null)
            edge.EndAnchor.position = to.transform.position;

        if (!MinimapEdgeLinkEditorUtility.BindAnchorsToFollowLinkedNodes(edge, out var err))
            Debug.LogWarning($"[Граф] {go.name}: {err}", go);
        if (registry != null && registry.SharedMapVisualPalette != null)
            edge.SetLineColorPalette(registry.SharedMapVisualPalette);
        edge.RefreshLinePositions();

        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log($"[Граф] Создано ребро {from.name} → {to.name}. Нажми «Собрать граф», чтобы обновить физику.", go);
    }

    [MenuItem(MenuRoot + "Соединить выбранные ноды ребром %#e", validate = true)]
    private static bool ConnectSelectedNodesValidate() => !Application.isPlaying;

    // ------------------------------------------------------------------ Отвязать выделенные рёбра

    [MenuItem(MenuRoot + "Отвязать выделенные рёбра (для перестановки)", priority = 21)]
    public static void UnlinkSelectedEdges()
    {
        var edges = Selection.gameObjects
            .Select(g => g.GetComponentInParent<MinimapEdge>())
            .Where(e => e != null)
            .Distinct()
            .ToList();
        if (edges.Count == 0)
        {
            EditorUtility.DisplayDialog("Отвязать рёбра", "Выдели одно или несколько рёбер.", "OK");
            return;
        }

        foreach (var e in edges)
        {
            Undo.RecordObject(e, "Отвязать рёбра");
            e.SetAnchorsFollowLinkedNodes(false);
            var so = new SerializedObject(e);
            so.FindProperty("fromNode").objectReferenceValue = null;
            so.FindProperty("toNode").objectReferenceValue = null;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(e);
        }

        EditorSceneManager.MarkSceneDirty(edges[0].gameObject.scene);
        Debug.Log($"[Граф] Отвязано рёбер: {edges.Count}. Переставь их якорями на нужные ноды и нажми «Собрать граф».");
    }

    [MenuItem(MenuRoot + "Отвязать выделенные рёбра (для перестановки)", validate = true)]
    private static bool UnlinkSelectedEdgesValidate() => !Application.isPlaying;

    // ------------------------------------------------------------------ helpers

    private static GameObject FindEdgePrefab()
    {
        GameObject fallback = null;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null || go.GetComponent<MinimapEdge>() == null)
                continue;
            if (go.name == "Edge")
                return go;
            fallback ??= go;
        }

        return fallback;
    }

    private static Transform FindEdgesParent(MinimapEdgeRegistry registry)
    {
        var any = registry != null ? registry.Edges.FirstOrDefault(e => e != null) : null;
        if (any == null)
            any = Object.FindObjectOfType<MinimapEdge>(true);
        return any != null ? any.transform.parent : null;
    }

    private static string GetPath(Transform t)
    {
        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}

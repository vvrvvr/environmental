using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MinimapEdgeRegistry))]
public class MinimapEdgeRegistryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
        {
            if (GUILayout.Button("Собрать все рёбра со сцены"))
                CollectAllEdgesFromScene();
            if (GUILayout.Button("Пересобрать кэш рёбер"))
                RebuildCacheOnly();
            EditorGUILayout.Space();
            if (GUILayout.Button("Связать все рёбра (якоря → ноды по коллайдерам)"))
                ConnectAllEdgesInList();
            if (GUILayout.Button("Повесить якоря всех рёбер на ноды (ноль локально)"))
                ParentAllAnchorsInList();
        }
    }

    private void RebuildCacheOnly()
    {
        var registry = (MinimapEdgeRegistry)target;
        Undo.RecordObject(registry, "Пересобрать кэш MinimapEdgeRegistry");
        registry.RebuildEdgeCache();
        if (Application.isPlaying)
            registry.RefreshOutgoingLineVisibilityForMapSelection();
        EditorUtility.SetDirty(registry);
    }

    private void CollectAllEdgesFromScene()
    {
        var registry = (MinimapEdgeRegistry)target;
        serializedObject.Update();

        var edgesProp = serializedObject.FindProperty("edges");
        if (edgesProp == null)
        {
            Debug.LogError("MinimapEdgeRegistry: не найдено поле edges.", registry);
            return;
        }

        var found = new List<MinimapEdge>(Object.FindObjectsOfType<MinimapEdge>(true));
        found.RemoveAll(e => e == null);
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        Undo.RecordObject(registry, "Собрать рёбра MinimapEdgeRegistry");
        edgesProp.ClearArray();
        for (var i = 0; i < found.Count; i++)
        {
            edgesProp.InsertArrayElementAtIndex(i);
            edgesProp.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        }

        serializedObject.ApplyModifiedProperties();
        registry.RebuildEdgeCache();
        if (Application.isPlaying)
            registry.RefreshOutgoingLineVisibilityForMapSelection();
        EditorUtility.SetDirty(registry);
    }

    private void ConnectAllEdgesInList()
    {
        serializedObject.Update();
        var edgesProp = serializedObject.FindProperty("edges");
        if (edgesProp == null)
        {
            EditorUtility.DisplayDialog("Реестр", "Не найдено поле edges.", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();

        var failed = new StringBuilder();
        var ok = 0;
        for (var i = 0; i < edgesProp.arraySize; i++)
        {
            var edge = edgesProp.GetArrayElementAtIndex(i).objectReferenceValue as MinimapEdge;
            if (edge == null)
                continue;

            if (MinimapEdgeLinkEditorUtility.ConnectAnchorsToNodesByCollider(edge, out var err))
            {
                ok++;
                edge.RefreshLinePositions();
            }
            else
                failed.AppendLine($"• {edge.name}: {err}");
        }

        Undo.SetCurrentGroupName("Связать все рёбра (реестр)");

        if (failed.Length > 0)
            EditorUtility.DisplayDialog(
                "Связать все рёбра",
                $"Успешно: {ok}.\n\nОшибки:\n{failed}",
                "OK");
        else
            EditorUtility.DisplayDialog("Связать все рёбра", $"Связано рёбер: {ok}.", "OK");
    }

    private void ParentAllAnchorsInList()
    {
        serializedObject.Update();
        var edgesProp = serializedObject.FindProperty("edges");
        if (edgesProp == null)
        {
            EditorUtility.DisplayDialog("Реестр", "Не найдено поле edges.", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();

        var failed = new StringBuilder();
        var ok = 0;
        for (var i = 0; i < edgesProp.arraySize; i++)
        {
            var edge = edgesProp.GetArrayElementAtIndex(i).objectReferenceValue as MinimapEdge;
            if (edge == null)
                continue;

            if (MinimapEdgeLinkEditorUtility.ParentAnchorsUnderLinkedNodes(edge, out var err))
            {
                ok++;
                edge.RefreshLinePositions();
            }
            else
                failed.AppendLine($"• {edge.name}: {err}");
        }

        Undo.SetCurrentGroupName("Якоря на ноды (реестр)");

        if (failed.Length > 0)
            EditorUtility.DisplayDialog(
                "Якоря на ноды",
                $"Успешно: {ok}.\n\nПропуски / ошибки:\n{failed}",
                "OK");
        else
            EditorUtility.DisplayDialog("Якоря на ноды", $"Обработано рёбер: {ok}.", "OK");
    }
}

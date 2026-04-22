using System.Collections.Generic;
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
        }
    }

    private void RebuildCacheOnly()
    {
        var registry = (MinimapEdgeRegistry)target;
        Undo.RecordObject(registry, "Пересобрать кэш MinimapEdgeRegistry");
        registry.RebuildEdgeCache();
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
        EditorUtility.SetDirty(registry);
    }
}

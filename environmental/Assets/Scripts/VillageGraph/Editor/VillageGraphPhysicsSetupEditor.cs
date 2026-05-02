using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VillageGraphPhysicsSetup))]
public sealed class VillageGraphPhysicsSetupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        var setup = (VillageGraphPhysicsSetup)target;

        if (GUILayout.Button("Построить физичный граф", GUILayout.Height(26f)))
        {
            Undo.RecordObject(setup, "Построить физичный граф");
            setup.BuildPhysicalGraph();
            EditorUtility.SetDirty(setup);
        }

        if (GUILayout.Button("Удалить физичный граф", GUILayout.Height(26f)))
        {
            Undo.RecordObject(setup, "Удалить физичный граф");
            setup.DestroyPhysicalGraph();
            EditorUtility.SetDirty(setup);
        }
    }
}

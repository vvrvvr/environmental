using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MinimapEdge))]
public class MinimapEdgeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
        {
            var edge = (MinimapEdge)target;
            var followProp = serializedObject.FindProperty("anchorsFollowLinkedNodes");

            if (GUILayout.Button("Связать"))
            {
                if (!MinimapEdgeLinkEditorUtility.ConnectAnchorsToNodesByCollider(edge, out var err))
                    EditorUtility.DisplayDialog("Связать", err, "OK");
                else
                    edge.RefreshLinePositions();
            }

            if (GUILayout.Button("Повесить якоря на ноды (ноль локально)"))
            {
                if (!MinimapEdgeLinkEditorUtility.BindAnchorsToFollowLinkedNodes(edge, out var err))
                    EditorUtility.DisplayDialog("Якоря на ноды", err, "OK");
            }

            using (new EditorGUI.DisabledScope(followProp != null && !followProp.boolValue))
            {
                if (GUILayout.Button("Отвязать якоря от следования за нодами"))
                {
                    Undo.RecordObject(edge, "Отвязать якоря от нод");
                    edge.SetAnchorsFollowLinkedNodes(false);
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}

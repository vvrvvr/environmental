using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MinimapEdge))]
public class MinimapEdgeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
        {
            if (GUILayout.Button("Связать"))
            {
                var edge = (MinimapEdge)target;
                if (!MinimapEdgeLinkEditorUtility.ConnectAnchorsToNodesByCollider(edge, out var err))
                    EditorUtility.DisplayDialog("Связать", err, "OK");
                else
                    edge.RefreshLinePositions();
            }

            if (GUILayout.Button("Повесить якоря на ноды (ноль локально)"))
            {
                var edge = (MinimapEdge)target;
                if (!MinimapEdgeLinkEditorUtility.ParentAnchorsUnderLinkedNodes(edge, out var err))
                    EditorUtility.DisplayDialog("Якоря на ноды", err, "OK");
                else
                    edge.RefreshLinePositions();
            }
        }
    }
}

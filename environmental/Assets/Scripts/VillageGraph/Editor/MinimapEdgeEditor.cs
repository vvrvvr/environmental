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
                ConnectByColliderOverlap();
        }
    }

    private void ConnectByColliderOverlap()
    {
        var edge = (MinimapEdge)target;
        serializedObject.Update();

        var pFrom = serializedObject.FindProperty("fromNode");
        var pTo = serializedObject.FindProperty("toNode");
        var pStart = serializedObject.FindProperty("startAnchor");
        var pEnd = serializedObject.FindProperty("endAnchor");

        var startAnchor = pStart.objectReferenceValue as Transform;
        var endAnchor = pEnd.objectReferenceValue as Transform;

        if (startAnchor == null || endAnchor == null)
        {
            EditorUtility.DisplayDialog("Связать", "Назначь Start Anchor и End Anchor.", "OK");
            return;
        }

        Node from = FindBestNodeForAnchor(startAnchor);
        Node to = FindBestNodeForAnchor(endAnchor);

        if (from == null)
        {
            EditorUtility.DisplayDialog(
                "Связать",
                "Не найдена нода: у Start Anchor (или детей) должен быть Collider, пересекающийся по bounds с коллайдером какой-либо ноды на сцене.",
                "OK");
            return;
        }

        if (to == null)
        {
            EditorUtility.DisplayDialog(
                "Связать",
                "Не найдена нода: у End Anchor (или детей) должен быть Collider, пересекающийся по bounds с коллайдером какой-либо ноды на сцене.",
                "OK");
            return;
        }

        if (from == to)
        {
            EditorUtility.DisplayDialog(
                "Связать",
                "Оба якоря попали в одну и ту же ноду. Разнеси якоря по разным нодам или проверь коллайдеры.",
                "OK");
            return;
        }

        Undo.RecordObject(edge, "Связать MinimapEdge");
        pFrom.objectReferenceValue = from;
        pTo.objectReferenceValue = to;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(edge);
    }

    private static bool AnyBoundsIntersect(Collider[] a, Collider[] b)
    {
        foreach (var ca in a)
        {
            if (ca == null)
                continue;
            foreach (var cb in b)
            {
                if (cb == null)
                    continue;
                if (ca.bounds.Intersects(cb.bounds))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Нода с коллайдером, пересекающимся с любым коллайдером под якорем; при нескольких — ближайшая по позиции transform ноды к якорю.
    /// </summary>
    private static Node FindBestNodeForAnchor(Transform anchorRoot)
    {
        if (anchorRoot == null)
            return null;

        Collider[] anchorColliders = anchorRoot.GetComponentsInChildren<Collider>(true);
        if (anchorColliders.Length == 0)
            return null;

        Node best = null;
        float bestSqr = float.MaxValue;
        Vector3 anchorPos = anchorRoot.position;

        var nodes = Object.FindObjectsOfType<Node>(true);
        foreach (var node in nodes)
        {
            if (node == null)
                continue;

            Collider[] nodeColliders = node.GetComponentsInChildren<Collider>(true);
            if (nodeColliders.Length == 0)
                continue;

            if (!AnyBoundsIntersect(anchorColliders, nodeColliders))
                continue;

            float sqr = (node.transform.position - anchorPos).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = node;
            }
        }

        return best;
    }
}

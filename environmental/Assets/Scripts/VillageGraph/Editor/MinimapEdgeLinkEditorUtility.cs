using UnityEditor;
using UnityEngine;

/// <summary>
/// Общая логика связывания якорей <see cref="MinimapEdge"/> с <see cref="Node"/> для инспекторов и реестра.
/// </summary>
public static class MinimapEdgeLinkEditorUtility
{
    public static bool ConnectAnchorsToNodesByCollider(MinimapEdge edge, out string errorMessage)
    {
        errorMessage = null;
        if (edge == null)
        {
            errorMessage = "Ребро не задано.";
            return false;
        }

        var so = new SerializedObject(edge);
        var pFrom = so.FindProperty("fromNode");
        var pTo = so.FindProperty("toNode");
        var pStart = so.FindProperty("startAnchor");
        var pEnd = so.FindProperty("endAnchor");

        if (pFrom == null || pTo == null || pStart == null || pEnd == null)
        {
            errorMessage = "Не найдены сериализованные поля ребра.";
            return false;
        }

        var startAnchor = pStart.objectReferenceValue as Transform;
        var endAnchor = pEnd.objectReferenceValue as Transform;

        if (startAnchor == null || endAnchor == null)
        {
            errorMessage = "Назначь Start Anchor и End Anchor.";
            return false;
        }

        Node from = FindBestNodeForAnchor(startAnchor);
        Node to = FindBestNodeForAnchor(endAnchor);

        if (from == null)
        {
            errorMessage =
                "Start Anchor: нет ноды с коллайдером, пересекающимся по bounds с коллайдером под якорем.";
            return false;
        }

        if (to == null)
        {
            errorMessage =
                "End Anchor: нет ноды с коллайдером, пересекающимся по bounds с коллайдером под якорем.";
            return false;
        }

        if (from == to)
        {
            errorMessage = "Оба якоря попали в одну ноду.";
            return false;
        }

        Undo.RecordObject(edge, "Связать MinimapEdge");
        pFrom.objectReferenceValue = from;
        pTo.objectReferenceValue = to;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(edge);
        return true;
    }

    /// <summary>
    /// Start Anchor → дочерний к <see cref="MinimapEdge.FromNode"/>, End Anchor → к <see cref="MinimapEdge.ToNode"/>, локально в нуле.
    /// </summary>
    public static bool ParentAnchorsUnderLinkedNodes(MinimapEdge edge, out string errorMessage)
    {
        errorMessage = null;
        if (edge == null)
        {
            errorMessage = "Ребро не задано.";
            return false;
        }

        Node from = edge.FromNode;
        Node to = edge.ToNode;
        Transform start = edge.StartAnchor;
        Transform end = edge.EndAnchor;

        if (from == null || to == null)
        {
            errorMessage = "Сначала назначь From / To (кнопка «Связать»).";
            return false;
        }

        if (start == null || end == null)
        {
            errorMessage = "Назначь Start Anchor и End Anchor.";
            return false;
        }

        if (start == end)
        {
            errorMessage = "Start и End якоря совпадают.";
            return false;
        }

        Undo.RecordObject(start, "Якорь ребра под ноду");
        Undo.RecordObject(end, "Якорь ребра под ноду");

        Undo.SetTransformParent(start, from.transform, "Start anchor → FromNode");
        start.localPosition = Vector3.zero;
        start.localRotation = Quaternion.identity;
        start.localScale = Vector3.one;

        Undo.SetTransformParent(end, to.transform, "End anchor → ToNode");
        end.localPosition = Vector3.zero;
        end.localRotation = Quaternion.identity;
        end.localScale = Vector3.one;

        EditorUtility.SetDirty(start);
        EditorUtility.SetDirty(end);
        EditorUtility.SetDirty(edge);
        return true;
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

using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum VillageGraphHingeAxisMode
{
    FromEdgeDirection,
    LocalOnFromNode,
}

/// <summary>
/// Первая итерация «физичного графа»: по рёбрам из <see cref="MinimapEdgeRegistry"/> на нодах-источниках
/// создаются <see cref="HingeJoint"/> (по одному на исходящее ребро со стартовым якорем), <c>connectedBody</c> — Rigidbody ноды на конце ребра;
/// при заданном префабе — инстанс коллайдера ребра как дочерний объект <see cref="MinimapEdge.ToNode"/> в середине между якорями.
/// </summary>
[DisallowMultipleComponent]
public class VillageGraphPhysicsSetup : MonoBehaviour
{
    [Tooltip("Реестр рёбер; если пусто — берётся компонент с этого же GameObject.")]
    [SerializeField]
    private MinimapEdgeRegistry edgeRegistry;

    [Header("Rigidbody")]
    [Tooltip("Если на ноде нет Rigidbody — добавить (иначе Joint не работает). Стартовая нода: kinematic; остальные: не kinematic, без гравитации.")]
    [SerializeField]
    private bool addRigidbodyIfMissing = true;

    [SerializeField, Min(0.001f)]
    private float addedRigidbodyMass = 1f;

    [Tooltip("Rigidbody.constraints: заморозить позицию по мировым осям (вращение не трогаем).")]
    [SerializeField]
    private bool rigidbodyFreezePositionX;

    [SerializeField]
    private bool rigidbodyFreezePositionY;

    [SerializeField]
    private bool rigidbodyFreezePositionZ;

    [Header("Hinge")]
    [Tooltip("Коллизия между сцеплёнными телами (обычно выкл. для карты).")]
    [SerializeField]
    private bool hingeEnableCollision;

    [Tooltip("Ось вращения: по направлению ребра (start→end) или заданная в локали ноды-источника.")]
    [SerializeField]
    private VillageGraphHingeAxisMode hingeAxisMode = VillageGraphHingeAxisMode.FromEdgeDirection;

    [Tooltip("Локальная ось шарнира на FromNode (нормализуется). Используется, если режим LocalOnFromNode.")]
    [SerializeField]
    private Vector3 hingeAxisLocal = new Vector3(0f, 0f, 1f);

    [SerializeField]
    private bool hingeUseSpring;

    [SerializeField]
    private float hingeSpring = 0f;

    [SerializeField]
    private float hingeDamper = 0f;

    [Tooltip("Целевой угол пружины в градусах (см. JointSpring.targetPosition).")]
    [SerializeField]
    private float hingeSpringTargetPosition;

    [SerializeField]
    private bool hingeUseLimits;

    [SerializeField]
    private JointLimits hingeLimits;

    [Header("Edge collider")]
    [Tooltip("Префаб коллайдера ребра: дочерний объект ToNode, центр — середина между якорями, локальная +Y вдоль ребра. После спавна scale.y подгоняется под целевую длину (см. отступ).")]
    [SerializeField]
    private GameObject edgeColliderPrefab;

    [Tooltip("Отступ вдоль ребра с каждого конца (мир). Целевая длина капсулы = расстояние между якорями − 2×отступ (например ребро 10 и отступ 1 → длина 8). Центр и ориентация — по полному ребру.")]
    [SerializeField, Min(0f)]
    private float edgeColliderLengthEndOffset;

    [Tooltip(
        "Во время фазы Appearing префаб коллайдера ребра дополнительно смещается вдоль ребра к началу (против конечного якоря) на долю длины ребра между якорями, затем за то же время, что и рост scale.y, возвращается к нулевому смещению. " +
        "0 — без сдвига; 100 — на полную длину ребра. Пример для сдвига на половину длины коллайдера в мире: 100 × (половина длины префаба после сборки / длина ребра между якорями).")]
    [SerializeField, Range(0f, 100f)]
    private float edgeColliderAppearShiftBackPercentOfEdgeLength = 50f;

    [Tooltip("Созданные в последней сборке шарниры (для кнопки «Удалить»).")]
    [SerializeField]
    private List<HingeJoint> generatedHinges = new List<HingeJoint>();

    [SerializeField]
    private List<Rigidbody> generatedRigidbodies = new List<Rigidbody>();

    [Tooltip("Инстансы префаба коллайдера ребра (удаляются вместе с графом).")]
    [SerializeField]
    private List<GameObject> generatedEdgeColliderInstances = new List<GameObject>();

    public IReadOnlyList<HingeJoint> GeneratedHinges => generatedHinges;

    /// <summary>
    /// Собрать шарниры по текущему списку рёбер реестра. Сначала удаляет результат предыдущей сборки.
    /// </summary>
    public void BuildPhysicalGraph()
    {
        DestroyPhysicalGraph();

        var registry = ResolveRegistry();
        if (registry == null)
        {
            Debug.LogWarning($"{nameof(VillageGraphPhysicsSetup)}: нет {nameof(MinimapEdgeRegistry)}.", this);
            return;
        }

        var edges = registry.Edges;
        if (edges == null)
            return;

        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (edge == null)
                continue;

            var from = edge.FromNode;
            var to = edge.ToNode;
            var start = edge.StartAnchor;
            var end = edge.EndAnchor;
            if (from == null || to == null || start == null || end == null)
                continue;
            if (from == to)
                continue;

            var fromRb = EnsureRigidbodyOnNode(from);
            var toRb = EnsureRigidbodyOnNode(to);
            if (fromRb == null || toRb == null)
            {
                Debug.LogWarning(
                    $"{nameof(VillageGraphPhysicsSetup)}: пропуск ребра — нет Rigidbody на From/To ({edge.name}).",
                    edge);
                continue;
            }

            var hinge = AddHingeJoint(from.gameObject);

            // Auto Configure Connected Anchor пересчитывает привязку и в ряде случаев искажает axis в инспекторе / при первом шаге физики.
            hinge.autoConfigureConnectedAnchor = false;

            Transform fromRbT = fromRb.transform;
            hinge.anchor = fromRbT.InverseTransformPoint(start.position);

            var axisLocal = ResolveHingeAxisLocal(fromRbT, start, end);
            hinge.axis = axisLocal.sqrMagnitude > 1e-10f ? axisLocal.normalized : Vector3.up;

            hinge.connectedBody = toRb;
            hinge.connectedAnchor = toRb.transform.InverseTransformPoint(start.position);

            hinge.enableCollision = hingeEnableCollision;
            hinge.useSpring = hingeUseSpring;
            hinge.spring = new JointSpring
            {
                spring = hingeSpring,
                damper = hingeDamper,
                targetPosition = hingeSpringTargetPosition,
            };
            hinge.useLimits = hingeUseLimits;
            hinge.limits = hingeLimits;

            generatedHinges.Add(hinge);

            var colliderGo = InstantiateEdgeColliderUnderTo(edge, to, start.position, end.position);
            if (colliderGo != null)
                generatedEdgeColliderInstances.Add(colliderGo);
        }
    }

    /// <summary>
    /// Удалить добавленные шарниры, Rigidbody и инстансы коллайдера ребра, созданные этим компонентом при последней сборке.
    /// </summary>
    public void DestroyPhysicalGraph()
    {
        for (var i = 0; i < generatedEdgeColliderInstances.Count; i++)
        {
            var go = generatedEdgeColliderInstances[i];
            if (go != null)
                DestroyEdgeColliderInstanceSafe(go);
        }

        generatedEdgeColliderInstances.Clear();

        for (var i = 0; i < generatedHinges.Count; i++)
        {
            var h = generatedHinges[i];
            if (h != null)
                DestroyJointSafe(h);
        }

        generatedHinges.Clear();

        for (var i = 0; i < generatedRigidbodies.Count; i++)
        {
            var rb = generatedRigidbodies[i];
            if (rb != null)
                DestroyRigidbodySafe(rb);
        }

        generatedRigidbodies.Clear();
    }

    private void DestroyJointSafe(HingeJoint hinge)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.DestroyObjectImmediate(hinge);
            return;
        }
#endif
        Destroy(hinge);
    }

    private void DestroyRigidbodySafe(Rigidbody rb)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.DestroyObjectImmediate(rb);
            return;
        }
#endif
        Destroy(rb);
    }

    private void DestroyEdgeColliderInstanceSafe(GameObject go)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.DestroyObjectImmediate(go);
            return;
        }
#endif
        Destroy(go);
    }

    private GameObject InstantiateEdgeColliderUnderTo(MinimapEdge edge, Node to, Vector3 worldStart, Vector3 worldEnd)
    {
        if (edgeColliderPrefab == null || to == null || edge == null)
            return null;

        var anchorDistance = Vector3.Distance(worldStart, worldEnd);
        anchorDistance = Mathf.Max(anchorDistance, 1e-5f);
        var targetCapsuleLength = anchorDistance - 2f * edgeColliderLengthEndOffset;
        targetCapsuleLength = Mathf.Max(targetCapsuleLength, 1e-5f);

        var mid = (worldStart + worldEnd) * 0.5f;
        var rot = RotationWorldYAlongEdge(worldStart, worldEnd);

        GameObject go;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(edgeColliderPrefab);
            if (go == null)
                return null;
            Undo.RegisterCreatedObjectUndo(go, "Graph edge collider");
            go.transform.SetPositionAndRotation(mid, rot);
            go.transform.SetParent(to.transform, true);
        }
        else
#endif
        {
            go = Instantiate(edgeColliderPrefab, mid, rot, to.transform);
        }

        ScaleEdgeColliderRootYToTargetLength(go.transform, targetCapsuleLength);

        var driver = go.GetComponent<VillageGraphEdgeEndColliderDriver>();
        if (driver == null)
            driver = go.AddComponent<VillageGraphEdgeEndColliderDriver>();
        driver.Configure(edge, edgeColliderAppearShiftBackPercentOfEdgeLength);

        return go;
    }

    /// <summary>
    /// Инстанс уже повёрнут: <c>root.up</c> вдоль ребра. Меряем коллайдеры вдоль up, затем <c>localScale.y *= targetLength / текущаяДлина</c>.
    /// </summary>
    private static void ScaleEdgeColliderRootYToTargetLength(Transform root, float targetLengthAlongEdge)
    {
        if (root == null)
            return;

        var current = MeasureColliderWorldExtentAlongAxis(root, root.up);
        if (current < 1e-5f)
            current = 1f;

        var sc = root.localScale;
        sc.y *= targetLengthAlongEdge / current;
        root.localScale = sc;
    }

    private static float MeasureColliderWorldExtentAlongAxis(Transform root, Vector3 axisWorld)
    {
        var cols = root.GetComponentsInChildren<Collider>(true);
        if (cols == null || cols.Length == 0)
            return 0f;

        var w = cols[0].bounds;
        for (var i = 1; i < cols.Length; i++)
            w.Encapsulate(cols[i].bounds);

        var up = axisWorld.normalized;
        var c = w.center;
        var e = w.extents;
        var minP = float.MaxValue;
        var maxP = float.MinValue;
        for (var dx = -1; dx <= 1; dx += 2)
        for (var dy = -1; dy <= 1; dy += 2)
        for (var dz = -1; dz <= 1; dz += 2)
        {
            var corner = c + new Vector3(dx * e.x, dy * e.y, dz * e.z);
            var p = Vector3.Dot(corner, up);
            if (p < minP)
                minP = p;
            if (p > maxP)
                maxP = p;
        }

        return Mathf.Max(0f, maxP - minP);
    }

    private static Quaternion RotationWorldYAlongEdge(Vector3 worldStart, Vector3 worldEnd)
    {
        var dir = worldEnd - worldStart;
        if (dir.sqrMagnitude < 1e-10f)
            return Quaternion.identity;
        return Quaternion.FromToRotation(Vector3.up, dir.normalized);
    }

    private static HingeJoint AddHingeJoint(GameObject host)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return Undo.AddComponent<HingeJoint>(host);
#endif
        return host.AddComponent<HingeJoint>();
    }

    private Rigidbody AddRigidbodyToNode(Node node)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return Undo.AddComponent<Rigidbody>(node.gameObject);
#endif
        return node.gameObject.AddComponent<Rigidbody>();
    }

    private MinimapEdgeRegistry ResolveRegistry()
    {
        if (edgeRegistry != null)
            return edgeRegistry;
        return GetComponent<MinimapEdgeRegistry>();
    }

    private Rigidbody EnsureRigidbodyOnNode(Node node)
    {
        if (node == null)
            return null;

        var rb = node.GetComponent<Rigidbody>();
        if (rb != null)
        {
            ApplyRigidbodyPhysicsPolicy(node, rb);
            return rb;
        }

        if (!addRigidbodyIfMissing)
            return null;

        rb = AddRigidbodyToNode(node);
        rb.mass = addedRigidbodyMass;
        generatedRigidbodies.Add(rb);
        ApplyRigidbodyPhysicsPolicy(node, rb);
        return rb;
    }

    private Vector3 ResolveHingeAxisLocal(Transform fromT, Transform start, Transform end)
    {
        if (fromT == null)
            return Vector3.forward;

        switch (hingeAxisMode)
        {
            case VillageGraphHingeAxisMode.LocalOnFromNode:
            {
                var ax = hingeAxisLocal.sqrMagnitude > 1e-8f ? hingeAxisLocal.normalized : Vector3.forward;
                return ax;
            }
            default:
            {
                var worldDir = end.position - start.position;
                if (worldDir.sqrMagnitude < 1e-8f)
                    worldDir = fromT.forward;
                return fromT.InverseTransformDirection(worldDir.normalized);
            }
        }
    }

    private void ApplyRigidbodyPhysicsPolicy(Node node, Rigidbody rb)
    {
        if (node == null || rb == null)
            return;

        if (node.IsMinimapStartNode)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        else
        {
            rb.isKinematic = false;
            rb.useGravity = false;
        }

        const RigidbodyConstraints positionMask =
            RigidbodyConstraints.FreezePositionX |
            RigidbodyConstraints.FreezePositionY |
            RigidbodyConstraints.FreezePositionZ;

        var c = rb.constraints;
        c &= ~positionMask;
        if (rigidbodyFreezePositionX)
            c |= RigidbodyConstraints.FreezePositionX;
        if (rigidbodyFreezePositionY)
            c |= RigidbodyConstraints.FreezePositionY;
        if (rigidbodyFreezePositionZ)
            c |= RigidbodyConstraints.FreezePositionZ;
        rb.constraints = c;
    }
}

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
/// создаются <see cref="HingeJoint"/> (по одному на исходящее ребро со стартовым якорем), <c>connectedBody</c> — Rigidbody ноды на конце ребра.
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

    [Tooltip("Созданные в последней сборке шарниры (для кнопки «Удалить»).")]
    [SerializeField]
    private List<HingeJoint> generatedHinges = new List<HingeJoint>();

    [SerializeField]
    private List<Rigidbody> generatedRigidbodies = new List<Rigidbody>();

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
        }
    }

    /// <summary>
    /// Удалить добавленные шарниры и Rigidbody, созданные этим компонентом при последней сборке.
    /// </summary>
    public void DestroyPhysicalGraph()
    {
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

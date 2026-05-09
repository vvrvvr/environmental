using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Притягивает ноды графа к точке под курсором в пределах префаба-триггера.
/// Вешается на тот же объект, что <see cref="MinimapEdgeRegistry"/> / <see cref="VillageGraphPhysicsSetup"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class VillageGraphMouseNodeAttraction : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Пусто — GameManager.MapCamera.")]
    [SerializeField]
    private Camera mapCameraOverride;

    [Header("Зона (префаб)")]
    [Tooltip("Префаб: Collider isTrigger, Rigidbody kinematic, компонент VillageGraphAttractionTriggerVolume.")]
    [SerializeField]
    private GameObject attractionZonePrefab;

    [Tooltip("Z плоскости следования (мир): луч из камеры в эту Z кладёт центр зоны.")]
    [SerializeField]
    private float followPlaneWorldZ;

    [Header("Фильтры нод")]
    [SerializeField]
    private bool attractBlockedNodes;

    [SerializeField]
    private bool attractSelectedNodes = true;

    [Tooltip("Не трогать ноды в Inactive (и прочие неигровые состояния карты).")]
    [SerializeField]
    private bool skipInactiveNodes = true;

    [Header("Сила")]
    [Tooltip("Пружина: сила ~ stiffness * расстояние до центра зоны (XY), до потолка.")]
    [SerializeField, Min(0f)]
    private float stiffness = 12f;

    [SerializeField, Min(0f)]
    private float maxForce = 40f;

    [Tooltip("Ниже этого расстояния (XY) силу не добавляем — меньше дрожи у цели.")]
    [SerializeField, Min(0f)]
    private float deadZoneWorld = 0.02f;

    [Tooltip("Гасит горизонтальную скорость (XY), меньше качки на пружинах.")]
    [SerializeField, Min(0f)]
    private float velocityDamping = 2.5f;

    [Header("Toggle")]
    [SerializeField]
    private bool enableAttraction = true;

    private Camera _cam;
    private Transform _zoneTransform;
    private VillageGraphAttractionTriggerVolume _zoneVolume;
    private readonly HashSet<Node> _nodesInZone = new HashSet<Node>();

    private void OnEnable()
    {
        TrySpawnZone();
    }

    private void OnDisable()
    {
        DestroyZoneInstance();
    }

    private void LateUpdate()
    {
        if (!enableAttraction || !Application.isPlaying || _zoneTransform == null)
            return;

        ResolveCam();
        if (_cam == null)
            return;

        var ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (!TryRayIntersectWorldXYPlaneAtZ(ray, followPlaneWorldZ, out var hit))
            return;

        var p = _zoneTransform.position;
        p.x = hit.x;
        p.y = hit.y;
        p.z = followPlaneWorldZ;
        _zoneTransform.position = p;
    }

    private void FixedUpdate()
    {
        if (!enableAttraction || !Application.isPlaying || _zoneVolume == null)
            return;

        _zoneVolume.GatherOverlappingNodes(_nodesInZone, clearInto: true);
        var target = _zoneTransform.position;

        foreach (var node in _nodesInZone)
        {
            if (node == null)
                continue;
            if (skipInactiveNodes && node.CurrentState == NodeMapState.Inactive)
                continue;
            if (!attractBlockedNodes && node.CurrentState == NodeMapState.Blocked)
                continue;
            if (!attractSelectedNodes && node.IsSelected)
                continue;

            var rb = node.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic)
                continue;

            var pos = rb.worldCenterOfMass;
            var delta = new Vector3(target.x - pos.x, target.y - pos.y, 0f);
            var dist = delta.magnitude;
            if (dist < deadZoneWorld)
                continue;

            var dir = delta / dist;
            var f = Mathf.Min(stiffness * dist, maxForce);
            rb.AddForce(dir * f, ForceMode.Force);

            if (velocityDamping > 0f)
            {
                var v = rb.velocity;
                var drag = new Vector3(v.x, v.y, 0f) * -velocityDamping;
                rb.AddForce(drag, ForceMode.Force);
            }
        }
    }

    private void TrySpawnZone()
    {
        DestroyZoneInstance();
        if (attractionZonePrefab == null)
            return;

        var inst = Instantiate(attractionZonePrefab, transform);
        inst.name = attractionZonePrefab.name + " (instance)";
        _zoneTransform = inst.transform;
        _zoneVolume = inst.GetComponent<VillageGraphAttractionTriggerVolume>();
        if (_zoneVolume == null)
        {
            Debug.LogWarning(
                $"{nameof(VillageGraphMouseNodeAttraction)}: на префабе зоны нет {nameof(VillageGraphAttractionTriggerVolume)}.",
                this);
        }

        var p = _zoneTransform.position;
        p.z = followPlaneWorldZ;
        _zoneTransform.position = p;

        var rbZone = inst.GetComponent<Rigidbody>();
        if (rbZone != null)
        {
            rbZone.isKinematic = true;
            rbZone.useGravity = false;
        }
    }

    private void DestroyZoneInstance()
    {
        if (_zoneTransform != null)
        {
            Destroy(_zoneTransform.gameObject);
            _zoneTransform = null;
        }

        _zoneVolume = null;
    }

    private void ResolveCam()
    {
        if (_cam != null)
            return;
        _cam = mapCameraOverride != null ? mapCameraOverride : GameManager.Instance != null ? GameManager.Instance.MapCamera : null;
    }

    private static bool TryRayIntersectWorldXYPlaneAtZ(Ray ray, float zPlane, out Vector3 hit)
    {
        var dz = ray.direction.z;
        if (Mathf.Abs(dz) < 1e-7f)
        {
            hit = default;
            return false;
        }

        var t = (zPlane - ray.origin.z) / dz;
        if (t < 0f)
        {
            hit = default;
            return false;
        }

        hit = ray.origin + ray.direction * t;
        hit.z = zPlane;
        return true;
    }
}

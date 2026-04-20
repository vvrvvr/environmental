using System.Collections.Generic;
using UnityEngine;

public class Node : MonoBehaviour
{
    [Tooltip("Камера, из которой строится луч к курсору (обязательно назначить для отладки наведения).")]
    [SerializeField] private Camera raycastCamera;

    private readonly HashSet<Collider> overlappingNodes = new HashSet<Collider>();
    private bool mouseOver;

    private void Update() => ProcessInput();

    private void ProcessInput()
    {
        bool over = IsMouseHoveringThisNode();
        if (over != mouseOver)
        {
            if (over)
                OnNodePointerEnter();
            else
                OnNodePointerExit();
        }
        mouseOver = over;
        if (over)
            OnNodePointerHold();
        if (over && Input.GetMouseButtonDown(0))
            OnNodePointerClick();

        if (!Input.GetKeyDown(KeyCode.Space))
            return;
        foreach (var c in overlappingNodes)
            if (c != null)
                Debug.Log("trigger");
    }

    protected virtual void OnNodePointerEnter()
    {
        Debug.Log("enter");
    }

    protected virtual void OnNodePointerHold()
    {
        Debug.Log("hold");
    }

    protected virtual void OnNodePointerExit()
    {
        Debug.Log("exit");
    }

    protected virtual void OnNodePointerClick()
    {
        Debug.Log("click");
    }

    private bool IsMouseHoveringThisNode()
    {
        if (raycastCamera == null)
            return false;

        Ray ray = raycastCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, ~0, QueryTriggerInteraction.Collide))
            return false;

        return hit.collider != null && hit.collider.GetComponentInParent<Node>() == this;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Node"))
            overlappingNodes.Add(other);
    }

    private void OnTriggerExit(Collider other)
    {
        overlappingNodes.Remove(other);
    }
}

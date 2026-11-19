using MoSimLib;
using UnityEngine;

/// <summary>
/// Keeps the XPBD net solver's GameObject at world origin so that mesh
/// vertices (stored in world space) render correctly without any additional
/// transform offset.
///
/// Supports both XPBDNetSolver and NetSolver — attach alongside
/// either component.
/// </summary>
[ExecuteAlways]
public class NetParentFollower : MonoBehaviour
{
    private bool _hasSolver;
    private bool _hasJobsSolver;

    void OnEnable()
    {
        _hasJobsSolver = GetComponent<NetSolver>() != null;

        if (!_hasSolver && !_hasJobsSolver)
            Debug.LogWarning(
                "[NetParentFollower] No XPBDNetSolver or NetSolver " +
                "found on this GameObject.", this);
                
        // Match initial setup state immediately on enable or asset reload
        UpdateTransformPosition();
    }

    void LateUpdate()
    {
        UpdateTransformPosition();
    }

    private void UpdateTransformPosition()
    {
        if (!_hasSolver && !_hasJobsSolver) return;
        if (transform.parent == null) return;

        // Force zero-origin alignment so world calculations map directly 1:1
        // to the dynamic working Vertex Array elements.
        transform.position   = Vector3.zero;
        transform.rotation   = Quaternion.identity;
        transform.localScale = Vector3.one;
    }
}
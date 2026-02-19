using UnityEngine;

public sealed class ConnectionNode : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private NodeKind _kind = NodeKind.FrameMount;
    [SerializeField] private NodeKind _compatibleWith = NodeKind.FrameMount;

    [Header("Facing Rules (V0)")]
    [SerializeField, Range(-1f, 1f)]
    private float _facingOtherDotThreshold = -0.9f; // must be ~opposite

    [SerializeField, Range(-1f, 1f)]
    private float _matchSurfaceNormalDotThreshold = 0.9f; // must match hit normal

    [Header("Debug Gizmo")]
    [SerializeField] private float _gizmoRadius = 0.08f;

    [Header("Runtime (read-only)")]
    [SerializeField] private Part _owner;
    [SerializeField] private bool _isOccupied;
    [SerializeField] private ConnectionNode _connectedTo;

    public Part Owner => _owner;
    public NodeKind Kind => _kind;
    public NodeKind CompatibleWith => _compatibleWith;
    public bool IsOccupied => _isOccupied;
    public ConnectionNode ConnectedTo => _connectedTo;

    // Outward direction of this node (treat transform.right as “face normal”)
    public Vector3 Outward => transform.right.normalized;

    private void Awake()
    {
        if (_owner == null)
            _owner = GetComponentInParent<Part>();

        if (_owner == null)
            Debug.LogError($"[{nameof(ConnectionNode)}] No parent Part found.", this);
    }

    public void Initialize(Part owner)
    {
        if (owner == null) return;
        if (_owner != null && _owner != owner) return;
        _owner = owner;
    }

    public bool CanConnectTo(ConnectionNode other)
    {
        if (other == null) return false;
        if (other == this) return false;

        if (_owner == null || other._owner == null) return false;
        if (_owner == other._owner) return false;

        if (_isOccupied || other._isOccupied) return false;

        if (!IsKindCompatible(_compatibleWith, other._kind)) return false;
        if (!IsKindCompatible(other._compatibleWith, _kind)) return false;
        if (!PartConnectionRules.IsConnectionAllowed(_owner.PartType, _kind, other._owner.PartType, other._kind)) return false;

        bool bypassFacingRule = _owner.PartType == PartType.Wheel || other._owner.PartType == PartType.Wheel;
        if (!bypassFacingRule)
        {
            // Must face each other (prevents “snap inside” from same-side)
            float facingDot = Vector3.Dot(Outward, other.Outward);
            if (facingDot > _facingOtherDotThreshold) return false;
        }

        return true;
    }

    // NEW: restrict snapping to nodes on the face the mouse is pointing at
    public bool MatchesSurfaceNormal(Vector3 surfaceNormal)
    {
        float dot = Vector3.Dot(Outward, surfaceNormal.normalized);
        return dot >= _matchSurfaceNormalDotThreshold;
    }

    public void MarkConnected(ConnectionNode other)
    {
        if (!CanConnectTo(other)) return;

        _isOccupied = true;
        other._isOccupied = true;

        _connectedTo = other;
        other._connectedTo = this;
    }

    public void ClearConnection()
    {
        if (_connectedTo == null) return;
        var other = _connectedTo;

        _connectedTo = null;
        _isOccupied = false;

        if (other._connectedTo == this)
        {
            other._connectedTo = null;
            other._isOccupied = false;
        }
        else
        {
            Debug.LogWarning(
                $"[{nameof(ConnectionNode)}] ClearConnection found asymmetric state between '{name}' and '{other.name}'.",
                this
            );
        }
    }

    public Vector3 GetWorldPosition() => transform.position;
    public Quaternion GetWorldRotation() => transform.rotation;

    private static bool IsKindCompatible(NodeKind expected, NodeKind actual)
    {
        return expected == NodeKind.Generic
            || actual == NodeKind.Generic
            || expected == actual;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = _isOccupied ? Color.red : Color.green;
        Gizmos.DrawSphere(transform.position, _gizmoRadius);
        Gizmos.DrawLine(transform.position, transform.position + transform.right * (_gizmoRadius * 2f));
    }
#endif
}

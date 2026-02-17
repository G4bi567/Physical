using UnityEngine;

public class ConnectionNode : MonoBehaviour
{

    [Header("Config")]
    [SerializeField] private NodeKind _kind;
    [SerializeField] private NodeKind _compatibleWith;

    [Header("Runtime (read-only)")]
    [SerializeField] private Part _owner;
    [SerializeField] private bool _isOccupied;
    [SerializeField] private ConnectionNode _connectedTo;
    
    // Public read-only surface
    public Part Owner => _owner;
    public NodeKind Kind => _kind;
    public NodeKind CompatibleWith => _compatibleWith;
    public bool IsOccupied => _isOccupied;
    public ConnectionNode ConnectedTo => _connectedTo;

    private void Awake()
    {
        // Auto-cache owner from parent Part if not set.
        if (_owner == null)
            _owner = GetComponentInParent<Part>();

        if (_owner == null)
            Debug.LogError($"[{nameof(ConnectionNode)}] No parent Part found.", this);
    }

    public void Initialize(Part owner)
    {
        if (owner == null)
        {
            Debug.LogError($"[{nameof(ConnectionNode)}] Initialize called with null owner.", this);
            return;
        }

        // Guard: owner should not change.
        if (_owner != null && _owner != owner)
        {
            Debug.LogError($"[{nameof(ConnectionNode)}] Owner reassignment is not allowed.", this);
            return;
        }

        _owner = owner;
    }

    public bool CanConnectTo(ConnectionNode other)
    {
        if (other == null) return false;
        if (other == this) return false;

        if (_owner == null || other._owner == null) return false;
        if (_owner == other._owner) return false;

        if (_isOccupied || other._isOccupied) return false;

        // Simple V0 compatibility: kind must match each other's CompatibleWith.
        if (other._kind != _compatibleWith) return false;
        if (_kind != other._compatibleWith) return false;

        return true;
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

        // Clear this side
        _connectedTo = null;
        _isOccupied = false;

        // Clear other side (guard against already-cleared)
        if (other._connectedTo == this)
            other._connectedTo = null;

        other._isOccupied = false;
    }

    public Vector3 GetWorldPosition() => transform.position;
    public Quaternion GetWorldRotation() => transform.rotation;
    #if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Choose a color based on state.
        Gizmos.color = _isOccupied ? Color.red : Color.green;

        // Draw a small sphere at the node position.
        Gizmos.DrawSphere(transform.position, 0.5f);

        // Draw a short direction line so you can see orientation.
        Gizmos.DrawLine(transform.position, transform.position + transform.right * 0.15f);
    }
    #endif


}
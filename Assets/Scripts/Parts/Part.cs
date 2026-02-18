using UnityEngine;
using System.Collections.Generic;

public sealed class Part : MonoBehaviour
{
    private const float MinMass = 0.01f;
    private const float MaxMass = 10000f;
    private const float DefaultMass = 10f;

    [Header("Config")]
    [SerializeField] private PartType _partType = PartType.Frame;

    [Header("Runtime (read-only)")]
    [SerializeField] private Rigidbody _rb;
    [SerializeField] private bool _isFinalized;
    [SerializeField] private bool _isSimulationEnabled;
    [SerializeField] private string _runtimePrefabKey;

    private IReadOnlyList<ConnectionNode> _nodes;
    private WheelCollider[] _wheelColliders;
    private bool _isInitializedForPlacement;

    public PartType PartType => _partType;
    public float Mass => _rb != null ? _rb.mass : DefaultMass;
    public string RuntimePrefabKey => _runtimePrefabKey;

    public IReadOnlyList<ConnectionNode> GetNodes()
    {
        if (_nodes == null || HasNullNodeEntries(_nodes))
        {
            RefreshNodeCache();
        }

        return _nodes;
    }


    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
        {
            Debug.LogError("[Part] Missing Rigidbody on root. Part requires exactly one Rigidbody on the root GameObject.", this);
        }

        // Stronger invariant: exactly one Rigidbody in this part hierarchy.
        var rigidbodies = GetComponentsInChildren<Rigidbody>(includeInactive: true);
        if (rigidbodies == null || rigidbodies.Length == 0)
        {
            Debug.LogError("[Part] No Rigidbody found in hierarchy. Add one Rigidbody to root.", this);
        }
        else if (rigidbodies.Length > 1)
        {
            Debug.LogError($"[Part] Found {rigidbodies.Length} Rigidbodies in hierarchy. Expected exactly one on root.", this);
        }

        if (_rb != null && (_rb.transform != transform))
        {
            Debug.LogError("[Part] Rigidbody must be on the Part root GameObject.", this);
        }

        _wheelColliders = GetComponentsInChildren<WheelCollider>(includeInactive: true);
        ValidateRigidbodyMass();

        RefreshNodeCache();

        // Ensure nodes know their owner and report prefab miswiring.
        if (_nodes != null)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                ConnectionNode node = _nodes[i];
                if (node == null) continue;

                if (node.Owner != null && node.Owner != this)
                {
                    Debug.LogError($"[Part] Node '{node.name}' is owned by a different Part. Fix prefab hierarchy.", node);
                    continue;
                }

                node.Initialize(this);

                if (node.Owner != this)
                {
                    Debug.LogError($"[Part] Node '{node.name}' failed owner initialization.", node);
                }
            }
        }

        ValidateNodeKindsForPartType();
    }

    public void InitializeForPlacement()
    {
        if (_rb == null) return;
        if (_isInitializedForPlacement && !_isFinalized) return;

        _rb.isKinematic = true;
        _rb.detectCollisions = false;
        SetWheelCollidersEnabled(false);
        _isFinalized = false;
        _isSimulationEnabled = false;
        _isInitializedForPlacement = true;
    }

    public void FinalizePlacement(bool enableSimulation = true)
    {
        if (_rb == null) return;
        if (_isFinalized) return;
        if (!_isInitializedForPlacement)
        {
            Debug.LogWarning("[Part] FinalizePlacement called before InitializeForPlacement. Applying finalization anyway.", this);
        }

        _rb.detectCollisions = true;
        ValidateRigidbodyMass();
        SetSimulationEnabled(enableSimulation);

        _isFinalized = true;
        _isInitializedForPlacement = false;
    }

    public Rigidbody GetRigidbody()
    {
        return _rb;
    }

    public void SetSimulationEnabled(bool enabled)
    {
        if (_rb == null) return;
        if (!_isFinalized && enabled) return;

        _rb.isKinematic = !enabled;
        _rb.detectCollisions = true;
        SetWheelCollidersEnabled(enabled);
        _isSimulationEnabled = enabled;
    }

    public void SetRuntimePrefabKey(string prefabKey)
    {
        _runtimePrefabKey = prefabKey ?? string.Empty;
    }

    private void OnValidate()
    {
        ValidateRigidbodyMass();
        ValidateNodeKindsForPartType();
    }

    private void ValidateRigidbodyMass()
    {
        Rigidbody rb = _rb != null ? _rb : GetComponent<Rigidbody>();
        if (rb == null) return;

        _rb = rb;

        if (float.IsNaN(rb.mass) || float.IsInfinity(rb.mass))
        {
            rb.mass = DefaultMass;
            Debug.LogWarning($"[Part] Rigidbody mass was invalid (NaN/Infinity). Reset to {DefaultMass}.", this);
            return;
        }

        if (rb.mass < MinMass || rb.mass > MaxMass)
        {
            float clamped = Mathf.Clamp(rb.mass, MinMass, MaxMass);
            Debug.LogWarning($"[Part] Rigidbody mass {rb.mass} out of range [{MinMass}, {MaxMass}]. Clamped to {clamped}.", this);
            rb.mass = clamped;
        }
    }

    private void ValidateNodeKindsForPartType()
    {
        var nodes = GetComponentsInChildren<ConnectionNode>(includeInactive: true);
        if (nodes == null || nodes.Length == 0) return;

        for (int i = 0; i < nodes.Length; i++)
        {
            ConnectionNode node = nodes[i];
            if (node == null) continue;

            if (PartConnectionRules.IsNodeKindAllowedOnPart(_partType, node.Kind)) continue;

            Debug.LogWarning(
                $"[Part] Node '{node.name}' kind {node.Kind} is unusual for PartType {_partType}. " +
                "This may block snapping by design.",
                node
            );
        }
    }

    private void RefreshNodeCache()
    {
        var found = GetComponentsInChildren<ConnectionNode>(includeInactive: true);
        _nodes = found;

        if (_nodes == null || _nodes.Count == 0)
            Debug.LogWarning("[Part] No ConnectionNodes found under this Part.", this);
    }

    private static bool HasNullNodeEntries(IReadOnlyList<ConnectionNode> nodes)
    {
        if (nodes == null) return true;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] == null) return true;
        }

        return false;
    }

    private void SetWheelCollidersEnabled(bool enabled)
    {
        if (_wheelColliders == null || _wheelColliders.Length == 0)
            _wheelColliders = GetComponentsInChildren<WheelCollider>(includeInactive: true);

        if (_wheelColliders == null || _wheelColliders.Length == 0)
            return;

        for (int i = 0; i < _wheelColliders.Length; i++)
        {
            WheelCollider wheelCollider = _wheelColliders[i];
            if (wheelCollider == null) continue;
            wheelCollider.enabled = enabled;
        }
    }
}

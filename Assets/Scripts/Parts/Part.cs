using UnityEngine;
using System.Collections.Generic;

public sealed class Part : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private PartType _partType = PartType.Frame;
    [SerializeField] private float _mass = 10f;

    [Header("Runtime (read-only)")]
    [SerializeField] private Rigidbody _rb;
    [SerializeField] private bool _isFinalized;
    [SerializeField] private bool _isSimulationEnabled;
    [SerializeField] private string _runtimePrefabKey;

    private IReadOnlyList<ConnectionNode> _nodes;
    private bool _isInitializedForPlacement;

    public PartType PartType => _partType;
    public float Mass => _mass;
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

        ValidateMass();

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
    }

    public void InitializeForPlacement()
    {
        if (_rb == null) return;
        if (_isInitializedForPlacement && !_isFinalized) return;

        _rb.isKinematic = true;
        _rb.detectCollisions = false;
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
        _rb.mass = GetClampedMass();
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
        _isSimulationEnabled = enabled;
    }

    public void SetRuntimePrefabKey(string prefabKey)
    {
        _runtimePrefabKey = prefabKey ?? string.Empty;
    }

    private void OnValidate()
    {
        ValidateMass();
    }

    private void ValidateMass()
    {
        const float minMass = 0.01f;
        const float maxMass = 10000f;

        if (float.IsNaN(_mass) || float.IsInfinity(_mass))
        {
            _mass = 10f;
            Debug.LogWarning("[Part] Mass was invalid (NaN/Infinity). Reset to 10.", this);
            return;
        }

        if (_mass < minMass || _mass > maxMass)
        {
            float clamped = Mathf.Clamp(_mass, minMass, maxMass);
            Debug.LogWarning($"[Part] Mass {_mass} out of range [{minMass}, {maxMass}]. Clamped to {clamped}.", this);
            _mass = clamped;
        }
    }

    private float GetClampedMass()
    {
        const float minMass = 0.01f;
        const float maxMass = 10000f;
        return Mathf.Clamp(_mass, minMass, maxMass);
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
}

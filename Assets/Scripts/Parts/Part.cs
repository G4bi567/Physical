// Assets/Scripts/Parts/Part.cs
using System.Collections.Generic;
using UnityEngine;

public sealed class Part : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private PartType _partType;

    [Header("Physics")]
    [Min(0.001f)]
    [SerializeField] private float _mass = 1f;

    [Header("References (optional)")]
    [Tooltip("If left empty, will auto-find Rigidbody on this GameObject.")]
    [SerializeField] private Rigidbody _rb;

    // Public “surface API”
    public PartType PartType => _partType;
    public float Mass => _mass;

    private IReadOnlyList<ConnectionNode> _nodes;
    private bool _isFinalized;

    private void Awake()
    {
        CacheRigidbodyOrError();
        CacheNodes();
    }

    public IReadOnlyList<ConnectionNode> GetNodes()
    {
        if (_nodes == null)
        {
            // In case someone calls before Awake (script execution order / disabled object)
            CacheNodes();
        }
        return _nodes;
    }

    public void InitializeForPlacement()
    {
        if (_isFinalized) return;
        if (!CacheRigidbodyOrError()) return;

        // Preview mode: safe to move around without physics interference.
        _rb.isKinematic = true;
        _rb.detectCollisions = false;
    }

    public void FinalizePlacement()
    {
        if (_isFinalized) return;
        if (!CacheRigidbodyOrError()) return;

        // Simulation mode.
        _rb.isKinematic = false;
        _rb.detectCollisions = true;

        // Apply tuning.
        _rb.mass = Mathf.Max(0.001f, _mass);

        _isFinalized = true;
    }

    public Rigidbody GetRigidbody()
    {
        CacheRigidbodyOrError();
        return _rb;
    }

    private bool CacheRigidbodyOrError()
    {
        if (_rb != null) return true;

        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
        {
            Debug.LogError($"[{nameof(Part)}] Missing Rigidbody on Part prefab.", this);
            return false;
        }

        return true;
    }

    private void CacheNodes()
    {
        // Auto-discover nodes on children (including inactive) for prefab safety.
        var found = GetComponentsInChildren<ConnectionNode>(includeInactive: true);
        _nodes = found;

        if (found == null || found.Length == 0)
        {
            Debug.LogWarning($"[{nameof(Part)}] No ConnectionNode children found.", this);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Keep references sane while editing prefabs.
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_mass < 0.001f) _mass = 0.001f;
    }
#endif
}

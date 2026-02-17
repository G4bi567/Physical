using UnityEngine;
using UnityEngine.InputSystem;
using System;

public sealed class PlacementPreview : MonoBehaviour
{
    public event Action<Part> PartPlaced;

    [Header("Scene References")]
    [SerializeField] private Camera _camera;
    [SerializeField] private SnapSystem _snapSystem;

    [Header("Surface Raycast")]
    [Tooltip("Layers the mouse can hit for placement: Ground + Parts.")]
    [SerializeField] private LayerMask _surfaceLayer;

    [SerializeField] private float _maxRayDistance = 200f;
    [SerializeField] private bool _requireSnapForPlacement = true;

    [Header("Preview Rotation")]
    [SerializeField, Min(1f)] private float _rotationStepDegrees = 15f;
    [SerializeField] private Key _rotateLeftKey = Key.Z;
    [SerializeField] private Key _rotateRightKey = Key.X;

    [Header("Placement Validation")]
    [Tooltip("Layer containing placed parts colliders. Must NOT include Ground.")]
    [SerializeField] private LayerMask _partLayer;

    [Tooltip("Shrink overlap bounds to allow touching. Start: 0.01")]
    [SerializeField] private float _overlapShrink = 0.01f;

    [Header("Joint Stability")]
    [SerializeField] private bool _autoConfigureConnectedAnchor = false;
    [SerializeField] private bool _enablePreprocessing = false;
    [SerializeField] private bool _enableCollisionBetweenConnectedBodies = false;
    [SerializeField] private float _jointBreakForce = Mathf.Infinity;
    [SerializeField] private float _jointBreakTorque = Mathf.Infinity;
    [SerializeField, Min(0.01f)] private float _jointMassScale = 1f;
    [SerializeField, Min(0.01f)] private float _jointConnectedMassScale = 1f;
    [SerializeField] private bool _autoConnectAdditionalNodes = true;
    [SerializeField] private bool _debugBuildFlow = true;

    // Runtime
    private GameObject _activePreviewPrefab;
    private Part _currentPreviewPart;
    private Collider[] _previewColliders;
    private SnapSystem.SnapResult _lastSnap;
    private bool _isPlacementValid;
    private bool _isCancellingPreview;
    private float _previewYawDegrees;

    private readonly Collider[] _overlapBuffer = new Collider[64];

    public void BeginPreview(GameObject partPrefab)
    {
        if (partPrefab == null)
        {
            Debug.LogError("[PlacementPreview] BeginPreview called with null prefab.", this);
            return;
        }

        if (_debugBuildFlow)
            Debug.Log($"[PlacementPreview] BeginPreview -> {partPrefab.name}", this);

        bool isSamePrefabSelection = _activePreviewPrefab == partPrefab;
        _activePreviewPrefab = partPrefab;
        if (!isSamePrefabSelection)
            _previewYawDegrees = 0f;

        CancelPreview();
        bool spawned = TrySpawnPreview(partPrefab);

        if (_debugBuildFlow)
            Debug.Log($"[PlacementPreview] BeginPreview spawned={spawned}", this);
    }

    private bool TrySpawnPreview(GameObject partPrefab)
    {
        if (_debugBuildFlow)
            Debug.Log($"[PlacementPreview] TrySpawnPreview -> {partPrefab.name}", this);

        GameObject go = Instantiate(partPrefab);

        _currentPreviewPart = go.GetComponent<Part>();
        if (_currentPreviewPart == null)
        {
            Debug.LogError("[PlacementPreview] Prefab has no Part component on root. Add Part to the prefab root.", go);
            Destroy(go);
            return false;
        }
        _currentPreviewPart.SetRuntimePrefabKey(partPrefab.name);

        _currentPreviewPart.InitializeForPlacement();
        _previewColliders = _currentPreviewPart.GetComponentsInChildren<Collider>(includeInactive: false);
        _currentPreviewPart.transform.rotation = Quaternion.Euler(0f, _previewYawDegrees, 0f);

        _lastSnap = default;
        _isPlacementValid = false;

        if (_debugBuildFlow)
            Debug.Log($"[PlacementPreview] Preview ready: {_currentPreviewPart.name}", this);

        return true;
    }

    public void CancelPreview()
    {
        if (_isCancellingPreview) return;
        _isCancellingPreview = true;

        if (_currentPreviewPart != null)
        {
            if (_debugBuildFlow)
                Debug.Log($"[PlacementPreview] CancelPreview destroying {_currentPreviewPart.name}", this);

            GameObject previewGo = _currentPreviewPart.gameObject;
            _currentPreviewPart = null;

            if (previewGo != null)
                Destroy(previewGo);
        }

        _previewColliders = null;
        _lastSnap = default;
        _isPlacementValid = false;
        _isCancellingPreview = false;
    }

    private void Update()
    {
        if (_currentPreviewPart == null) return;
        if (Mouse.current == null) return;

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelPreview();
            return;
        }

        ApplyRotationInput();
        _currentPreviewPart.transform.rotation = Quaternion.Euler(0f, _previewYawDegrees, 0f);

        if (!TryGetMouseSurfaceHit(out RaycastHit hit))
        {
            _isPlacementValid = false;
            return;
        }

        // Desired point is the surface point the mouse is on (prevents “behind” placement)
        Vector3 desiredPoint = hit.point;

        // If the mouse is hitting a part, prefer snapping to THAT part face
        Part hitPart = hit.collider != null ? hit.collider.GetComponentInParent<Part>() : null;
        Vector3 hitNormal = hit.normal;

        _lastSnap = (_snapSystem != null)
            ? _snapSystem.FindBestSnap(_currentPreviewPart, desiredPoint, hitPart, hitNormal)
            : default;

        Vector3 targetPos = _lastSnap.IsValid ? _lastSnap.SnappedWorldPosition : desiredPoint;
        _currentPreviewPart.transform.position = targetPos;

        _isPlacementValid = CheckPlacementValid(_currentPreviewPart);
        if (_requireSnapForPlacement && !_lastSnap.IsValid)
            _isPlacementValid = false;

        if (Mouse.current.leftButton.wasPressedThisFrame && _isPlacementValid)
        {
            PlaceCurrent();
        }
    }

    private bool TryGetMouseSurfaceHit(out RaycastHit hit)
    {
        hit = default;
        if (_camera == null) return false;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(mousePos);

        return Physics.Raycast(ray, out hit, _maxRayDistance, _surfaceLayer, QueryTriggerInteraction.Ignore);
    }

    private bool CheckPlacementValid(Part previewPart)
    {
        Collider[] previewColliders = _previewColliders;
        if (previewColliders == null || previewColliders.Length == 0 || !HasLiveCollider(previewColliders))
        {
            previewColliders = previewPart.GetComponentsInChildren<Collider>(includeInactive: false);
            _previewColliders = previewColliders;
        }

        if (previewColliders == null || previewColliders.Length == 0) return false;

        for (int i = 0; i < previewColliders.Length; i++)
        {
            Collider c = previewColliders[i];
            if (c == null) continue;

            Bounds b = c.bounds;

            Vector3 halfExtents = b.extents - Vector3.one * _overlapShrink;
            halfExtents = Vector3.Max(halfExtents, Vector3.zero);

            int count = Physics.OverlapBoxNonAlloc(
                b.center,
                halfExtents,
                _overlapBuffer,
                Quaternion.identity,
                _partLayer,
                QueryTriggerInteraction.Ignore
            );

            for (int k = 0; k < count; k++)
            {
                Collider other = _overlapBuffer[k];
                if (other == null) continue;

                // ignore our own colliders
                if (other.transform.IsChildOf(previewPart.transform)) continue;

                return false;
            }
        }

        return true;
    }

    private static bool HasLiveCollider(Collider[] colliders)
    {
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) return true;
        }

        return false;
    }

    private void ApplyRotationInput()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[_rotateLeftKey].wasPressedThisFrame)
            _previewYawDegrees -= _rotationStepDegrees;

        if (Keyboard.current[_rotateRightKey].wasPressedThisFrame)
            _previewYawDegrees += _rotationStepDegrees;
    }

    private void PlaceCurrent()
    {
        if (_currentPreviewPart == null)
        {
            if (_debugBuildFlow)
                Debug.LogWarning("[PlacementPreview] PlaceCurrent called with null _currentPreviewPart.", this);
            return;
        }

        GameObject placedPrefab = _activePreviewPrefab;

        if (_debugBuildFlow)
        {
            string prefabName = placedPrefab != null ? placedPrefab.name : "NULL";
            Debug.Log($"[PlacementPreview] PlaceCurrent placed={_currentPreviewPart.name} nextPrefab={prefabName}", this);
        }

        // Build phase: place as finalized but still kinematic until simulation starts.
        _currentPreviewPart.FinalizePlacement(enableSimulation: false);

        Part placedPart = _currentPreviewPart;

        if (_lastSnap.IsValid && _lastSnap.PreviewNode != null && _lastSnap.TargetNode != null)
        {
            TryCreateNodeJoint(_lastSnap.PreviewNode, _lastSnap.TargetNode);
        }

        if (_autoConnectAdditionalNodes && _currentPreviewPart != null)
        {
            AutoConnectRemainingNodes(_currentPreviewPart);
        }

        if (placedPart != null)
            PartPlaced?.Invoke(placedPart);

        // V0: stop after one placement
        _currentPreviewPart = null;
        _lastSnap = default;
        _isPlacementValid = false;

        if (placedPrefab != null)
        {
            bool spawned = TrySpawnPreview(placedPrefab);
            if (_debugBuildFlow)
                Debug.Log($"[PlacementPreview] Continuous spawn result={spawned}", this);
        }
        else if (_debugBuildFlow)
        {
            Debug.LogWarning("[PlacementPreview] Continuous spawn skipped: _activePreviewPrefab is null.", this);
        }
    }

    private void OnGUI()
    {
        if (_currentPreviewPart == null) return;
        GUI.Label(new Rect(10, 10, 300, 30), _isPlacementValid ? "PLACEMENT: VALID" : "PLACEMENT: INVALID");
    }

    private bool TryCreateNodeJoint(ConnectionNode previewNode, ConnectionNode targetNode)
    {
        if (previewNode == null || targetNode == null) return false;
        if (!previewNode.CanConnectTo(targetNode)) return false;

        Rigidbody a = previewNode.Owner != null ? previewNode.Owner.GetRigidbody() : null;
        Rigidbody b = targetNode.Owner != null ? targetNode.Owner.GetRigidbody() : null;
        if (a == null || b == null) return false;

        FixedJoint joint = a.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = b;

        Vector3 previewNodeWorld = previewNode.GetWorldPosition();
        Vector3 targetNodeWorld = targetNode.GetWorldPosition();

        // Define both anchors explicitly to reduce initial solver error.
        joint.autoConfigureConnectedAnchor = _autoConfigureConnectedAnchor;
        joint.anchor = a.transform.InverseTransformPoint(previewNodeWorld);
        if (!_autoConfigureConnectedAnchor)
        {
            joint.connectedAnchor = b.transform.InverseTransformPoint(targetNodeWorld);
        }

        joint.enablePreprocessing = _enablePreprocessing;
        joint.enableCollision = _enableCollisionBetweenConnectedBodies;
        joint.breakForce = _jointBreakForce;
        joint.breakTorque = _jointBreakTorque;
        joint.massScale = _jointMassScale;
        joint.connectedMassScale = _jointConnectedMassScale;

        previewNode.MarkConnected(targetNode);
        return true;
    }

    private void AutoConnectRemainingNodes(Part placedPart)
    {
        float secondarySnapRadius = (_snapSystem != null && _snapSystem.SnapRadius > 0f)
            ? _snapSystem.SnapRadius
            : 0.5f;

        var nodes = placedPart.GetNodes();
        if (nodes == null || nodes.Count == 0) return;

        for (int i = 0; i < nodes.Count; i++)
        {
            ConnectionNode previewNode = nodes[i];
            if (previewNode == null || previewNode.IsOccupied) continue;

            int hitCount = Physics.OverlapSphereNonAlloc(
                previewNode.GetWorldPosition(),
                secondarySnapRadius,
                _overlapBuffer,
                _partLayer,
                QueryTriggerInteraction.Ignore
            );

            ConnectionNode bestTarget = null;
            float bestDistance = float.PositiveInfinity;

            for (int h = 0; h < hitCount; h++)
            {
                Collider col = _overlapBuffer[h];
                if (col == null) continue;

                Part targetPart = col.GetComponentInParent<Part>();
                if (targetPart == null || targetPart == placedPart) continue;

                var targetNodes = targetPart.GetNodes();
                if (targetNodes == null || targetNodes.Count == 0) continue;

                for (int t = 0; t < targetNodes.Count; t++)
                {
                    ConnectionNode targetNode = targetNodes[t];
                    if (targetNode == null) continue;
                    if (!previewNode.CanConnectTo(targetNode)) continue;

                    float d = Vector3.Distance(previewNode.GetWorldPosition(), targetNode.GetWorldPosition());
                    if (d > secondarySnapRadius) continue;

                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        bestTarget = targetNode;
                    }
                }
            }

            if (bestTarget != null)
            {
                TryCreateNodeJoint(previewNode, bestTarget);
            }
        }
    }
}

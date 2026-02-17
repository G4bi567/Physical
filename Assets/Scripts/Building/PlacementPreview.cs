using UnityEngine;
using UnityEngine.InputSystem;

public class PlacementPreview : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Camera _camera;
    [SerializeField] private SnapSystem _snapSystem;

    [Header("Raycast Settings")]
    [SerializeField] private LayerMask _groundLayer;
    [SerializeField] private float _maxRayDistance = 200f;

    [Header("Placement Validation")]
    [Tooltip("Layer that contains already-placed parts (their colliders).")]
    [SerializeField] private LayerMask _partLayer;

    [Tooltip("Small extra margin for overlap checks. Helps avoid 'almost touching' false placements.")]
    [SerializeField] private float _overlapPadding = 0.01f;

    [Header("Input (V0)")]
    [Tooltip("Left click places, right click cancels.")]


    private Part _currentPreviewPart;
    private SnapSystem.SnapResult _lastSnap;
    private bool _isPlacementValid;

    // Reused buffer to avoid allocations each frame during overlap checks
    private readonly Collider[] _overlapBuffer = new Collider[64];

    /// <summary>
    /// Call this from UI (button) or from a BuildManager when the player chooses a part.
    /// </summary>
    public void BeginPreview(Part partPrefab)
    {
        if (partPrefab == null)
        {
            Debug.LogError("[PlacementPreview] BeginPreview called with null prefab.", this);
            return;
        }

        CancelPreview(); // remove any previous preview

        // Instantiate preview part
        _currentPreviewPart = Instantiate(partPrefab);

        // Put the preview into "safe to move" mode (your Part.cs already does this)
        _currentPreviewPart.InitializeForPlacement();

        // Reset snap and validity state
        _lastSnap = default;
        _isPlacementValid = false;
    }

    /// <summary>
    /// Cancels preview and clears state.
    /// </summary>
    public void CancelPreview()
    {
        if (_currentPreviewPart != null)
        {
            Destroy(_currentPreviewPart.gameObject);
            _currentPreviewPart = null;
        }

        _lastSnap = default;
        _isPlacementValid = false;
    }
    
    private void Update()
    {
        if (Mouse.current == null)
            return;
        // If we are not currently previewing anything, do nothing.
        if (_currentPreviewPart == null)
            return;

        // Right click cancels preview.
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelPreview();
            return;
        }

        // 1) Get desired placement point from mouse raycast
        if (!TryGetMouseWorldPoint(out Vector3 desiredPoint))
            return; // mouse not hitting ground

        // 2) Ask SnapSystem if we can snap near this desired point
        _lastSnap = _snapSystem != null
            ? _snapSystem.FindBestSnap(_currentPreviewPart, desiredPoint)
            : default;

        // 3) Move preview part: snapped position if valid, otherwise follow desired point
        Vector3 targetPos = _lastSnap.IsValid ? _lastSnap.SnappedWorldPosition : desiredPoint;
        _currentPreviewPart.transform.position = targetPos;

        // 4) Validate placement (prevents part-inside-part)
        _isPlacementValid = CheckPlacementValid(_currentPreviewPart);

        // 5) Place on left click (only if valid)
        if (Mouse.current.leftButton.wasPressedThisFrame && _isPlacementValid)
        {
            PlaceCurrent();
        }
    }

    
    /// <summary>
    /// Raycast from mouse cursor to ground to get world point.
    /// This is how we know where the player "wants" the part.
    /// </summary>
    private bool TryGetMouseWorldPoint(out Vector3 worldPoint)
    {
        worldPoint = default;

        if (_camera == null)
        {
            Debug.LogError("[PlacementPreview] No camera assigned.", this);
            return false;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out RaycastHit hit, _maxRayDistance, _groundLayer, QueryTriggerInteraction.Ignore))
        {
            worldPoint = hit.point;
            return true;
        }

        return false;
    }
    
    /// <summary>
    /// Returns false if the preview overlaps any collider on the part layer
    /// that does NOT belong to the preview itself.
    /// </summary>
    private bool CheckPlacementValid(Part previewPart)
    {
        if (previewPart == null) return false;

        // Collect all colliders on the preview part
        Collider[] previewColliders = previewPart.GetComponentsInChildren<Collider>(includeInactive: false);
        if (previewColliders == null || previewColliders.Length == 0)
        {
            // If the part has no colliders, we can't validate properly.
            // For V0: consider this invalid to avoid weird placements.
            return false;
        }

        for (int i = 0; i < previewColliders.Length; i++)
        {
            Collider c = previewColliders[i];
            if (c == null) continue;

            // We do an overlap query using the collider's bounds.
            // This is not perfect geometry-accurate, but it's robust for V0.
            Bounds b = c.bounds;

            Vector3 center = b.center;
            Vector3 halfExtents = b.extents + Vector3.one * _overlapPadding;

            int hitCount = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                _overlapBuffer,
                Quaternion.identity,  // bounds are axis-aligned in world space
                _partLayer,
                QueryTriggerInteraction.Ignore
            );

            for (int h = 0; h < hitCount; h++)
            {
                Collider hit = _overlapBuffer[h];
                if (hit == null) continue;

                // Ignore collisions with our own preview colliders
                if (hit.transform.IsChildOf(previewPart.transform))
                    continue;

                // We found overlap with some other placed part collider => invalid placement
                return false;
            }
        }

        return true;
    }
    /// <summary>
    /// Finalize the preview into a real physics part and connect joint if snapped.
    /// </summary>
    private void PlaceCurrent()
    {
        if (_currentPreviewPart == null)
            return;

        // Convert preview -> real physics object
        _currentPreviewPart.FinalizePlacement();

        // If we have a valid snap, create a FixedJoint
        if (_lastSnap.IsValid && _lastSnap.PreviewNode != null && _lastSnap.TargetNode != null)
        {
            Rigidbody previewRb = _lastSnap.PreviewNode.Owner != null ? _lastSnap.PreviewNode.Owner.GetRigidbody() : null;
            Rigidbody targetRb  = _lastSnap.TargetNode.Owner  != null ? _lastSnap.TargetNode.Owner.GetRigidbody()  : null;

            if (previewRb != null && targetRb != null)
            {
                // Create joint on preview part, connect to target part
                FixedJoint joint = previewRb.gameObject.AddComponent<FixedJoint>();
                joint.connectedBody = targetRb;

                // Optional: you can tune these later for stability
                // joint.breakForce = Mathf.Infinity;
                // joint.breakTorque = Mathf.Infinity;
                // joint.enableCollision = false;

                // Mark nodes as connected (updates occupancy + pairing)
                _lastSnap.PreviewNode.MarkConnected(_lastSnap.TargetNode);
            }
            else
            {
                Debug.LogWarning("[PlacementPreview] Snap valid but missing rigidbodies for joint creation.", this);
            }
        }

        // For V0: stop preview after placing once
        _currentPreviewPart = null;
        _lastSnap = default;
        _isPlacementValid = false;
    }

    // Optional: quick debug so you can see validity while playing.
    // You can remove this later.
    private void OnGUI()
    {
        if (_currentPreviewPart == null) return;

        string text = _isPlacementValid ? "PLACEMENT: VALID" : "PLACEMENT: INVALID";
        GUI.Label(new Rect(10, 10, 300, 30), text);
    }

}

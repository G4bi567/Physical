using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class PlacementPreview : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Camera _camera;
    [SerializeField] private SnapSystem _snapSystem;

    [Header("Raycast Settings")]
    [FormerlySerializedAs("_groundLayer")]
    [SerializeField] private LayerMask _surfaceLayer;
    [SerializeField] private float _maxRayDistance = 200f;

    [Header("Preview Rotation")]
    [SerializeField, Min(1f)] private float _rotationStepDegrees = 15f;
    [SerializeField] private Key _rotateLeftKey = Key.Z;
    [SerializeField] private Key _rotateRightKey = Key.X;

    [Header("Preview Visuals")]
    [SerializeField] private bool _showSnapIndicator = true;
    [SerializeField, Min(0.02f)] private float _snapIndicatorScale = 0.08f;
    [SerializeField] private Color _snapIndicatorColor = new Color(0.2f, 1f, 0.25f, 1f);
    [SerializeField] private Color _validPreviewColor = new Color(0.65f, 0.9f, 1f, 0.85f);
    [SerializeField] private Color _invalidPreviewColor = new Color(1f, 0.35f, 0.35f, 0.85f);

    [Header("Placement Validation")]
    [Tooltip("Layer that contains already-placed parts (their colliders).")]
    [SerializeField] private LayerMask _partLayer;

    [Tooltip("Small shrink amount for overlap checks. Allows flush face-to-face placement while still blocking real intersections.")]
    [SerializeField, Min(0f)] private float _overlapPadding = 0.01f;

    [Header("Joint Stability")]
    [SerializeField] private bool _autoConfigureConnectedAnchor = false;
    [SerializeField] private bool _enablePreprocessing = false;
    [SerializeField] private bool _enableCollisionBetweenConnectedBodies = false;
    [SerializeField] private float _jointBreakForce = Mathf.Infinity;
    [SerializeField] private float _jointBreakTorque = Mathf.Infinity;
    [SerializeField, Min(0.01f)] private float _jointMassScale = 1f;
    [SerializeField, Min(0.01f)] private float _jointConnectedMassScale = 1f;
    [SerializeField, Min(0.001f)] private float _secondarySnapDistance = 0.015f;
    [SerializeField, Min(0)] private int _maxSecondaryLoopJointsPerPart = 1;

    [Header("Mirror Rules")]
    [SerializeField] private bool _requireMirrorSnapConnection = true;
    [SerializeField, Min(0f)] private float _mirrorPlaneDeadZone = 0.02f;

    private Part _currentPreviewPart;
    private Part _activePreviewPrefab;
    private SnapSystem.SnapResult _lastSnap;
    private Part _mirrorPreviewPart;
    private SnapSystem.SnapResult _lastMirrorSnap;
    private bool _isPlacementValid;
    private bool _isMirrorPlacementValid;
    private float _previewYawDegrees;
    private bool _isMirrorModeEnabled;
    private Transform _mirrorReference;
    private Renderer[] _previewRenderers;
    private Renderer[] _mirrorPreviewRenderers;
    private Transform _snapIndicator;
    private Renderer _snapIndicatorRenderer;
    public event System.Action<Part> PartPlaced;

    // Reused buffer to avoid allocations each frame during overlap checks
    private readonly Collider[] _overlapBuffer = new Collider[64];
    private readonly RaycastHit[] _raycastBuffer = new RaycastHit[32];
    private readonly System.Collections.Generic.HashSet<ConnectionNode> _usedSecondaryTargets = new System.Collections.Generic.HashSet<ConnectionNode>();
    private MaterialPropertyBlock _previewPropertyBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        _previewPropertyBlock = new MaterialPropertyBlock();
    }

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

        bool isSamePrefab = _activePreviewPrefab == partPrefab;
        _activePreviewPrefab = partPrefab;
        if (!isSamePrefab)
            _previewYawDegrees = 0f;

        CancelPreview(clearActivePrefab: false);
        TrySpawnPreview(_activePreviewPrefab);
    }

    public void SetMirrorMode(bool enabled, Transform mirrorReference)
    {
        _isMirrorModeEnabled = enabled;
        _mirrorReference = mirrorReference;

        if (!_isMirrorModeEnabled)
        {
            DestroyMirrorPreview();
            return;
        }

        if (_currentPreviewPart != null && _activePreviewPrefab != null)
        {
            EnsureMirrorPreviewExists();
            UpdateMirrorPreviewFromPrimary();
        }
    }

    /// <summary>
    /// Cancels preview and clears state.
    /// </summary>
    public void CancelPreview()
    {
        CancelPreview(clearActivePrefab: true);
    }

    private void CancelPreview(bool clearActivePrefab)
    {
        if (_currentPreviewPart != null)
        {
            Destroy(_currentPreviewPart.gameObject);
            _currentPreviewPart = null;
        }

        DestroyMirrorPreview();

        if (clearActivePrefab)
            _activePreviewPrefab = null;

        _previewRenderers = null;
        _lastSnap = default;
        _isPlacementValid = false;
        SetSnapIndicatorVisible(false);
    }

    private bool TrySpawnPreview(Part partPrefab)
    {
        if (partPrefab == null)
            return false;

        _currentPreviewPart = Instantiate(partPrefab);
        if (_currentPreviewPart == null)
            return false;

        _currentPreviewPart.InitializeForPlacement();
        _currentPreviewPart.transform.rotation = Quaternion.Euler(0f, _previewYawDegrees, 0f);
        _previewRenderers = _currentPreviewPart.GetComponentsInChildren<Renderer>(includeInactive: false);
        _lastSnap = default;
        _isPlacementValid = false;
        ApplyPreviewTint(_invalidPreviewColor);

        if (_isMirrorModeEnabled)
        {
            EnsureMirrorPreviewExists();
            UpdateMirrorPreviewFromPrimary();
        }
        else
        {
            DestroyMirrorPreview();
        }

        SetSnapIndicatorVisible(false);
        return true;
    }

    private void EnsureMirrorPreviewExists()
    {
        if (!_isMirrorModeEnabled || _activePreviewPrefab == null)
        {
            DestroyMirrorPreview();
            return;
        }

        if (_mirrorPreviewPart != null)
            return;

        _mirrorPreviewPart = Instantiate(_activePreviewPrefab);
        if (_mirrorPreviewPart == null)
            return;

        _mirrorPreviewPart.InitializeForPlacement();
        _mirrorPreviewRenderers = _mirrorPreviewPart.GetComponentsInChildren<Renderer>(includeInactive: false);
        _lastMirrorSnap = default;
        _isMirrorPlacementValid = false;
        ApplyPreviewTint(_invalidPreviewColor, _mirrorPreviewRenderers);
        SetMirrorPreviewVisible(false);
    }

    private void DestroyMirrorPreview()
    {
        if (_mirrorPreviewPart != null)
        {
            Destroy(_mirrorPreviewPart.gameObject);
            _mirrorPreviewPart = null;
        }

        _mirrorPreviewRenderers = null;
        _lastMirrorSnap = default;
        _isMirrorPlacementValid = false;
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

        ApplyRotationInput();
        _currentPreviewPart.transform.rotation = Quaternion.Euler(0f, _previewYawDegrees, 0f);

        // Keep mirror preview hidden while solving the primary preview to avoid self-targeting.
        SetMirrorPreviewVisible(false);

        // 1) Get desired placement point and hovered surface from mouse raycast
        if (!TryGetMouseSurfaceHit(out RaycastHit surfaceHit))
        {
            _isPlacementValid = false;
            ApplyPreviewTint(_invalidPreviewColor);
            _isMirrorPlacementValid = false;
            SetMirrorPreviewVisible(false);
            SetSnapIndicatorVisible(false);
            return;
        }

        Vector3 desiredPoint = surfaceHit.point;
        Part preferredTargetPart = surfaceHit.collider != null
            ? surfaceHit.collider.GetComponentInParent<Part>()
            : null;
        Vector3 preferredSurfaceNormal = surfaceHit.normal;

        // 2) Ask SnapSystem for the best snap near this point, biased to hovered face/part.
        _lastSnap = _snapSystem != null
            ? _snapSystem.FindBestSnap(
                _currentPreviewPart,
                desiredPoint,
                preferredTargetPart,
                preferredSurfaceNormal)
            : default;

        // 3) Move preview part: snapped position if valid, otherwise follow desired point
        Vector3 targetPos = _lastSnap.IsValid ? _lastSnap.SnappedWorldPosition : desiredPoint;
        _currentPreviewPart.transform.position = targetPos;

        // 4) Validate placement (prevents part-inside-part)
        Transform primarySnapTargetRoot = GetSnapTargetRoot(_lastSnap);
        _isPlacementValid = CheckPlacementValid(
            _currentPreviewPart,
            _mirrorPreviewPart != null ? _mirrorPreviewPart.transform : null,
            primarySnapTargetRoot
        );
        ApplyPreviewTint(_isPlacementValid ? _validPreviewColor : _invalidPreviewColor);
        UpdateMirrorPreviewFromPrimary();
        UpdateSnapIndicator();

        // 5) Place on left click (only if valid)
        if (Mouse.current.leftButton.wasPressedThisFrame && _isPlacementValid)
        {
            PlaceCurrent();
        }
    }

    
    /// <summary>
    /// Raycast from mouse cursor to world surfaces (ground + parts), ignoring preview colliders.
    /// </summary>
    private bool TryGetMouseSurfaceHit(out RaycastHit bestHit)
    {
        bestHit = default;

        if (_camera == null)
        {
            Debug.LogError("[PlacementPreview] No camera assigned.", this);
            return false;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(mousePos);
        int surfaceMask = GetSurfaceRaycastMask();
        int hitCount = Physics.RaycastNonAlloc(ray, _raycastBuffer, _maxRayDistance, surfaceMask, QueryTriggerInteraction.Ignore);
        if (hitCount <= 0) return false;

        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _raycastBuffer[i];
            if (hit.collider == null) continue;

            if (_currentPreviewPart != null && hit.collider.transform.IsChildOf(_currentPreviewPart.transform))
                continue;
            if (_mirrorPreviewPart != null && hit.collider.transform.IsChildOf(_mirrorPreviewPart.transform))
                continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                bestHit = hit;
            }
        }

        return bestDistance < float.PositiveInfinity;
    }

    private int GetSurfaceRaycastMask()
    {
        int combined = _surfaceLayer.value | _partLayer.value;
        return combined != 0 ? combined : Physics.DefaultRaycastLayers;
    }

    private void UpdateMirrorPreviewFromPrimary()
    {
        if (!_isMirrorModeEnabled || _mirrorReference == null || _currentPreviewPart == null)
        {
            _isMirrorPlacementValid = false;
            _lastMirrorSnap = default;
            SetMirrorPreviewVisible(false);
            return;
        }

        EnsureMirrorPreviewExists();
        if (_mirrorPreviewPart == null)
        {
            _isMirrorPlacementValid = false;
            _lastMirrorSnap = default;
            return;
        }

        Vector3 planePoint = _mirrorReference.position;
        Vector3 planeNormal = _mirrorReference.right.normalized;
        if (planeNormal.sqrMagnitude <= 0.0001f)
        {
            _isMirrorPlacementValid = false;
            _lastMirrorSnap = default;
            SetMirrorPreviewVisible(false);
            return;
        }

        Vector3 sourcePos = _currentPreviewPart.transform.position;
        float distanceToPlane = Mathf.Abs(Vector3.Dot(sourcePos - planePoint, planeNormal));
        if (distanceToPlane <= _mirrorPlaneDeadZone)
        {
            _isMirrorPlacementValid = false;
            _lastMirrorSnap = default;
            SetMirrorPreviewVisible(false);
            return;
        }

        Vector3 mirroredPos = MirrorPointAcrossPlane(sourcePos, planePoint, planeNormal);
        Quaternion mirroredRot = MirrorRotationAcrossPlane(_currentPreviewPart.transform.rotation, planeNormal);
        _mirrorPreviewPart.gameObject.SetActive(true);
        _mirrorPreviewPart.transform.SetPositionAndRotation(mirroredPos, mirroredRot);

        bool primaryWasActive = _currentPreviewPart.gameObject.activeSelf;
        if (primaryWasActive)
            _currentPreviewPart.gameObject.SetActive(false);

        _lastMirrorSnap = _snapSystem != null
            ? _snapSystem.FindBestSnap(_mirrorPreviewPart, mirroredPos, null, default)
            : default;

        if (_lastMirrorSnap.IsValid)
            _mirrorPreviewPart.transform.position = _lastMirrorSnap.SnappedWorldPosition;

        Transform mirrorSnapTargetRoot = GetSnapTargetRoot(_lastMirrorSnap);
        _isMirrorPlacementValid = CheckPlacementValid(_mirrorPreviewPart, null, mirrorSnapTargetRoot);
        if (_requireMirrorSnapConnection && !_lastMirrorSnap.IsValid)
            _isMirrorPlacementValid = false;

        if (primaryWasActive)
            _currentPreviewPart.gameObject.SetActive(true);

        ApplyPreviewTint(_isMirrorPlacementValid ? _validPreviewColor : _invalidPreviewColor, _mirrorPreviewRenderers);
        SetMirrorPreviewVisible(true);
    }

    private void ApplyRotationInput()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[_rotateLeftKey].wasPressedThisFrame)
            _previewYawDegrees -= _rotationStepDegrees;

        if (Keyboard.current[_rotateRightKey].wasPressedThisFrame)
            _previewYawDegrees += _rotationStepDegrees;
    }

    private void ApplyPreviewTint(Color color)
    {
        ApplyPreviewTint(color, _previewRenderers);
    }

    private void ApplyPreviewTint(Color color, Renderer[] renderers)
    {
        if (renderers == null) return;
        if (_previewPropertyBlock == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;

            _previewPropertyBlock.Clear();
            _previewPropertyBlock.SetColor(BaseColorId, color);
            _previewPropertyBlock.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_previewPropertyBlock);
        }
    }

    private void ClearPreviewTint(Renderer[] renderers)
    {
        if (renderers == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            renderer.SetPropertyBlock(null);
        }
    }

    private void UpdateSnapIndicator()
    {
        bool shouldShow = _showSnapIndicator
            && _lastSnap.IsValid
            && _lastSnap.TargetNode != null;

        if (!shouldShow)
        {
            SetSnapIndicatorVisible(false);
            return;
        }

        EnsureSnapIndicatorExists();
        if (_snapIndicator == null) return;

        _snapIndicator.position = _lastSnap.TargetNode.GetWorldPosition();
        _snapIndicator.localScale = Vector3.one * _snapIndicatorScale;
        _snapIndicator.gameObject.SetActive(true);
    }

    private void EnsureSnapIndicatorExists()
    {
        if (_snapIndicator != null) return;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "SnapIndicator";
        go.transform.SetParent(transform, worldPositionStays: true);
        go.layer = gameObject.layer;

        Collider collider = go.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        _snapIndicator = go.transform;
        _snapIndicator.localScale = Vector3.one * _snapIndicatorScale;

        _snapIndicatorRenderer = go.GetComponent<Renderer>();
        if (_snapIndicatorRenderer != null)
        {
            Material indicatorMaterial = _snapIndicatorRenderer.material;
            indicatorMaterial.color = _snapIndicatorColor;
        }

        go.SetActive(false);
    }

    private void SetSnapIndicatorVisible(bool visible)
    {
        if (_snapIndicator == null) return;
        _snapIndicator.gameObject.SetActive(visible);
    }

    private void SetMirrorPreviewVisible(bool visible)
    {
        if (_mirrorPreviewPart == null) return;
        _mirrorPreviewPart.gameObject.SetActive(visible);
    }
    
    /// <summary>
    /// Returns false if the preview overlaps any collider on the part layer
    /// that does NOT belong to the preview itself.
    /// </summary>
    private bool CheckPlacementValid(
        Part previewPart,
        Transform additionalIgnoredRoot = null,
        Transform secondaryIgnoredRoot = null)
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

        int solidColliderCount = 0;
        bool isWheelPart = previewPart.PartType == PartType.Wheel;
        for (int i = 0; i < previewColliders.Length; i++)
        {
            Collider c = previewColliders[i];
            if (c == null) continue;
            if (!c.enabled) continue;
            if (c is WheelCollider) continue;
            if (c.isTrigger) continue;

            solidColliderCount++;

            // We do an overlap query using the collider's bounds.
            // This is not perfect geometry-accurate, but it's robust for V0.
            Bounds b = c.bounds;

            Vector3 center = b.center;
            // Shrink bounds a bit so perfectly flush faces are considered valid.
            Vector3 halfExtents = b.extents - Vector3.one * _overlapPadding;
            halfExtents = new Vector3(
                Mathf.Max(0.001f, halfExtents.x),
                Mathf.Max(0.001f, halfExtents.y),
                Mathf.Max(0.001f, halfExtents.z)
            );

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

                if (additionalIgnoredRoot != null && hit.transform.IsChildOf(additionalIgnoredRoot))
                    continue;
                if (secondaryIgnoredRoot != null && hit.transform.IsChildOf(secondaryIgnoredRoot))
                    continue;

                // We found overlap with some other placed part collider => invalid placement
                return false;
            }
        }

        if (solidColliderCount > 0)
            return true;

        // Keep wheel placement simple: wheel parts can be collider-light (WheelCollider only).
        // In that case allow placement and rely on snap + runtime physics for final behavior.
        return isWheelPart;
    }

    private static Transform GetSnapTargetRoot(SnapSystem.SnapResult snap)
    {
        if (!snap.IsValid || snap.TargetNode == null || snap.TargetNode.Owner == null)
            return null;

        return snap.TargetNode.Owner.transform;
    }

    private void ConfigurePlacedJoint(
        FixedJoint joint,
        Rigidbody previewRb,
        Rigidbody targetRb,
        ConnectionNode previewNode,
        ConnectionNode targetNode)
    {
        if (joint == null || previewRb == null || targetRb == null || previewNode == null || targetNode == null)
            return;

        joint.autoConfigureConnectedAnchor = _autoConfigureConnectedAnchor;
        joint.anchor = previewRb.transform.InverseTransformPoint(previewNode.GetWorldPosition());
        if (!_autoConfigureConnectedAnchor)
            joint.connectedAnchor = targetRb.transform.InverseTransformPoint(targetNode.GetWorldPosition());

        joint.enablePreprocessing = _enablePreprocessing;
        joint.enableCollision = _enableCollisionBetweenConnectedBodies;
        joint.breakForce = _jointBreakForce;
        joint.breakTorque = _jointBreakTorque;
        joint.massScale = Mathf.Max(0.01f, _jointMassScale);
        joint.connectedMassScale = Mathf.Max(0.01f, _jointConnectedMassScale);
    }

    private bool TryCreateNodeJoint(ConnectionNode previewNode, ConnectionNode targetNode)
    {
        if (previewNode == null || targetNode == null)
            return false;
        if (!previewNode.CanConnectTo(targetNode))
            return false;

        Rigidbody previewRb = previewNode.Owner != null ? previewNode.Owner.GetRigidbody() : null;
        Rigidbody targetRb = targetNode.Owner != null ? targetNode.Owner.GetRigidbody() : null;
        if (previewRb == null || targetRb == null || previewRb == targetRb)
            return false;

        FixedJoint joint = previewRb.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = targetRb;
        ConfigurePlacedJoint(joint, previewRb, targetRb, previewNode, targetNode);
        previewNode.MarkConnected(targetNode);
        return true;
    }

    private int TryCreateSecondaryLoopJoints(Part placedPart)
    {
        if (placedPart == null || _secondarySnapDistance <= 0f)
            return 0;
        if (_maxSecondaryLoopJointsPerPart <= 0)
            return 0;

        var nodes = placedPart.GetNodes();
        if (nodes == null || nodes.Count == 0)
            return 0;

        int created = 0;
        _usedSecondaryTargets.Clear();

        for (int i = 0; i < nodes.Count; i++)
        {
            ConnectionNode previewNode = nodes[i];
            if (previewNode == null || previewNode.IsOccupied)
                continue;

            int hitCount = Physics.OverlapSphereNonAlloc(
                previewNode.GetWorldPosition(),
                _secondarySnapDistance,
                _overlapBuffer,
                _partLayer,
                QueryTriggerInteraction.Ignore
            );

            ConnectionNode bestTarget = null;
            float bestDistance = _secondarySnapDistance;

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
                    if (_usedSecondaryTargets.Contains(targetNode)) continue;
                    if (!previewNode.CanConnectTo(targetNode)) continue;

                    float d = Vector3.Distance(previewNode.GetWorldPosition(), targetNode.GetWorldPosition());
                    if (d > bestDistance) continue;

                    bestDistance = d;
                    bestTarget = targetNode;
                }
            }

            if (bestTarget != null && TryCreateNodeJoint(previewNode, bestTarget))
            {
                _usedSecondaryTargets.Add(bestTarget);
                created++;
                if (created >= _maxSecondaryLoopJointsPerPart)
                    break;
            }
        }

        return created;
    }

    /// <summary>
    /// Finalize the preview into a real physics part and connect joint if snapped.
    /// </summary>
    private void PlaceCurrent()
    {
        if (_currentPreviewPart == null)
            return;

        Part placedPart = _currentPreviewPart;
        Renderer[] placedRenderers = _previewRenderers;
        SnapSystem.SnapResult primarySnap = _lastSnap;

        Part mirroredPlacedPart = null;
        Renderer[] mirroredPlacedRenderers = null;
        SnapSystem.SnapResult mirroredSnap = _lastMirrorSnap;

        if (_isMirrorModeEnabled
            && _mirrorPreviewPart != null
            && _mirrorPreviewPart.gameObject.activeInHierarchy
            && _isMirrorPlacementValid)
        {
            mirroredPlacedPart = _mirrorPreviewPart;
            mirroredPlacedRenderers = _mirrorPreviewRenderers;
        }
        else if (_mirrorPreviewPart != null)
        {
            Destroy(_mirrorPreviewPart.gameObject);
        }

        _mirrorPreviewPart = null;
        _mirrorPreviewRenderers = null;
        _lastMirrorSnap = default;
        _isMirrorPlacementValid = false;

        // Convert preview -> real physics object
        ClearPreviewTint(placedRenderers);
        placedPart.FinalizePlacement();

        if (primarySnap.IsValid && primarySnap.PreviewNode != null && primarySnap.TargetNode != null)
            TryCreateNodeJoint(primarySnap.PreviewNode, primarySnap.TargetNode);

        TryCreateSecondaryLoopJoints(placedPart);

        PartPlaced?.Invoke(placedPart);

        if (mirroredPlacedPart != null)
        {
            ClearPreviewTint(mirroredPlacedRenderers);
            mirroredPlacedPart.FinalizePlacement();

            if (mirroredSnap.IsValid && mirroredSnap.PreviewNode != null && mirroredSnap.TargetNode != null)
                TryCreateNodeJoint(mirroredSnap.PreviewNode, mirroredSnap.TargetNode);

            TryCreateSecondaryLoopJoints(mirroredPlacedPart);

            PartPlaced?.Invoke(mirroredPlacedPart);
        }

        // Continuous build: spawn the same selected part again.
        _currentPreviewPart = null;
        _previewRenderers = null;
        _lastSnap = default;
        _isPlacementValid = false;
        SetSnapIndicatorVisible(false);

        if (_activePreviewPrefab != null)
        {
            TrySpawnPreview(_activePreviewPrefab);
        }
    }

    private static Vector3 MirrorPointAcrossPlane(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
    {
        Vector3 n = planeNormal.normalized;
        Vector3 toPoint = point - planePoint;
        return point - 2f * Vector3.Dot(toPoint, n) * n;
    }

    private static Quaternion MirrorRotationAcrossPlane(Quaternion rotation, Vector3 planeNormal)
    {
        Vector3 n = planeNormal.normalized;
        Vector3 mirroredForward = Vector3.Reflect(rotation * Vector3.forward, n);
        Vector3 mirroredUp = Vector3.Reflect(rotation * Vector3.up, n);

        if (mirroredForward.sqrMagnitude <= 0.0001f)
            mirroredForward = Vector3.forward;
        if (mirroredUp.sqrMagnitude <= 0.0001f)
            mirroredUp = Vector3.up;

        return Quaternion.LookRotation(mirroredForward.normalized, mirroredUp.normalized);
    }

    // Optional: quick debug so you can see validity while playing.
    // You can remove this later.
    private void OnGUI()
    {
        if (_currentPreviewPart == null) return;

        string text = _isPlacementValid ? "PLACEMENT: VALID" : "PLACEMENT: INVALID";
        string snap = _lastSnap.IsValid
            ? $" | SNAP {Mathf.Max(0f, _lastSnap.Distance):F2}m ({_lastSnap.SupportCount} links)"
            : " | SNAP NONE";
        string hint = $" | ROTATE [{_rotateLeftKey}/{_rotateRightKey}]";
        string mirror = _isMirrorModeEnabled ? (_isMirrorPlacementValid ? " | MIRROR ON: VALID" : " | MIRROR ON: INVALID") : " | MIRROR OFF";
        GUI.Label(new Rect(10, 10, 800, 30), text + snap + hint + mirror);
    }

}

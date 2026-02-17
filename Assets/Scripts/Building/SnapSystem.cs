using UnityEngine;
using System.Collections.Generic;

public sealed class SnapSystem : MonoBehaviour
{
    [Header("Snap Settings")]
    [SerializeField] private float _snapRadius = 0.5f;
    [SerializeField] private LayerMask _partLayer;
    [SerializeField] private int _maxCandidates = 32;

    public float SnapRadius => _snapRadius;
    public LayerMask PartLayer => _partLayer;
    public int MaxCandidates => _maxCandidates;

    private Collider[] _overlapBuffer;
    private readonly HashSet<Part> _candidateParts = new HashSet<Part>();
    private bool _didWarnCandidateOverflow;

    public struct SnapResult
    {
        public bool IsValid;
        public ConnectionNode PreviewNode;
        public ConnectionNode TargetNode;
        public Vector3 SnappedWorldPosition;
        public float Distance;
    }

    private void Awake()
    {
        _overlapBuffer = new Collider[Mathf.Max(1, _maxCandidates)];
    }

    public SnapResult FindBestSnap(
        Part previewPart,
        Vector3 desiredWorldPosition,
        Part preferredTargetPart,
        Vector3 preferredSurfaceNormal)
    {
        SnapResult best = default;
        best.IsValid = false;
        best.Distance = float.PositiveInfinity;

        if (previewPart == null) return best;
        if (_snapRadius <= 0f) return best;

        var previewNodes = previewPart.GetNodes();
        if (previewNodes == null || previewNodes.Count == 0) return best;

        Transform previewRoot = previewPart.transform;

        // If we are hovering a specific part, snap ONLY to that part and ONLY to face-matching nodes.
        if (preferredTargetPart != null && preferredTargetPart != previewPart)
        {
            return EvaluateTargetPart(
                previewRoot,
                previewNodes,
                preferredTargetPart,
                preferredSurfaceNormal,
                ref best
            );
        }

        // Otherwise: normal behavior (hovering ground) -> find nearby parts.
        _candidateParts.Clear();
        int hitCount = Physics.OverlapSphereNonAlloc(
            desiredWorldPosition,
            _snapRadius,
            _overlapBuffer,
            _partLayer,
            QueryTriggerInteraction.Ignore
        );

        if (hitCount >= _overlapBuffer.Length)
        {
            if (!_didWarnCandidateOverflow)
            {
                Debug.LogWarning($"[SnapSystem] Candidate buffer full ({_overlapBuffer.Length}). Increase _maxCandidates to avoid missed snap targets.", this);
                _didWarnCandidateOverflow = true;
            }
        }
        else
        {
            _didWarnCandidateOverflow = false;
        }

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _overlapBuffer[i];
            if (col == null) continue;

            Part targetPart = col.GetComponentInParent<Part>();
            if (targetPart == null) continue;
            if (targetPart == previewPart) continue;
            if (!_candidateParts.Add(targetPart)) continue;

            EvaluateTargetPart(previewRoot, previewNodes, targetPart, preferredSurfaceNormal: default, ref best);
        }

        return best;
    }

    private SnapResult EvaluateTargetPart(
        Transform previewRoot,
        System.Collections.Generic.IReadOnlyList<ConnectionNode> previewNodes,
        Part targetPart,
        Vector3 preferredSurfaceNormal,
        ref SnapResult best)
    {
        var targetNodes = targetPart.GetNodes();
        if (targetNodes == null || targetNodes.Count == 0) return best;

        bool useSurfaceFilter = preferredSurfaceNormal != default;

        for (int p = 0; p < previewNodes.Count; p++)
        {
            ConnectionNode previewNode = previewNodes[p];
            if (previewNode == null) continue;

            Vector3 previewNodePos = previewNode.GetWorldPosition();

            for (int t = 0; t < targetNodes.Count; t++)
            {
                ConnectionNode targetNode = targetNodes[t];
                if (targetNode == null) continue;

                // If we have a surface normal (hovering a face), only allow nodes on that face.
                if (useSurfaceFilter && !targetNode.MatchesSurfaceNormal(preferredSurfaceNormal))
                    continue;

                if (!previewNode.CanConnectTo(targetNode))
                    continue;

                Vector3 targetPos = targetNode.GetWorldPosition();
                float d = Vector3.Distance(previewNodePos, targetPos);
                if (d > _snapRadius) continue;

                if (d < best.Distance)
                {
                    best.IsValid = true;
                    best.Distance = d;
                    best.PreviewNode = previewNode;
                    best.TargetNode = targetNode;
                    best.SnappedWorldPosition = ComputeSnappedRootPosition(previewRoot, previewNode, targetPos);
                }
            }
        }

        return best;
    }

    public Vector3 ComputeSnappedRootPosition(Transform partRoot, ConnectionNode previewNode, Vector3 targetNodeWorldPos)
    {
        Vector3 offset = previewNode.GetWorldPosition() - partRoot.position;
        return targetNodeWorldPos - offset;
    }
}

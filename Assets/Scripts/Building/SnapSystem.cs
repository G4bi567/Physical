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
    private readonly List<Part> _candidatePartList = new List<Part>(64);
    private readonly HashSet<ConnectionNode> _supportUsedTargets = new HashSet<ConnectionNode>();
    private bool _didWarnCandidateOverflow;

    public struct SnapResult
    {
        public bool IsValid;
        public ConnectionNode PreviewNode;
        public ConnectionNode TargetNode;
        public Vector3 SnappedWorldPosition;
        public float Distance;
        public int SupportCount;
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
        best.SupportCount = 0;

        if (previewPart == null) return best;
        if (_snapRadius <= 0f) return best;

        var previewNodes = previewPart.GetNodes();
        if (previewNodes == null || previewNodes.Count == 0) return best;

        Transform previewRoot = previewPart.transform;

        _candidateParts.Clear();
        _candidatePartList.Clear();

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
            _candidatePartList.Add(targetPart);
        }

        if (preferredTargetPart != null && preferredTargetPart != previewPart && _candidateParts.Add(preferredTargetPart))
            _candidatePartList.Add(preferredTargetPart);

        if (preferredTargetPart != null && preferredTargetPart != previewPart)
        {
            SnapResult strictFaceResult = EvaluateTargetPart(
                previewRoot,
                previewNodes,
                preferredTargetPart,
                preferredSurfaceNormal,
                _candidatePartList,
                prioritizeDistance: true,
                ref best
            );

            if (strictFaceResult.IsValid)
                return strictFaceResult;

            // Fallback: if strict surface-normal matching finds nothing,
            // retry against the hovered target without face filtering.
            return EvaluateTargetPart(
                previewRoot,
                previewNodes,
                preferredTargetPart,
                preferredSurfaceNormal: default,
                _candidatePartList,
                prioritizeDistance: true,
                ref best
            );
        }

        for (int i = 0; i < _candidatePartList.Count; i++)
        {
            Part targetPart = _candidatePartList[i];
            if (targetPart == null) continue;

            EvaluateTargetPart(
                previewRoot,
                previewNodes,
                targetPart,
                preferredSurfaceNormal: default,
                _candidatePartList,
                prioritizeDistance: false,
                ref best
            );
        }

        return best;
    }

    private SnapResult EvaluateTargetPart(
        Transform previewRoot,
        IReadOnlyList<ConnectionNode> previewNodes,
        Part targetPart,
        Vector3 preferredSurfaceNormal,
        List<Part> supportCandidates,
        bool prioritizeDistance,
        ref SnapResult best)
    {
        var targetNodes = targetPart != null ? targetPart.GetNodes() : null;
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

                if (useSurfaceFilter && !targetNode.MatchesSurfaceNormal(preferredSurfaceNormal))
                    continue;

                if (!previewNode.CanConnectTo(targetNode))
                    continue;

                Vector3 targetPos = targetNode.GetWorldPosition();
                float d = Vector3.Distance(previewNodePos, targetPos);
                if (d > _snapRadius) continue;

                Vector3 snappedPos = ComputeSnappedRootPosition(previewRoot, previewNode, targetPos);
                int supportCount = CountSupportConnections(previewRoot, previewNodes, snappedPos, supportCandidates);

                bool shouldReplace = !best.IsValid;
                if (prioritizeDistance)
                {
                    if (!shouldReplace && d < best.Distance)
                        shouldReplace = true;
                }
                else
                {
                    if (!shouldReplace && supportCount > best.SupportCount) shouldReplace = true;
                    if (!shouldReplace && supportCount == best.SupportCount && d < best.Distance) shouldReplace = true;
                }

                if (shouldReplace)
                {
                    best.IsValid = true;
                    best.Distance = d;
                    best.SupportCount = supportCount;
                    best.PreviewNode = previewNode;
                    best.TargetNode = targetNode;
                    best.SnappedWorldPosition = snappedPos;
                }
            }
        }

        return best;
    }

    private int CountSupportConnections(
        Transform previewRoot,
        IReadOnlyList<ConnectionNode> previewNodes,
        Vector3 snappedRootPosition,
        List<Part> supportCandidates)
    {
        if (previewRoot == null || previewNodes == null || supportCandidates == null || supportCandidates.Count == 0)
            return 0;

        Vector3 delta = snappedRootPosition - previewRoot.position;
        int count = 0;
        _supportUsedTargets.Clear();

        for (int i = 0; i < previewNodes.Count; i++)
        {
            ConnectionNode previewNode = previewNodes[i];
            if (previewNode == null || previewNode.IsOccupied) continue;

            Vector3 shiftedPreviewNodePos = previewNode.GetWorldPosition() + delta;
            ConnectionNode bestTarget = null;
            float bestDistance = _snapRadius;

            for (int p = 0; p < supportCandidates.Count; p++)
            {
                Part candidate = supportCandidates[p];
                if (candidate == null) continue;
                if (candidate == previewNode.Owner) continue;

                var targetNodes = candidate.GetNodes();
                if (targetNodes == null || targetNodes.Count == 0) continue;

                for (int t = 0; t < targetNodes.Count; t++)
                {
                    ConnectionNode targetNode = targetNodes[t];
                    if (targetNode == null) continue;
                    if (_supportUsedTargets.Contains(targetNode)) continue;
                    if (!previewNode.CanConnectTo(targetNode)) continue;

                    float distance = Vector3.Distance(shiftedPreviewNodePos, targetNode.GetWorldPosition());
                    if (distance > bestDistance) continue;

                    bestDistance = distance;
                    bestTarget = targetNode;
                }
            }

            if (bestTarget != null)
            {
                _supportUsedTargets.Add(bestTarget);
                count++;
            }
        }

        return count;
    }

    public Vector3 ComputeSnappedRootPosition(Transform partRoot, ConnectionNode previewNode, Vector3 targetNodeWorldPos)
    {
        Vector3 offset = previewNode.GetWorldPosition() - partRoot.position;
        return targetNodeWorldPos - offset;
    }
}

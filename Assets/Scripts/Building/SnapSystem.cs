using UnityEngine;

public class SnapSystem : MonoBehaviour
{
    [Header("Snap Settings")]
    [SerializeField] private float _snapRadius = 0.5f;
    [SerializeField] private LayerMask _partLayer;
    [SerializeField] private int _maxCandidates = 32;

    public float SnapRadius => _snapRadius;
    public LayerMask PartLayer => _partLayer;
    public int MaxCandidates => _maxCandidates;

    private Collider[] _overlapBuffer;

    private void Awake()
    {
        _overlapBuffer = new Collider[Mathf.Max(1, _maxCandidates)];
    }

    public struct SnapResult
    {
        public bool IsValid;
        public ConnectionNode PreviewNode;
        public ConnectionNode TargetNode;
        public Vector3 SnappedWorldPosition;
        public float Distance;
    }

    public bool IsSnappable(Part previewPart)
    {
        if (previewPart == null) return false;

        var nodes = previewPart.GetNodes();
        return nodes != null && nodes.Count > 0;
    }

    public SnapResult FindBestSnap(Part previewPart, Vector3 desiredWorldPosition)
    {
        SnapResult best = default;
        best.IsValid = false;
        best.Distance = float.PositiveInfinity;

        if (!IsSnappable(previewPart))
            return best;

        if (_snapRadius <= 0f)
            return best;

        var previewNodes = previewPart.GetNodes();
        Transform previewRoot = previewPart.transform;

        int hitCount = Physics.OverlapSphereNonAlloc(
            desiredWorldPosition,
            _snapRadius,
            _overlapBuffer,
            _partLayer,
            QueryTriggerInteraction.Ignore
        );

        if (hitCount == 0)
            return best;

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _overlapBuffer[i];
            if (col == null) continue;

            Part targetPart = col.GetComponentInParent<Part>();
            if (targetPart == null) continue;
            if (targetPart == previewPart) continue;

            var targetNodes = targetPart.GetNodes();
            if (targetNodes == null || targetNodes.Count == 0) continue;

            for (int p = 0; p < previewNodes.Count; p++)
            {
                ConnectionNode previewNode = previewNodes[p];
                if (previewNode == null) continue;

                Vector3 previewNodePos = previewNode.GetWorldPosition();

                for (int t = 0; t < targetNodes.Count; t++)
                {
                    ConnectionNode targetNode = targetNodes[t];
                    if (targetNode == null) continue;

                    if (!previewNode.CanConnectTo(targetNode))
                        continue;

                    Vector3 targetNodePos = targetNode.GetWorldPosition();
                    float d = Vector3.Distance(previewNodePos, targetNodePos);

                    if (d < best.Distance && d <= _snapRadius)
                    {
                        best.IsValid = true;
                        best.Distance = d;
                        best.PreviewNode = previewNode;
                        best.TargetNode = targetNode;

                        best.SnappedWorldPosition = ComputeSnappedRootPosition(
                            previewRoot,
                            previewNode,
                            targetNodePos
                        );
                    }
                }
            }
        }

        return best;
    }

    public Vector3 ComputeSnappedRootPosition(Transform partRoot, ConnectionNode previewNode, Vector3 targetNodeWorldPos)
    {
        Vector3 rootPos = partRoot.position;
        Vector3 previewNodePos = previewNode.GetWorldPosition();
        Vector3 offset = previewNodePos - rootPos;
        return targetNodeWorldPos - offset;
    }
}

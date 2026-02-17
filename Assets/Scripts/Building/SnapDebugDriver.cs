using UnityEngine;

public sealed class SnapDebugDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SnapSystem _snapSystem;
    [SerializeField] private Part _previewPart;

    [Tooltip("Move this transform around (in scene) to simulate the 'desired placement point'.")]
    [SerializeField] private Transform _desiredPoint;

    [Header("Behavior")]
    [SerializeField] private bool _applySnapToPreview = true;
    [SerializeField] private bool _logWhenValidChanges = true;

    private bool _lastValid;

    private void Start()
    {
        if (_previewPart != null)
            _previewPart.InitializeForPlacement();
    }

    private void Update()
    {
        if (_snapSystem == null || _previewPart == null || _desiredPoint == null)
            return;

        var result = _snapSystem.FindBestSnap(_previewPart, _desiredPoint.position, null, default);

        if (_logWhenValidChanges && result.IsValid != _lastValid)
        {
            _lastValid = result.IsValid;
            Debug.Log($"[SnapDebug] IsValid={result.IsValid} Distance={result.Distance}", this);
        }

        if (!_applySnapToPreview)
            return;

        _previewPart.transform.position = result.IsValid
            ? result.SnappedWorldPosition
            : _desiredPoint.position;
    }
}

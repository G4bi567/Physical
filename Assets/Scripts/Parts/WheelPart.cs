using UnityEngine;

[DisallowMultipleComponent]
public sealed class WheelPart : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WheelCollider _wheelCollider;
    [SerializeField] private Transform _visualWheel;

    [Header("Drive Setup")]
    [SerializeField] private bool _isDriven = true;
    [SerializeField] private bool _isSteerable = true;
    [SerializeField] private bool _invertDriveDirection;
    [SerializeField] private bool _invertSteerDirection;
    [SerializeField, Min(0f)] private float _maxSteerAngle = 30f;
    [SerializeField, Min(0f)] private float _maxMotorTorque = 1000f;
    [SerializeField, Min(0f)] private float _maxBrakeTorque = 2500f;
    [SerializeField] private Vector3 _visualEulerOffset;

    public bool IsDriven => _isDriven;
    public bool IsSteerable => _isSteerable;
    public WheelCollider Collider => _wheelCollider;
    public bool IsReady => _wheelCollider != null;

    private void Reset()
    {
        AutoAssignReferences();
    }

    private void Awake()
    {
        AutoAssignReferences();
    }

    private void OnValidate()
    {
        AutoAssignReferences();
        _maxSteerAngle = Mathf.Max(0f, _maxSteerAngle);
        _maxMotorTorque = Mathf.Max(0f, _maxMotorTorque);
        _maxBrakeTorque = Mathf.Max(0f, _maxBrakeTorque);
    }

    private void LateUpdate()
    {
        SyncVisualFromCollider();
    }

    public void ApplyDrive(float motorTorque, float steerInput, float brakeTorque)
    {
        if (_wheelCollider == null || !_wheelCollider.enabled)
            return;

        float signedSteer = _invertSteerDirection ? -steerInput : steerInput;
        float steerAngle = _isSteerable ? Mathf.Clamp(signedSteer, -1f, 1f) * _maxSteerAngle : 0f;
        _wheelCollider.steerAngle = steerAngle;

        float clampedBrakeTorque = Mathf.Clamp(brakeTorque, 0f, _maxBrakeTorque);
        _wheelCollider.brakeTorque = clampedBrakeTorque;

        float signedMotor = _invertDriveDirection ? -motorTorque : motorTorque;
        float clampedMotorTorque = Mathf.Clamp(signedMotor, -_maxMotorTorque, _maxMotorTorque);
        _wheelCollider.motorTorque = _isDriven ? clampedMotorTorque : 0f;
    }

    public void ClearDrive()
    {
        if (_wheelCollider == null)
            return;

        _wheelCollider.motorTorque = 0f;
        _wheelCollider.steerAngle = 0f;
        _wheelCollider.brakeTorque = 0f;
    }

    private void AutoAssignReferences()
    {
        if (_wheelCollider == null)
            _wheelCollider = GetComponent<WheelCollider>();

        if (_wheelCollider == null)
            _wheelCollider = GetComponentInChildren<WheelCollider>(includeInactive: true);

        if (_visualWheel != null)
            return;

        var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            if (renderer.transform == transform) continue;
            if (renderer.GetComponent<WheelCollider>() != null) continue;

            _visualWheel = renderer.transform;
            break;
        }
    }

    private void SyncVisualFromCollider()
    {
        if (_wheelCollider == null || _visualWheel == null || !_wheelCollider.enabled)
            return;

        _wheelCollider.GetWorldPose(out Vector3 worldPos, out Quaternion worldRot);
        _visualWheel.SetPositionAndRotation(worldPos, worldRot * Quaternion.Euler(_visualEulerOffset));
    }
}

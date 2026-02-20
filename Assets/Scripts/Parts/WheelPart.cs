using UnityEngine;

[DisallowMultipleComponent]
public sealed class WheelPart : MonoBehaviour
{
    private static readonly System.Collections.Generic.HashSet<int> WarnedScaleObjects = new System.Collections.Generic.HashSet<int>();
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
    [SerializeField, Min(0.01f)] private float _motorTorqueResponsePerSecond = 6000f;
    [SerializeField, Min(0.01f)] private float _brakeTorqueResponsePerSecond = 8000f;
    [SerializeField] private bool _enableAbsBrakeModulation = true;
    [SerializeField, Min(0f)] private float _absForwardSlipThreshold = 0.55f;
    [SerializeField, Range(0f, 1f)] private float _absBrakeReleaseMultiplier = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _airborneBrakeMultiplier = 0.2f;
    [SerializeField] private bool _autoCaptureVisualOffsetOnPlay = true;
    [SerializeField] private Vector3 _visualEulerOffset;

    [Header("Wheel Physics Tuning")]
    [SerializeField] private bool _autoTuneWheelCollider = true;
    [SerializeField] private bool _autoFitColliderToVisual = true;
    [SerializeField] private bool _warnOnNonUniformParentScale = false;
    [SerializeField] private bool _disableExtraVisualColliders = true;
    [SerializeField, Min(0.1f)] private float _autoFitRadiusMultiplier = 1.0f;
    [SerializeField, Min(0.01f)] private float _autoFitMinRadius = 0.12f;
    [SerializeField, Min(0.01f)] private float _fallbackRadiusForNonUniformVisualScale = 0.18f;
    [SerializeField, Min(0.1f)] private float _wheelMass = 22f;
    [SerializeField, Min(0f)] private float _wheelDampingRate = 1.0f;
    [SerializeField, Min(0.005f)] private float _suspensionDistance = 0.02f;
    [SerializeField, Min(1f)] private float _suspensionSpring = 28000f;
    [SerializeField, Min(0f)] private float _suspensionDamper = 4200f;
    [SerializeField, Range(0f, 1f)] private float _suspensionTargetPosition = 0.5f;
    [SerializeField, Min(0.1f)] private float _forwardFrictionStiffness = 1.9f;
    [SerializeField, Min(0.1f)] private float _sidewaysFrictionStiffness = 2.3f;
    [SerializeField, Min(0f)] private float _maxForwardSlipBeforeTorqueCut = 1.5f;
    [SerializeField, Min(0f)] private float _maxSideSlipBeforeTorqueCut = 1.2f;
    [SerializeField, Range(0f, 1f)] private float _slipTorqueMultiplier = 0.75f;
    [SerializeField, Range(0f, 1f)] private float _airborneTorqueMultiplier = 0.25f;
    [Header("Build Debug")]
    [SerializeField] private bool _showBuildSpinArrow = true;
    [SerializeField, Min(0.05f)] private float _buildArrowLength = 0.45f;
    [SerializeField] private Color _buildArrowColor = new Color(0.25f, 0.95f, 1f, 1f);
    [Header("Runtime Debug")]
    [SerializeField] private bool _enableWheelDebugLogs = false;
    [SerializeField, Min(0.05f)] private float _debugLogInterval = 0.25f;
    [SerializeField, Min(0f)] private float _visualPoseErrorWarnDistance = 0.08f;
    [SerializeField, Min(0f)] private float _visualPoseErrorWarnAngle = 12f;

    public bool IsDriven => _isDriven;
    public bool IsSteerable => _isSteerable;
    public WheelCollider Collider => _wheelCollider;
    public bool IsReady => _wheelCollider != null;
    private BuildManager _buildManager;
    private float _appliedMotorTorque;
    private float _appliedBrakeTorque;
    private bool _hasNonUniformVisualScale;
    private float _nextDebugLogTime;
    private int _wheelDebugId;

    private void Reset()
    {
        AutoAssignReferences();
    }

    private void Awake()
    {
        AutoAssignReferences();
        _wheelDebugId = GetInstanceID();
        _buildManager = FindFirstObjectByType<BuildManager>();
        _hasNonUniformVisualScale = HasNonUniformScaleInChain(_visualWheel);
        WarnIfVisualScaleChainIsNonUniform();
        DisableExtraVisualColliders();
        ApplyWheelColliderTuning();

        // If the mesh is already oriented correctly in editor, capture that as runtime offset
        // so the first wheel sync does not "snap" to a wrong axis.
        if (_autoCaptureVisualOffsetOnPlay)
            CaptureVisualOffsetFromCurrentPose();

        LogWheelSetup("Awake");
    }

    private void OnValidate()
    {
        AutoAssignReferences();
        _maxSteerAngle = Mathf.Max(0f, _maxSteerAngle);
        _maxMotorTorque = Mathf.Max(0f, _maxMotorTorque);
        _maxBrakeTorque = Mathf.Max(0f, _maxBrakeTorque);
        _motorTorqueResponsePerSecond = Mathf.Max(0.01f, _motorTorqueResponsePerSecond);
        _brakeTorqueResponsePerSecond = Mathf.Max(0.01f, _brakeTorqueResponsePerSecond);
        _absForwardSlipThreshold = Mathf.Max(0f, _absForwardSlipThreshold);
        _absBrakeReleaseMultiplier = Mathf.Clamp01(_absBrakeReleaseMultiplier);
        _airborneBrakeMultiplier = Mathf.Clamp01(_airborneBrakeMultiplier);
        _buildArrowLength = Mathf.Max(0.05f, _buildArrowLength);
        _autoFitRadiusMultiplier = Mathf.Max(0.1f, _autoFitRadiusMultiplier);
        _autoFitMinRadius = Mathf.Max(0.01f, _autoFitMinRadius);
        _fallbackRadiusForNonUniformVisualScale = Mathf.Max(0.01f, _fallbackRadiusForNonUniformVisualScale);
        _wheelMass = Mathf.Max(0.1f, _wheelMass);
        _wheelDampingRate = Mathf.Max(0f, _wheelDampingRate);
        _suspensionDistance = Mathf.Max(0.01f, _suspensionDistance);
        _suspensionSpring = Mathf.Max(1f, _suspensionSpring);
        _suspensionDamper = Mathf.Max(0f, _suspensionDamper);
        _forwardFrictionStiffness = Mathf.Max(0.1f, _forwardFrictionStiffness);
        _sidewaysFrictionStiffness = Mathf.Max(0.1f, _sidewaysFrictionStiffness);
        _maxForwardSlipBeforeTorqueCut = Mathf.Max(0f, _maxForwardSlipBeforeTorqueCut);
        _maxSideSlipBeforeTorqueCut = Mathf.Max(0f, _maxSideSlipBeforeTorqueCut);
        _slipTorqueMultiplier = Mathf.Clamp01(_slipTorqueMultiplier);
        _airborneTorqueMultiplier = Mathf.Clamp01(_airborneTorqueMultiplier);
        _debugLogInterval = Mathf.Max(0.05f, _debugLogInterval);
        _visualPoseErrorWarnDistance = Mathf.Max(0f, _visualPoseErrorWarnDistance);
        _visualPoseErrorWarnAngle = Mathf.Max(0f, _visualPoseErrorWarnAngle);
        if (_disableExtraVisualColliders)
            DisableExtraVisualColliders();
        ApplyWheelColliderTuning();
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

        float targetBrakeTorque = Mathf.Clamp(brakeTorque, 0f, _maxBrakeTorque);

        float signedMotor = _invertDriveDirection ? -motorTorque : motorTorque;
        float targetMotorTorque = Mathf.Clamp(signedMotor, -_maxMotorTorque, _maxMotorTorque);
        if (!_isDriven)
            targetMotorTorque = 0f;

        bool grounded = _wheelCollider.GetGroundHit(out WheelHit hit);

        // Slip/load-aware traction and braking: prevent single-wheel lockup hop under braking.
        if (grounded)
        {
            bool excessiveSlip = Mathf.Abs(hit.forwardSlip) > _maxForwardSlipBeforeTorqueCut
                && Mathf.Abs(hit.sidewaysSlip) > _maxSideSlipBeforeTorqueCut;
            if (excessiveSlip)
                targetMotorTorque *= _slipTorqueMultiplier;

            // Simple ABS: release some brake when lockup slip appears.
            if (_enableAbsBrakeModulation && Mathf.Abs(hit.forwardSlip) > _absForwardSlipThreshold)
                targetBrakeTorque *= _absBrakeReleaseMultiplier;

        }
        else
        {
            targetMotorTorque *= _airborneTorqueMultiplier;
            targetBrakeTorque *= _airborneBrakeMultiplier;
        }

        // Smooth torque application to avoid frame-to-frame impulses that can cause hopping.
        float response = Mathf.Max(0.01f, _motorTorqueResponsePerSecond) * Time.deltaTime;
        _appliedMotorTorque = Mathf.MoveTowards(_appliedMotorTorque, targetMotorTorque, response);
        _wheelCollider.motorTorque = _appliedMotorTorque;
        float brakeResponse = Mathf.Max(0.01f, _brakeTorqueResponsePerSecond) * Time.deltaTime;
        _appliedBrakeTorque = Mathf.MoveTowards(_appliedBrakeTorque, targetBrakeTorque, brakeResponse);
        _wheelCollider.brakeTorque = _appliedBrakeTorque;

        EmitRuntimeWheelDebug(targetMotorTorque, _appliedBrakeTorque, steerAngle);
    }

    public void ClearDrive()
    {
        if (_wheelCollider == null)
            return;

        _appliedMotorTorque = 0f;
        _appliedBrakeTorque = 0f;
        _wheelCollider.motorTorque = 0f;
        _wheelCollider.steerAngle = 0f;
        _wheelCollider.brakeTorque = 0f;
    }

    private void ApplyWheelColliderTuning()
    {
        if (!_autoTuneWheelCollider || _wheelCollider == null)
            return;

        bool shouldAutoFit = _autoFitColliderToVisual;
        if (shouldAutoFit)
            FitColliderToVisual();
        else if (_hasNonUniformVisualScale)
            _wheelCollider.radius = Mathf.Max(_wheelCollider.radius, _fallbackRadiusForNonUniformVisualScale);

        _wheelCollider.mass = _wheelMass;
        _wheelCollider.wheelDampingRate = _wheelDampingRate;
        _wheelCollider.suspensionDistance = _suspensionDistance;

        JointSpring spring = _wheelCollider.suspensionSpring;
        spring.spring = _suspensionSpring;
        spring.damper = _suspensionDamper;
        spring.targetPosition = _suspensionTargetPosition;
        _wheelCollider.suspensionSpring = spring;

        WheelFrictionCurve forward = _wheelCollider.forwardFriction;
        forward.stiffness = _forwardFrictionStiffness;
        _wheelCollider.forwardFriction = forward;

        WheelFrictionCurve sideways = _wheelCollider.sidewaysFriction;
        sideways.stiffness = _sidewaysFrictionStiffness;
        _wheelCollider.sidewaysFriction = sideways;

        _wheelCollider.ConfigureVehicleSubsteps(5f, 12, 15);
    }

    private void FitColliderToVisual()
    {
        if (_wheelCollider == null || _visualWheel == null)
            return;

        Renderer[] renderers = _visualWheel.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers == null || renderers.Length == 0)
            return;

        bool hasBounds = false;
        Vector3 min = Vector3.zero;
        Vector3 max = Vector3.zero;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            Bounds wb = r.bounds;
            Vector3 c = wb.center;
            Vector3 e = wb.extents;

            Vector3[] corners = new Vector3[8];
            corners[0] = c + new Vector3(-e.x, -e.y, -e.z);
            corners[1] = c + new Vector3(-e.x, -e.y,  e.z);
            corners[2] = c + new Vector3(-e.x,  e.y, -e.z);
            corners[3] = c + new Vector3(-e.x,  e.y,  e.z);
            corners[4] = c + new Vector3( e.x, -e.y, -e.z);
            corners[5] = c + new Vector3( e.x, -e.y,  e.z);
            corners[6] = c + new Vector3( e.x,  e.y, -e.z);
            corners[7] = c + new Vector3( e.x,  e.y,  e.z);

            for (int j = 0; j < corners.Length; j++)
            {
                Vector3 local = _wheelCollider.transform.InverseTransformPoint(corners[j]);
                if (!hasBounds)
                {
                    min = local;
                    max = local;
                    hasBounds = true;
                }
                else
                {
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }
        }

        if (!hasBounds)
            return;

        Vector3 size = max - min;
        Vector3 center = (min + max) * 0.5f;

        // Auto-detect wheel axle axis from the smallest local extent (wheel width).
        // Remaining two axes define rolling diameter/radius.
        float sx = Mathf.Abs(size.x);
        float sy = Mathf.Abs(size.y);
        float sz = Mathf.Abs(size.z);

        int widthAxis = 0;
        float minExtent = sx;
        if (sy < minExtent) { minExtent = sy; widthAxis = 1; }
        if (sz < minExtent) { minExtent = sz; widthAxis = 2; }

        float radialA;
        float radialB;
        switch (widthAxis)
        {
            case 0:
                radialA = sy;
                radialB = sz;
                break;
            case 1:
                radialA = sx;
                radialB = sz;
                break;
            default:
                radialA = sx;
                radialB = sy;
                break;
        }

        float fittedRadius = Mathf.Max(0.01f, Mathf.Max(radialA, radialB) * 0.5f);
        fittedRadius *= Mathf.Max(0.1f, _autoFitRadiusMultiplier);
        fittedRadius = Mathf.Max(_autoFitMinRadius, fittedRadius);
        if (_hasNonUniformVisualScale)
            fittedRadius = Mathf.Max(fittedRadius, _fallbackRadiusForNonUniformVisualScale);

        _wheelCollider.center = center;
        _wheelCollider.radius = fittedRadius;
        _wheelCollider.forceAppPointDistance = Mathf.Clamp(fittedRadius * 0.25f, 0f, fittedRadius);
    }

    private void WarnIfVisualScaleChainIsNonUniform()
    {
        if (!_warnOnNonUniformParentScale || !_enableWheelDebugLogs || _visualWheel == null || !_hasNonUniformVisualScale)
            return;

        Transform current = _visualWheel;
        while (current != null)
        {
            Vector3 s = current.localScale;
            bool nonUniform = Mathf.Abs(s.x - s.y) > 0.0001f
                || Mathf.Abs(s.y - s.z) > 0.0001f
                || Mathf.Abs(s.x - s.z) > 0.0001f;
            if (nonUniform)
            {
                int id = current.GetInstanceID();
                if (!WarnedScaleObjects.Add(id))
                    return;

                Debug.LogWarning(
                    $"[WheelPart] Non-uniform scale detected on '{current.name}' in wheel visual hierarchy. " +
                    "Rotating wheels under non-uniform scale can look oval and cause unstable contact. " +
                    "Auto-fit collider is running with axis detection + fallback radius clamp. " +
                    "Best fix: use uniform scale (1,1,1) on wheel parents and size via mesh import/prefab dimensions.",
                    current
                );
                return;
            }

            current = current.parent;
        }
    }

    private void DisableExtraVisualColliders()
    {
        if (!_disableExtraVisualColliders)
            return;

        Collider[] allColliders = GetComponentsInChildren<Collider>(includeInactive: true);
        if (allColliders == null || allColliders.Length == 0)
            return;

        int disabledCount = 0;
        for (int i = 0; i < allColliders.Length; i++)
        {
            Collider c = allColliders[i];
            if (c == null) continue;
            if (c == _wheelCollider) continue;
            if (c is WheelCollider) continue;
            if (!c.enabled) continue;

            c.enabled = false;
            disabledCount++;
        }

        if (disabledCount > 0 && _enableWheelDebugLogs)
        {
            Debug.Log($"[WheelDebug:{name}#{_wheelDebugId}] Disabled {disabledCount} extra collider(s) under wheel hierarchy.", this);
        }
    }

    private static bool HasNonUniformScaleInChain(Transform leaf)
    {
        Transform current = leaf;
        while (current != null)
        {
            Vector3 s = current.localScale;
            bool nonUniform = Mathf.Abs(s.x - s.y) > 0.0001f
                || Mathf.Abs(s.y - s.z) > 0.0001f
                || Mathf.Abs(s.x - s.z) > 0.0001f;
            if (nonUniform)
                return true;

            current = current.parent;
        }

        return false;
    }

    private void EmitRuntimeWheelDebug(float targetMotorTorque, float brakeTorque, float steerAngle)
    {
        if (!_enableWheelDebugLogs || _wheelCollider == null)
            return;
        if (Time.time < _nextDebugLogTime)
            return;

        _nextDebugLogTime = Time.time + Mathf.Max(0.05f, _debugLogInterval);

        bool grounded = _wheelCollider.GetGroundHit(out WheelHit hit);
        if (grounded)
        {
            Debug.Log(
                $"[WheelDebug:{name}#{_wheelDebugId}] g=1 rpm={_wheelCollider.rpm:F1} r={_wheelCollider.radius:F3} " +
                $"mt={_appliedMotorTorque:F1}/{targetMotorTorque:F1} bt={brakeTorque:F1} sa={steerAngle:F1} " +
                $"slipF={hit.forwardSlip:F2} slipS={hit.sidewaysSlip:F2} force={hit.force:F1} " +
                $"suspD={_wheelCollider.suspensionDistance:F3}",
                this
            );
        }
        else
        {
            Debug.Log(
                $"[WheelDebug:{name}#{_wheelDebugId}] g=0 rpm={_wheelCollider.rpm:F1} r={_wheelCollider.radius:F3} " +
                $"mt={_appliedMotorTorque:F1}/{targetMotorTorque:F1} bt={brakeTorque:F1} sa={steerAngle:F1}",
                this
            );
        }
    }

    private void LogWheelSetup(string phase)
    {
        if (!_enableWheelDebugLogs || _wheelCollider == null)
            return;

        Debug.Log(
            $"[WheelDebug:{name}#{_wheelDebugId}] {phase} radius={_wheelCollider.radius:F3} center={_wheelCollider.center} " +
            $"mass={_wheelCollider.mass:F2} dampRate={_wheelCollider.wheelDampingRate:F2} suspD={_wheelCollider.suspensionDistance:F3} " +
            $"spring={_wheelCollider.suspensionSpring.spring:F1} damper={_wheelCollider.suspensionSpring.damper:F1} " +
            $"fStiff={_wheelCollider.forwardFriction.stiffness:F2} sStiff={_wheelCollider.sidewaysFriction.stiffness:F2} " +
            $"nonUniformScale={_hasNonUniformVisualScale}",
            this
        );
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
        Quaternion targetRot = worldRot * Quaternion.Euler(_visualEulerOffset);
        Vector3 oldPos = _visualWheel.position;
        Quaternion oldRot = _visualWheel.rotation;
        _visualWheel.SetPositionAndRotation(worldPos, targetRot);

        if (_enableWheelDebugLogs && Time.time >= _nextDebugLogTime)
        {
            float posError = Vector3.Distance(oldPos, worldPos);
            float angError = Quaternion.Angle(oldRot, targetRot);
            if (posError > _visualPoseErrorWarnDistance || angError > _visualPoseErrorWarnAngle)
            {
                Debug.LogWarning(
                    $"[WheelDebug:{name}#{_wheelDebugId}] visual mismatch posErr={posError:F3} angErr={angError:F1}. " +
                    "Check wheel visual hierarchy/scales.",
                    this
                );
            }
        }
    }

    private void CaptureVisualOffsetFromCurrentPose()
    {
        if (_wheelCollider == null || _visualWheel == null)
            return;

        _wheelCollider.GetWorldPose(out _, out Quaternion wheelWorldRot);
        Quaternion relative = Quaternion.Inverse(wheelWorldRot) * _visualWheel.rotation;
        _visualEulerOffset = relative.eulerAngles;
    }

    private void OnDrawGizmos()
    {
        if (!_showBuildSpinArrow)
            return;

        if (Application.isPlaying)
        {
            if (_buildManager == null)
                _buildManager = FindFirstObjectByType<BuildManager>();

            if (_buildManager != null && _buildManager.IsSimulationMode)
                return;
        }

        Transform source = _visualWheel != null ? _visualWheel : transform;
        Vector3 origin = source.position;

        // Positive motor torque direction shown in build mode.
        Vector3 direction = (_invertDriveDirection ? -source.forward : source.forward).normalized;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        float len = Mathf.Max(0.05f, _buildArrowLength);
        Vector3 tip = origin + direction * len;

        Gizmos.color = _buildArrowColor;
        Gizmos.DrawLine(origin, tip);

        Vector3 side = Vector3.Cross(direction, Vector3.up);
        if (side.sqrMagnitude <= 0.0001f)
            side = Vector3.Cross(direction, Vector3.right);
        side.Normalize();

        Vector3 back = -direction * (len * 0.22f);
        Vector3 wing = side * (len * 0.14f);
        Gizmos.DrawLine(tip, tip + back + wing);
        Gizmos.DrawLine(tip, tip + back - wing);
    }
}

using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;
using System.IO;

public sealed class BuildManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlacementPreview _placementPreview;
    [SerializeField] private Camera _camera;

    [Header("Build Palette (V0)")]
    [SerializeField] private GameObject[] _partPrefabs;
    [SerializeField] private int _defaultSelectedIndex = 0;

    [Header("Core Spawn")]
    [SerializeField] private GameObject _corePrefab;
    [SerializeField] private Vector3 _coreSpawnPosition = new Vector3(0f, 1f, 0f);
    [SerializeField] private Quaternion _coreSpawnRotation = Quaternion.identity;

    [Header("Input")]
    [SerializeField] private bool _startPreviewOnEnable = false;
    [SerializeField] private Key _toggleSimulationKey = Key.Enter;
    [SerializeField] private Key _toggleCoreMoveModeKey = Key.M;
    [SerializeField] private Key _deletePartKey = Key.Delete;
    [SerializeField] private Key _undoPlaceKey = Key.U;
    [SerializeField] private Key _clearBuildKey = Key.C;
    [SerializeField] private Key _saveContraptionKey = Key.F5;
    [SerializeField] private Key _loadContraptionKey = Key.F9;

    [Header("Delete Part")]
    [SerializeField] private LayerMask _deletablePartLayer;
    [SerializeField] private float _deleteRayDistance = 300f;

    [Header("Core Move Mode")]
    [SerializeField, Min(0.1f)] private float _coreMoveSpeed = 5f;
    [SerializeField] private Key _coreMoveUpKey = Key.PageUp;
    [SerializeField] private Key _coreMoveDownKey = Key.PageDown;
    [SerializeField] private Key _coreMoveUpAltKey = Key.R;
    [SerializeField] private Key _coreMoveDownAltKey = Key.F;
    [SerializeField] private LayerMask _coreGroundLayer;
    [SerializeField, Min(0f)] private float _coreGroundClearance = 0.25f;
    [SerializeField, Min(0.1f)] private float _coreClampRayHeight = 10f;
    [SerializeField, Min(0.1f)] private float _coreClampRayDistance = 50f;
    [SerializeField] private float _fallbackGroundY = 0f;

    [Header("Drive (Simulation)")]
    [SerializeField] private Key _throttleForwardKey = Key.I;
    [SerializeField] private Key _throttleReverseKey = Key.K;
    [SerializeField] private Key _steerLeftKey = Key.J;
    [SerializeField] private Key _steerRightKey = Key.L;
    [SerializeField, Min(0f)] private float _motorForcePerMotor = 1200f;
    [SerializeField, Min(0f)] private float _steerTorquePerWheel = 150f;

    [Header("Runtime (read-only)")]
    [SerializeField] private int _selectedIndex = -1;
    [SerializeField] private bool _isSimulationMode;
    [SerializeField] private bool _isCoreMoveMode;

    public int SelectedIndex => _selectedIndex;
    private readonly Stack<Part> _placedHistory = new Stack<Part>();

    [Serializable]
    private struct PartSaveRecord
    {
        public int id;
        public string prefabKey;
        public PartType partType;
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    private struct ConnectionSaveRecord
    {
        public int partAId;
        public int nodeAIndex;
        public int partBId;
        public int nodeBIndex;
    }

    [Serializable]
    private sealed class ContraptionSaveData
    {
        public List<PartSaveRecord> parts = new List<PartSaveRecord>();
        public List<ConnectionSaveRecord> connections = new List<ConnectionSaveRecord>();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (_placementPreview != null)
            _placementPreview.PartPlaced += OnPartPlaced;

        SetSimulationMode(false);
        EnsureCoreExists();

        if (_startPreviewOnEnable)
        {
            SelectPartByIndex(_defaultSelectedIndex);
        }
    }

    private void OnDisable()
    {
        if (_placementPreview != null)
            _placementPreview.PartPlaced -= OnPartPlaced;
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[_toggleSimulationKey].wasPressedThisFrame)
        {
            SetSimulationMode(!_isSimulationMode);
        }

        if (!_isSimulationMode && Keyboard.current[_toggleCoreMoveModeKey].wasPressedThisFrame)
        {
            SetCoreMoveMode(!_isCoreMoveMode);
        }

        if (!_isSimulationMode)
        {
            if (Keyboard.current[_saveContraptionKey].wasPressedThisFrame)
                SaveContraption();

            if (Keyboard.current[_loadContraptionKey].wasPressedThisFrame)
                LoadContraption();
        }

        if (_isSimulationMode)
        {
            UpdateDriveInput();
            return;
        }

        if (_isCoreMoveMode)
        {
            UpdateCoreMoveInput();
            return;
        }

        if (!_isSimulationMode)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) SelectPartByIndex(0);
            if (Keyboard.current.digit2Key.wasPressedThisFrame) SelectPartByIndex(1);
            if (Keyboard.current.digit3Key.wasPressedThisFrame) SelectPartByIndex(2);
            if (Keyboard.current.digit4Key.wasPressedThisFrame) SelectPartByIndex(3);
            if (Keyboard.current.digit5Key.wasPressedThisFrame) SelectPartByIndex(4);
            if (Keyboard.current.digit6Key.wasPressedThisFrame) SelectPartByIndex(5);
            if (Keyboard.current.digit7Key.wasPressedThisFrame) SelectPartByIndex(6);
            if (Keyboard.current.digit8Key.wasPressedThisFrame) SelectPartByIndex(7);
            if (Keyboard.current.digit9Key.wasPressedThisFrame) SelectPartByIndex(8);

            if (Keyboard.current[_deletePartKey].wasPressedThisFrame)
            {
                TryDeleteHoveredPart();
            }

            if (Keyboard.current[_undoPlaceKey].wasPressedThisFrame)
            {
                TryUndoLastPlacedPart();
            }

            if (Keyboard.current[_clearBuildKey].wasPressedThisFrame)
            {
                ClearBuildExceptCore();
            }
        }

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CancelBuild();
        }
    }

    public void SelectPartByIndex(int index)
    {
        if (_placementPreview == null)
        {
            Debug.LogError("[BuildManager] PlacementPreview reference is missing.", this);
            return;
        }

        if (_partPrefabs == null || _partPrefabs.Length == 0)
        {
            Debug.LogWarning("[BuildManager] No part prefabs configured.", this);
            return;
        }

        if (index < 0 || index >= _partPrefabs.Length)
        {
            return;
        }

        GameObject prefab = _partPrefabs[index];
        if (prefab == null)
        {
            Debug.LogWarning($"[BuildManager] Part prefab at index {index} is null.", this);
            return;
        }

        _selectedIndex = index;
        _placementPreview.BeginPreview(prefab);
    }

    public void CancelBuild()
    {
        if (_placementPreview == null) return;
        _placementPreview.CancelPreview();
    }

    private void SetSimulationMode(bool enabled)
    {
        _isSimulationMode = enabled;
        if (enabled && _isCoreMoveMode)
            SetCoreMoveMode(false);

        Part[] parts = FindObjectsByType<Part>(FindObjectsSortMode.None);
        for (int i = 0; i < parts.Length; i++)
        {
            Part p = parts[i];
            if (p == null) continue;
            p.SetSimulationEnabled(enabled);
        }

        if (enabled)
        {
            CancelBuild();
        }
    }

    private void EnsureCoreExists()
    {
        Part[] parts = FindObjectsByType<Part>(FindObjectsSortMode.None);
        for (int i = 0; i < parts.Length; i++)
        {
            Part p = parts[i];
            if (p != null && p.PartType == PartType.Core)
            {
                if (_corePrefab != null && string.IsNullOrEmpty(p.RuntimePrefabKey))
                    p.SetRuntimePrefabKey(_corePrefab.name);
                return;
            }
        }

        if (_corePrefab == null)
        {
            Debug.LogWarning("[BuildManager] No core prefab assigned. Assign _corePrefab to bootstrap building from a core.", this);
            return;
        }

        GameObject go = Instantiate(_corePrefab, _coreSpawnPosition, _coreSpawnRotation);
        Part corePart = go.GetComponent<Part>();
        if (corePart == null)
        {
            Debug.LogError("[BuildManager] Core prefab must have a Part component on root.", go);
            return;
        }

        if (corePart.PartType != PartType.Core)
        {
            Debug.LogWarning($"[BuildManager] Spawned core prefab PartType is {corePart.PartType}, expected Core.", corePart);
        }

        corePart.SetRuntimePrefabKey(_corePrefab.name);
        // Core is a placed part in build mode (kinematic until simulation starts).
        corePart.FinalizePlacement(enableSimulation: false);
    }

    private void SetCoreMoveMode(bool enabled)
    {
        _isCoreMoveMode = enabled;

        if (enabled)
        {
            CancelBuild();
            Part core = FindCorePart();
            if (core != null)
            {
                Vector3 clamped = GetClampedCorePosition(core.transform.position);
                Vector3 delta = clamped - core.transform.position;
                if (delta != Vector3.zero)
                    MoveConnectedAssembly(core, delta);
            }
        }
        else if (!_isSimulationMode && _startPreviewOnEnable && _selectedIndex >= 0)
        {
            SelectPartByIndex(_selectedIndex);
        }
    }

    private void UpdateCoreMoveInput()
    {
        if (Keyboard.current == null) return;

        Part core = FindCorePart();
        if (core == null) return;

        Vector3 coreStartPos = core.transform.position;
        Vector3 move = Vector3.zero;

        if (Keyboard.current.upArrowKey.isPressed) move += Vector3.forward;
        if (Keyboard.current.downArrowKey.isPressed) move += Vector3.back;
        if (Keyboard.current.leftArrowKey.isPressed) move += Vector3.left;
        if (Keyboard.current.rightArrowKey.isPressed) move += Vector3.right;
        if (Keyboard.current[_coreMoveUpKey].isPressed || Keyboard.current[_coreMoveUpAltKey].isPressed)
            move += Vector3.up;
        if (Keyboard.current[_coreMoveDownKey].isPressed || Keyboard.current[_coreMoveDownAltKey].isPressed)
            move += Vector3.down;

        Vector3 desiredCorePos = coreStartPos;
        if (move != Vector3.zero)
            desiredCorePos += move.normalized * (_coreMoveSpeed * Time.deltaTime);

        Vector3 clampedCorePos = GetClampedCorePosition(desiredCorePos);
        Vector3 delta = clampedCorePos - coreStartPos;
        if (delta == Vector3.zero) return;

        MoveConnectedAssembly(core, delta);
    }

    private static Part FindCorePart()
    {
        Part[] parts = FindObjectsByType<Part>(FindObjectsSortMode.None);
        for (int i = 0; i < parts.Length; i++)
        {
            Part p = parts[i];
            if (p != null && p.PartType == PartType.Core)
                return p;
        }

        return null;
    }

    private Vector3 GetClampedCorePosition(Vector3 desiredCorePos)
    {
        if (_coreGroundLayer.value == 0)
        {
            float fallbackMinY = _fallbackGroundY + _coreGroundClearance;
            if (desiredCorePos.y < fallbackMinY)
                return new Vector3(desiredCorePos.x, fallbackMinY, desiredCorePos.z);
            return desiredCorePos;
        }

        Vector3 rayOrigin = desiredCorePos + Vector3.up * _coreClampRayHeight;

        if (!Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                _coreClampRayDistance,
                _coreGroundLayer,
                QueryTriggerInteraction.Ignore))
        {
            float fallbackMinY = _fallbackGroundY + _coreGroundClearance;
            if (desiredCorePos.y < fallbackMinY)
                return new Vector3(desiredCorePos.x, fallbackMinY, desiredCorePos.z);
            return desiredCorePos;
        }

        float minY = hit.point.y + _coreGroundClearance;
        if (desiredCorePos.y >= minY) return desiredCorePos;

        return new Vector3(desiredCorePos.x, minY, desiredCorePos.z);
    }

    private static void MoveConnectedAssembly(Part core, Vector3 delta)
    {
        var connectedParts = CollectConnectedParts(core);
        foreach (Part part in connectedParts)
        {
            if (part == null) continue;
            part.transform.position += delta;
        }
    }

    private static HashSet<Part> CollectConnectedParts(Part root)
    {
        var visited = new HashSet<Part>();
        if (root == null) return visited;

        var queue = new Queue<Part>();
        visited.Add(root);
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            Part current = queue.Dequeue();
            var nodes = current.GetNodes();
            if (nodes == null) continue;

            for (int i = 0; i < nodes.Count; i++)
            {
                ConnectionNode node = nodes[i];
                if (node == null) continue;

                ConnectionNode other = node.ConnectedTo;
                if (other == null || other.Owner == null) continue;

                if (visited.Add(other.Owner))
                    queue.Enqueue(other.Owner);
            }
        }

        return visited;
    }

    private void UpdateDriveInput()
    {
        if (Keyboard.current == null) return;

        Part core = FindCorePart();
        if (core == null) return;

        Rigidbody coreRb = core.GetRigidbody();
        if (coreRb == null) return;
        if (coreRb.isKinematic) return;

        float throttle = 0f;
        if (Keyboard.current[_throttleForwardKey].isPressed) throttle += 1f;
        if (Keyboard.current[_throttleReverseKey].isPressed) throttle -= 1f;

        float steer = 0f;
        if (Keyboard.current[_steerRightKey].isPressed) steer += 1f;
        if (Keyboard.current[_steerLeftKey].isPressed) steer -= 1f;

        if (Mathf.Approximately(throttle, 0f) && Mathf.Approximately(steer, 0f))
            return;

        var assembly = CollectConnectedParts(core);
        int motorCount = 0;
        int wheelCount = 0;

        foreach (Part part in assembly)
        {
            if (part == null) continue;
            if (part.PartType == PartType.Motor) motorCount++;
            if (part.PartType == PartType.Wheel) wheelCount++;
        }

        if (motorCount <= 0 || wheelCount <= 0) return;

        Vector3 forward = Vector3.ProjectOnPlane(core.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude <= 0.0001f) return;

        float motorForce = throttle * _motorForcePerMotor * motorCount;
        float steerTorque = steer * _steerTorquePerWheel * wheelCount;

        coreRb.AddForce(forward * motorForce, ForceMode.Force);
        coreRb.AddTorque(Vector3.up * steerTorque, ForceMode.Force);
    }

    private void TryDeleteHoveredPart()
    {
        if (_camera == null || Mouse.current == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(mousePos);
        if (!Physics.Raycast(ray, out RaycastHit hit, _deleteRayDistance, _deletablePartLayer, QueryTriggerInteraction.Ignore))
            return;

        Part part = hit.collider != null ? hit.collider.GetComponentInParent<Part>() : null;
        if (part == null) return;
        if (part.PartType == PartType.Core) return;

        Rigidbody rb = part.GetRigidbody();
        if (rb != null && !rb.detectCollisions)
            return; // likely preview object, never delete through this path

        var nodes = part.GetNodes();
        if (nodes != null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                ConnectionNode node = nodes[i];
                if (node == null) continue;
                node.ClearConnection();
            }
        }

        Destroy(part.gameObject);
    }

    private void OnPartPlaced(Part part)
    {
        if (part == null) return;
        if (part.PartType == PartType.Core) return;
        _placedHistory.Push(part);
    }

    private void TryUndoLastPlacedPart()
    {
        while (_placedHistory.Count > 0)
        {
            Part part = _placedHistory.Pop();
            if (part == null) continue;
            if (part.PartType == PartType.Core) continue;

            var nodes = part.GetNodes();
            if (nodes != null)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    ConnectionNode node = nodes[i];
                    if (node == null) continue;
                    node.ClearConnection();
                }
            }

            Destroy(part.gameObject);
            return;
        }
    }

    private void SaveContraption()
    {
        var data = new ContraptionSaveData();
        Part[] parts = FindObjectsByType<Part>(FindObjectsSortMode.None);
        var partToId = new Dictionary<Part, int>();

        for (int i = 0; i < parts.Length; i++)
        {
            Part part = parts[i];
            if (part == null) continue;

            int id = data.parts.Count;
            partToId[part] = id;

            string prefabKey = string.IsNullOrEmpty(part.RuntimePrefabKey)
                ? ResolvePrefabKeyForPart(part)
                : part.RuntimePrefabKey;

            data.parts.Add(new PartSaveRecord
            {
                id = id,
                prefabKey = prefabKey,
                partType = part.PartType,
                position = part.transform.position,
                rotation = part.transform.rotation
            });
        }

        for (int p = 0; p < parts.Length; p++)
        {
            Part part = parts[p];
            if (part == null) continue;
            if (!partToId.TryGetValue(part, out int partId)) continue;

            var nodes = part.GetNodes();
            if (nodes == null) continue;

            for (int n = 0; n < nodes.Count; n++)
            {
                ConnectionNode node = nodes[n];
                if (node == null || node.ConnectedTo == null || node.ConnectedTo.Owner == null) continue;

                Part otherPart = node.ConnectedTo.Owner;
                if (!partToId.TryGetValue(otherPart, out int otherId)) continue;

                var otherNodes = otherPart.GetNodes();
                int otherNodeIndex = IndexOfNode(otherNodes, node.ConnectedTo);
                if (otherNodeIndex < 0) continue;

                bool isDuplicate = partId > otherId || (partId == otherId && n > otherNodeIndex);
                if (isDuplicate) continue;

                data.connections.Add(new ConnectionSaveRecord
                {
                    partAId = partId,
                    nodeAIndex = n,
                    partBId = otherId,
                    nodeBIndex = otherNodeIndex
                });
            }
        }

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(GetSavePath(), json);
        Debug.Log($"[BuildManager] Contraption saved to {GetSavePath()}", this);
    }

    private void LoadContraption()
    {
        string savePath = GetSavePath();
        if (!File.Exists(savePath))
        {
            Debug.LogWarning($"[BuildManager] No save file found at {savePath}", this);
            return;
        }

        CancelBuild();
        SetSimulationMode(false);
        ClearAllParts();
        _placedHistory.Clear();

        string json = File.ReadAllText(savePath);
        ContraptionSaveData data = JsonUtility.FromJson<ContraptionSaveData>(json);
        if (data == null || data.parts == null || data.parts.Count == 0)
        {
            Debug.LogWarning("[BuildManager] Save file is empty or invalid.", this);
            return;
        }

        var prefabByKey = BuildPrefabLookup();
        var idToPart = new Dictionary<int, Part>();

        for (int i = 0; i < data.parts.Count; i++)
        {
            PartSaveRecord record = data.parts[i];
            GameObject prefab = ResolvePrefabForRecord(record, prefabByKey);
            if (prefab == null)
            {
                Debug.LogWarning($"[BuildManager] Missing prefab for save record id={record.id}, key='{record.prefabKey}', type={record.partType}.", this);
                continue;
            }

            GameObject go = Instantiate(prefab, record.position, record.rotation);
            Part part = go.GetComponent<Part>();
            if (part == null)
            {
                Destroy(go);
                continue;
            }

            part.SetRuntimePrefabKey(prefab.name);
            part.InitializeForPlacement();
            part.FinalizePlacement(enableSimulation: false);
            idToPart[record.id] = part;
        }

        if (data.connections != null)
        {
            for (int i = 0; i < data.connections.Count; i++)
            {
                ConnectionSaveRecord c = data.connections[i];
                if (!idToPart.TryGetValue(c.partAId, out Part a)) continue;
                if (!idToPart.TryGetValue(c.partBId, out Part b)) continue;

                var aNodes = a.GetNodes();
                var bNodes = b.GetNodes();
                if (aNodes == null || bNodes == null) continue;
                if (c.nodeAIndex < 0 || c.nodeAIndex >= aNodes.Count) continue;
                if (c.nodeBIndex < 0 || c.nodeBIndex >= bNodes.Count) continue;

                ConnectionNode nodeA = aNodes[c.nodeAIndex];
                ConnectionNode nodeB = bNodes[c.nodeBIndex];
                TryCreateConnectionFromLoad(nodeA, nodeB);
            }
        }

        EnsureCoreExists();
        Debug.Log($"[BuildManager] Contraption loaded from {savePath}", this);
    }

    private void TryCreateConnectionFromLoad(ConnectionNode nodeA, ConnectionNode nodeB)
    {
        if (nodeA == null || nodeB == null) return;
        if (!nodeA.CanConnectTo(nodeB)) return;

        Rigidbody rbA = nodeA.Owner != null ? nodeA.Owner.GetRigidbody() : null;
        Rigidbody rbB = nodeB.Owner != null ? nodeB.Owner.GetRigidbody() : null;
        if (rbA == null || rbB == null) return;

        FixedJoint joint = rbA.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = rbB;
        joint.anchor = rbA.transform.InverseTransformPoint(nodeA.GetWorldPosition());
        joint.connectedAnchor = rbB.transform.InverseTransformPoint(nodeB.GetWorldPosition());
        joint.autoConfigureConnectedAnchor = false;
        joint.enableCollision = false;

        nodeA.MarkConnected(nodeB);
    }

    private Dictionary<string, GameObject> BuildPrefabLookup()
    {
        var dict = new Dictionary<string, GameObject>(StringComparer.Ordinal);

        if (_corePrefab != null)
            dict[_corePrefab.name] = _corePrefab;

        if (_partPrefabs != null)
        {
            for (int i = 0; i < _partPrefabs.Length; i++)
            {
                GameObject prefab = _partPrefabs[i];
                if (prefab == null) continue;
                dict[prefab.name] = prefab;
            }
        }

        return dict;
    }

    private GameObject ResolvePrefabForRecord(PartSaveRecord record, Dictionary<string, GameObject> prefabByKey)
    {
        if (!string.IsNullOrEmpty(record.prefabKey) && prefabByKey.TryGetValue(record.prefabKey, out GameObject byKey))
            return byKey;

        if (record.partType == PartType.Core)
            return _corePrefab;

        if (_partPrefabs == null) return null;

        for (int i = 0; i < _partPrefabs.Length; i++)
        {
            GameObject prefab = _partPrefabs[i];
            if (prefab == null) continue;

            Part part = prefab.GetComponent<Part>();
            if (part != null && part.PartType == record.partType)
                return prefab;
        }

        return null;
    }

    private string ResolvePrefabKeyForPart(Part part)
    {
        if (part == null) return string.Empty;
        if (part.PartType == PartType.Core && _corePrefab != null) return _corePrefab.name;

        if (_partPrefabs != null)
        {
            for (int i = 0; i < _partPrefabs.Length; i++)
            {
                GameObject prefab = _partPrefabs[i];
                if (prefab == null) continue;
                Part prefabPart = prefab.GetComponent<Part>();
                if (prefabPart != null && prefabPart.PartType == part.PartType)
                    return prefab.name;
            }
        }

        return part.PartType.ToString();
    }

    private static int IndexOfNode(IReadOnlyList<ConnectionNode> nodes, ConnectionNode target)
    {
        if (nodes == null || target == null) return -1;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] == target) return i;
        }

        return -1;
    }

    private static void ClearAllParts()
    {
        Part[] parts = FindObjectsByType<Part>(FindObjectsSortMode.None);
        for (int i = 0; i < parts.Length; i++)
        {
            Part part = parts[i];
            if (part == null) continue;
            Destroy(part.gameObject);
        }
    }

    private void ClearBuildExceptCore()
    {
        CancelBuild();

        Part[] parts = FindObjectsByType<Part>(FindObjectsSortMode.None);
        for (int i = 0; i < parts.Length; i++)
        {
            Part part = parts[i];
            if (part == null) continue;
            if (part.PartType == PartType.Core) continue;

            var nodes = part.GetNodes();
            if (nodes != null)
            {
                for (int n = 0; n < nodes.Count; n++)
                {
                    ConnectionNode node = nodes[n];
                    if (node == null) continue;
                    node.ClearConnection();
                }
            }

            Destroy(part.gameObject);
        }

        _placedHistory.Clear();

        if (!_isSimulationMode && _selectedIndex >= 0)
            SelectPartByIndex(_selectedIndex);
    }

    private static string GetSavePath()
    {
        return Path.Combine(Application.persistentDataPath, "contraption.json");
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

public sealed class FreeCameraController : MonoBehaviour
{
    [SerializeField] private float _moveSpeed = 10f;
    [SerializeField] private float _lookSpeed = 0.15f;
    [SerializeField] private float _fastMultiplier = 3f;

    private float _yaw;
    private float _pitch;

    private void Awake()
    {
        Vector3 e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = e.x;
    }

    private void Update()
    {
        if (Keyboard.current == null) return;
        if (Mouse.current == null) return;

        bool lookHeld = Mouse.current.rightButton.isPressed;

        // Look
        if (lookHeld)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            _yaw += delta.x * _lookSpeed;
            _pitch -= delta.y * _lookSpeed;
            _pitch = Mathf.Clamp(_pitch, -85f, 85f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        // Move
        float speed = _moveSpeed;
        if (Keyboard.current.leftShiftKey.isPressed) speed *= _fastMultiplier;

        Vector3 dir = Vector3.zero;
        if (Keyboard.current.wKey.isPressed) dir += Vector3.forward;
        if (Keyboard.current.sKey.isPressed) dir += Vector3.back;
        if (Keyboard.current.aKey.isPressed) dir += Vector3.left;
        if (Keyboard.current.dKey.isPressed) dir += Vector3.right;
        if (Keyboard.current.eKey.isPressed) dir += Vector3.up;
        if (Keyboard.current.qKey.isPressed) dir += Vector3.down;

        if (dir.sqrMagnitude > 0f)
        {
            Vector3 move = transform.TransformDirection(dir.normalized) * (speed * Time.deltaTime);
            transform.position += move;
        }

        // Optional: mouse wheel changes speed
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            _moveSpeed = Mathf.Clamp(_moveSpeed + scroll * 0.01f, 1f, 50f);
        }
    }
}

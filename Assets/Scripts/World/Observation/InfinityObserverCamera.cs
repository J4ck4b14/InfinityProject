using UnityEngine;
using UnityEngine.InputSystem;

namespace InfinityProject.World.Observation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class InfinityObserverCamera : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _moveSpeed = 18f;
        [SerializeField, Min(1f)] private float _fastMultiplier = 5f;
        [SerializeField, Range(0.05f, 1f)] private float _slowMultiplier = 0.2f;
        [SerializeField, Min(0.01f)] private float _lookSensitivity = 0.12f;
        [SerializeField, Min(0.1f)] private float _wheelSpeedStep = 3f;

        private float _yaw;
        private float _pitch;

        private void Awake()
        {
            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = NormalizePitch(euler.x);

            Camera camera = GetComponent<Camera>();
            camera.useOcclusionCulling = true;
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, 3000f);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null || mouse == null) return;

            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
                _moveSpeed = Mathf.Clamp(_moveSpeed + Mathf.Sign(wheel) * _wheelSpeedStep, 0.5f, 250f);

            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * _lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * _lookSensitivity, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Vector3 input = Vector3.zero;
            if (keyboard.wKey.isPressed) input += Vector3.forward;
            if (keyboard.sKey.isPressed) input += Vector3.back;
            if (keyboard.dKey.isPressed) input += Vector3.right;
            if (keyboard.aKey.isPressed) input += Vector3.left;
            if (keyboard.eKey.isPressed) input += Vector3.up;
            if (keyboard.qKey.isPressed) input += Vector3.down;
            if (input.sqrMagnitude > 1f) input.Normalize();

            float speed = _moveSpeed;
            if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) speed *= _fastMultiplier;
            if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed) speed *= _slowMultiplier;

            Vector3 worldMove = transform.TransformDirection(new Vector3(input.x, 0f, input.z));
            worldMove += Vector3.up * input.y;
            transform.position += worldMove * (speed * Time.unscaledDeltaTime);
        }

        private static float NormalizePitch(float degrees)
        {
            return degrees > 180f ? degrees - 360f : degrees;
        }
    }
}

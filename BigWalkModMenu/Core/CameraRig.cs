using System;
using UnityEngine;

namespace BigWalk.ModMenu
{
    /// <summary>
    /// Takes the player's camera off the head so a module can place it anywhere (selfie, photo mode,
    /// replay) and puts it back afterwards. The camera object itself is kept: sky, culling and
    /// post-processing all stay attached to it. One owner at a time.
    /// </summary>
    public static class CameraRig
    {
        private static object _owner;
        private static Camera _cam;
        private static Transform _parent;
        private static Vector3 _localPosition;
        private static Quaternion _localRotation;
        private static float _fov;
        private static bool _bodyWasLocal;
        private static PlayerCharacter _pc;

        private static Vector3 _position;
        private static Quaternion _rotation = Quaternion.identity;
        private static float _targetFov;
        private static Vector3 _lastMouse;

        public static bool Active => _owner != null;
        public static object Owner => _owner;
        public static Camera Cam => _cam;
        /// <summary>The head pivot the camera normally sits on: its forward is where the player looks.</summary>
        public static Transform Pivot => _parent;
        public static float OriginalFov => _fov;
        public static Vector3 Position => _position;
        public static Quaternion Rotation => _rotation;

        /// <summary>Detaches the camera for this owner. A previous owner is released first.</summary>
        public static bool Acquire(object owner)
        {
            if (ReferenceEquals(_owner, owner)) return true;
            if (_owner != null) Release(_owner);

            Camera cam = Game.PlayerCamera;
            PlayerCharacter pc = Game.LocalPlayer;
            if (cam == null || pc == null) return false;

            _cam = cam;
            _pc = pc;
            _parent = cam.transform.parent;
            _localPosition = cam.transform.localPosition;
            _localRotation = cam.transform.localRotation;
            _fov = cam.fieldOfView;
            _position = cam.transform.position;
            _rotation = cam.transform.rotation;
            _targetFov = _fov;
            cam.transform.SetParent(null, true);

            // In first person the game hides parts of your own body: show it as others see it.
            _bodyWasLocal = false;
            try
            {
                if (pc.looks != null && pc.looks.currentLookType == PlayerLooks.LookType.Local)
                {
                    _bodyWasLocal = true;
                    pc.looks.SetBodyToRemoteMode();
                }
            }
            catch (Exception ex) { ModMenuApi.LogOnce("rig-body", $"[CameraRig] Could not show the full body: {ex.Message}"); }

            _owner = owner;
            _lastMouse = Input.mousePosition;
            ModMenuApi.Log.LogInfo($"[CameraRig] Camera '{cam.name}' detached from '{(_parent != null ? _parent.name : "<none>")}'.");
            return true;
        }

        public static void Release(object owner)
        {
            if (_owner == null || !ReferenceEquals(_owner, owner)) return;
            _owner = null;
            try
            {
                if (_cam != null)
                {
                    if (_parent != null)
                    {
                        _cam.transform.SetParent(_parent, false);
                        _cam.transform.localPosition = _localPosition;
                        _cam.transform.localRotation = _localRotation;
                    }
                    _cam.fieldOfView = _fov;
                }
                if (_bodyWasLocal && _pc != null && _pc.looks != null) _pc.looks.SetBodyToLocalMode();
            }
            catch (Exception ex) { ModMenuApi.Log.LogWarning($"[CameraRig] Restore failed: {ex.Message}"); }
            _cam = null;
            _pc = null;
            _parent = null;
        }

        public static void SetPose(Vector3 position, Quaternion rotation, float fov)
        {
            _position = position;
            _rotation = rotation;
            _targetFov = Mathf.Clamp(fov, 5f, 150f);
        }

        /// <summary>Called by the host at the end of LateUpdate, after the game has moved everything.</summary>
        internal static void Apply()
        {
            if (_owner == null) return;
            // The player left the world (or the camera was destroyed): nothing to restore to.
            if (_cam == null || _pc == null)
            {
                _owner = null;
                _cam = null;
                _pc = null;
                _parent = null;
                return;
            }
            if (_cam.transform.parent != null) _cam.transform.SetParent(null, true);
            _cam.transform.SetPositionAndRotation(_position, _rotation);
            _cam.fieldOfView = _targetFov;
        }

        /// <summary>
        /// Free-flight controls: WASD to move, Q/E down/up, Shift fast, Ctrl slow, hold the right mouse
        /// button to look around, wheel for the field of view.
        /// </summary>
        public static void FreeFly(ref Vector3 position, ref float yaw, ref float pitch, ref float fov, float speed)
        {
            float dt = Time.unscaledDeltaTime;
            Vector3 mouse = Input.mousePosition;
            if (Game.IsTyping) { _lastMouse = mouse; return; }
            if (Input.GetMouseButton(1))
            {
                Vector3 delta = mouse - _lastMouse;
                float sensitivity = 0.18f * Mathf.Clamp(fov / 60f, 0.2f, 1.5f);
                yaw += delta.x * sensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * sensitivity, -89f, 89f);
            }
            _lastMouse = mouse;

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) move += Vector3.back;
            if (Input.GetKey(KeyCode.D)) move += Vector3.right;
            if (Input.GetKey(KeyCode.A)) move += Vector3.left;
            Vector3 world = rot * move;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) world += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) world += Vector3.down;

            float k = speed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) k *= 5f;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) k *= 0.2f;
            position += world * (k * dt);

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && !ModMenuApi.IsMenuOpen) fov = Mathf.Clamp(fov - wheel * 3f, 8f, 120f);
        }
    }
}

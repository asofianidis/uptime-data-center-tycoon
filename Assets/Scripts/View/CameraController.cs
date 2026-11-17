using UnityEngine;

namespace DCT
{
    /// <summary>Top-down management camera: WASD/arrows/edge pan, middle-drag pan, scroll zoom, Q/E rotate.</summary>
    public class CameraController : MonoBehaviour
    {
        public Vector3 focus = new Vector3(32, 0, 24);
        public float distance = 22f;
        public float yaw = 0f;
        public float pitch = 58f;
        public float minDist = 6f, maxDist = 70f;
        public Vector2 boundsMin = Vector2.zero, boundsMax = new Vector2(64, 48);
        public bool inputEnabled = true;
        public bool blockScroll;

        float targetYaw, targetDist;
        Vector3 lastMouse;
        Camera cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            targetYaw = yaw;
            targetDist = distance;
        }

        public void SnapTo(Vector3 f, float dist)
        {
            focus = f; distance = targetDist = dist;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (inputEnabled)
            {
                Vector3 fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                Vector3 right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                Vector3 move = Vector3.zero;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move += fwd;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move -= fwd;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += right;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move -= right;
                float speed = (8f + distance * 0.9f) * (Input.GetKey(KeyCode.LeftShift) ? 2f : 1f);
                focus += move.normalized * speed * dt;

                if (Input.GetMouseButtonDown(2)) lastMouse = Input.mousePosition;
                if (Input.GetMouseButton(2))
                {
                    Vector3 delta = Input.mousePosition - lastMouse;
                    lastMouse = Input.mousePosition;
                    float k = distance * 0.0018f;
                    focus -= (right * delta.x + fwd * delta.y) * k;
                }

                if (!blockScroll)
                {
                    float scroll = Input.mouseScrollDelta.y;
                    if (Mathf.Abs(scroll) > 0.01f) targetDist = Mathf.Clamp(targetDist * Mathf.Pow(0.88f, scroll), minDist, maxDist);
                }
                if (Input.GetKeyDown(KeyCode.Q)) targetYaw -= 90f;
                if (Input.GetKeyDown(KeyCode.E)) targetYaw += 90f;
                if (Input.GetKey(KeyCode.PageUp)) pitch = Mathf.Clamp(pitch + 40f * dt, 35f, 85f);
                if (Input.GetKey(KeyCode.PageDown)) pitch = Mathf.Clamp(pitch - 40f * dt, 35f, 85f);
            }
            focus.x = Mathf.Clamp(focus.x, boundsMin.x, boundsMax.x);
            focus.z = Mathf.Clamp(focus.z, boundsMin.y, boundsMax.y);
            yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-12f * dt));
            distance = Mathf.Lerp(distance, targetDist, 1f - Mathf.Exp(-10f * dt));
            Apply();
        }

        public void Apply()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
            transform.position = focus - rot * Vector3.forward * distance;
            transform.rotation = rot;
        }

        /// <summary>Raycast the mouse onto the ground plane (y = 0).</summary>
        public bool MouseGround(out Vector3 p)
        {
            p = Vector3.zero;
            if (cam == null) cam = GetComponent<Camera>();
            Ray r = cam.ScreenPointToRay(Input.mousePosition);
            if (Mathf.Abs(r.direction.y) < 1e-4f) return false;
            float t = -r.origin.y / r.direction.y;
            if (t < 0) return false;
            p = r.origin + r.direction * t;
            return true;
        }
    }
}

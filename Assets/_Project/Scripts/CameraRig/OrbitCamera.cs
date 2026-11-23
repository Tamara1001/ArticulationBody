using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Station.CameraRig
{
    /// <summary>
    /// Cámara orbital: botón derecho (o izquierdo sobre el fondo) para girar, rueda para zoom,
    /// botón medio para desplazar. Tiene vistas predefinidas con transición suave.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        [Serializable]
        public struct View
        {
            public string name;
            public Vector3 pivot;
            public float yaw, pitch, distance;
        }

        [SerializeField] private View[] views =
        {
            new View { name = "General", pivot = new Vector3(0.2f, 1.0f, -0.6f), yaw = -38f, pitch = 28f, distance = 7.2f },
            new View { name = "Admisión", pivot = new Vector3(0f, 0.95f, -1.1f), yaw = -65f, pitch = 22f, distance = 3.2f },
            new View { name = "Clasificador", pivot = new Vector3(0f, 1.25f, 0f), yaw = -20f, pitch = 18f, distance = 4.0f },
            new View { name = "Salidas", pivot = new Vector3(0f, 1.1f, 0f), yaw = 0f, pitch = 12f, distance = 7.5f },
        };
        [SerializeField] private float orbitSpeed = 0.25f;
        [SerializeField] private float zoomStep = 0.1f;
        [SerializeField] private float smoothing = 8f;
        [SerializeField] private Vector2 pitchRange = new(5f, 80f);
        [SerializeField] private Vector2 distanceRange = new(1.5f, 14f);

        /// <summary>Devuelve true si el puntero está sobre la UI (la cámara no debe reaccionar).</summary>
        public Func<Vector2, bool> IsPointerOverUI;

        private Vector3 pivot, goalPivot;
        private float yaw, pitch, distance, goalYaw, goalPitch, goalDistance;
        private bool dragging;
        private int viewIndex;

        public string CurrentViewName => views[viewIndex].name;
        public int ViewCount => views.Length;

        private void Start()
        {
            SetView(0, true);
        }

        public void CycleView() => SetView((viewIndex + 1) % views.Length, false);

        public void SetView(int index, bool instant)
        {
            viewIndex = Mathf.Clamp(index, 0, views.Length - 1);
            var v = views[viewIndex];
            goalPivot = v.pivot; goalYaw = v.yaw; goalPitch = v.pitch; goalDistance = v.distance;
            if (instant) { pivot = goalPivot; yaw = goalYaw; pitch = goalPitch; distance = goalDistance; Apply(); }
        }

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 pos = mouse.position.ReadValue();
                bool overUI = IsPointerOverUI != null && IsPointerOverUI(pos);

                bool orbitPressed = mouse.rightButton.wasPressedThisFrame || mouse.leftButton.wasPressedThisFrame;
                if (orbitPressed && !overUI) dragging = true;
                if (!mouse.rightButton.isPressed && !mouse.leftButton.isPressed) dragging = false;

                Vector2 delta = mouse.delta.ReadValue();
                if (dragging)
                {
                    goalYaw += delta.x * orbitSpeed;
                    goalPitch = Mathf.Clamp(goalPitch - delta.y * orbitSpeed, pitchRange.x, pitchRange.y);
                }
                if (mouse.middleButton.isPressed && !overUI)
                {
                    float k = goalDistance * 0.0015f;
                    goalPivot -= (transform.right * delta.x + transform.up * delta.y) * k;
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !overUI)
                    // Paso fijo por "clic" de rueda: el valor crudo difiere entre Editor y navegador
                    goalDistance = Mathf.Clamp(goalDistance * (1f - Mathf.Sign(scroll) * zoomStep), distanceRange.x, distanceRange.y);
            }

            float t = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            pivot = Vector3.Lerp(pivot, goalPivot, t);
            yaw = Mathf.LerpAngle(yaw, goalYaw, t);
            pitch = Mathf.Lerp(pitch, goalPitch, t);
            distance = Mathf.Lerp(distance, goalDistance, t);
            Apply();
        }

        private void Apply()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot - rot * Vector3.forward * distance, rot);
        }
    }
}

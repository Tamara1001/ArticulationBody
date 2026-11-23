using UnityEngine;

namespace Station.Mechanism
{
    /// <summary>
    /// Lee un joint pasivo (sin stiffness). En modo palpador mide la altura de la caja que lo levanta:
    /// distancia de la bola al plano de la rampa menos el radio de la bola.
    /// </summary>
    [RequireComponent(typeof(ArticulationBody))]
    public class PassiveJointProbe : MonoBehaviour
    {
        [SerializeField] private string displayName = "Pasivo";
        [SerializeField] private bool isHeightProbe = false;

        [Header("Palpador")]
        [SerializeField] private Transform tip;                 // centro de la bola
        [SerializeField] private float tipRadius = 0.03f;
        [SerializeField] private Vector3 surfacePoint;          // un punto de la superficie de apoyo de la caja
        [SerializeField] private Vector3 surfaceNormal = Vector3.up;
        [SerializeField] private float contactHeight = 0.12f;   // por encima de esto hay una caja debajo
        [SerializeField] private float stableSpeed = 25f;       // °/s: por debajo, la lectura es estable
        [SerializeField] private float stableTime = 0.08f;

        private ArticulationBody body;
        private float angle;
        private float angularSpeed;
        private float liveHeight;
        private float heldHeight = -1f;
        private float stableTimer;

        public string DisplayName => displayName;
        public float Angle => angle;
        public float AngularSpeed => angularSpeed;
        public bool InContact => isHeightProbe && liveHeight > contactHeight;
        public float LiveHeight => liveHeight;
        /// <summary>Última altura estable medida (−1 si todavía no hay lectura).</summary>
        public float HeldHeight => heldHeight;
        public bool IsStable => InContact && stableTimer >= stableTime;
        public bool IsHeightProbe => isHeightProbe;

        public void Configure(string name, bool heightProbe, Transform tipTransform = null, float radius = 0.03f,
            Vector3 planePoint = default, Vector3 planeNormal = default)
        {
            displayName = name; isHeightProbe = heightProbe; tip = tipTransform; tipRadius = radius;
            surfacePoint = planePoint;
            surfaceNormal = planeNormal == Vector3.zero ? Vector3.up : planeNormal.normalized;
        }

        private void Awake() => body = GetComponent<ArticulationBody>();

        /// <summary>Paso manual para pruebas automáticas con Physics.Simulate (sin bucle de juego).</summary>
        public void ManualFixedUpdate() => FixedUpdate();

        private void FixedUpdate()
        {
            if (body.dofCount == 0) return;
            // Ángulo total: norma de los grados de libertad (Spherical) o el único (Revolute)
            float q = 0f, w = 0f;
            for (int i = 0; i < body.dofCount; i++)
            {
                q += body.jointPosition[i] * body.jointPosition[i];
                w += body.jointVelocity[i] * body.jointVelocity[i];
            }
            angle = Mathf.Sqrt(q) * Mathf.Rad2Deg;
            if (body.dofCount == 1) angle *= Mathf.Sign(body.jointPosition[0]);
            angularSpeed = Mathf.Sqrt(w) * Mathf.Rad2Deg;
            if (!isHeightProbe || tip == null) return;

            liveHeight = Vector3.Dot(tip.position - surfacePoint, surfaceNormal) - tipRadius;

            // Lectura estable: hay caja debajo y el péndulo casi no se mueve
            if (InContact && angularSpeed < stableSpeed)
            {
                stableTimer += Time.fixedDeltaTime;
                if (stableTimer >= stableTime) heldHeight = liveHeight;
            }
            else stableTimer = 0f;
        }
    }
}

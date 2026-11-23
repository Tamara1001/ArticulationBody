using UnityEngine;

namespace Station.Mechanism
{
    /// <summary>
    /// Balanza virtual: usa la fuerza que hace el drive del carro (ArticulationBody.driveForce).
    /// Con el carro quieto, esa fuerza iguala el peso de todo lo que cuelga de él.
    /// La tara es el peso propio del mecanismo (suma de masas del carro y sus hijos).
    /// </summary>
    public class LoadCell : MonoBehaviour
    {
        [SerializeField] private JointDriveController lift;
        [SerializeField] private float filter = 5f;        // 1/s, filtro pasa-bajos
        [SerializeField] private float warmup = 0.5f;      // s hasta la primera lectura

        private float filteredForce;
        private float tareMass;
        private float timer;

        public bool IsReady => timer > warmup;
        /// <summary>El carro está quieto y el target alcanzado: la lectura es confiable.</summary>
        public bool IsStable => IsReady && Mathf.Abs(lift.ActualVelocity) < 0.02f && Mathf.Abs(lift.Target - lift.Command) < 1e-4f;
        public float MassKg { get; private set; }
        public float TareKg => tareMass;

        public void Configure(JointDriveController liftJoint) => lift = liftJoint;

        private void Start()
        {
            tareMass = 0f;
            foreach (var ab in lift.GetComponentsInChildren<ArticulationBody>()) tareMass += ab.mass;
        }

        /// <summary>Paso manual para pruebas automáticas con Physics.Simulate (sin bucle de juego).</summary>
        public void ManualFixedUpdate() => FixedUpdate();

        private void FixedUpdate()
        {
            if (lift == null) return;
            timer += Time.fixedDeltaTime;
            filteredForce = Mathf.Lerp(filteredForce, lift.AppliedForce, 1f - Mathf.Exp(-filter * Time.fixedDeltaTime));
            float m = filteredForce / Physics.gravity.magnitude - tareMass;
            MassKg = m < 0.3f ? 0f : m; // zona muerta: ruido de ±0.3 kg se muestra como 0
        }
    }
}

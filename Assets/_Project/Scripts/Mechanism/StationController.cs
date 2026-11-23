using UnityEngine;
using UnityEngine.SceneManagement;

namespace Station.Mechanism
{
    /// <summary>
    /// Punto de acceso único al mecanismo: joints, sensores, enclavamientos,
    /// prueba en seco y reinicio. La entrada y la UI hablan solo con esta clase.
    /// </summary>
    public class StationController : MonoBehaviour
    {
        [Header("Drives")]
        [SerializeField] private JointDriveController gate;      // Tope
        [SerializeField] private JointDriveController roller;    // Rodillo
        [SerializeField] private JointDriveController lift;      // Carro
        [SerializeField] private JointDriveController turntable; // Mesa
        [SerializeField] private JointDriveController pusher;    // Empujador

        [Header("Sensores")]
        [SerializeField] private PassiveJointProbe heightProbe;  // Palpador
        [SerializeField] private PassiveJointProbe flap;         // Compuerta
        [SerializeField] private LoadCell loadCell;

        [Header("Colisiones a ignorar (pares solapados por diseño)")]
        [SerializeField] private Collider[] ignoreA;
        [SerializeField] private Collider[] ignoreB;

        [SerializeField] private float pusherRetractedTolerance = 0.03f;

        public JointDriveController Gate => gate;
        public JointDriveController Roller => roller;
        public JointDriveController Lift => lift;
        public JointDriveController Turntable => turntable;
        public JointDriveController Pusher => pusher;
        public PassiveJointProbe HeightProbe => heightProbe;
        public PassiveJointProbe Flap => flap;
        public LoadCell LoadCell => loadCell;
        public JointDriveController[] Drives { get; private set; }

        public bool DryRun { get; private set; }
        public bool PusherRetracted => pusher.Actual < pusherRetractedTolerance && pusher.Command < pusherRetractedTolerance;
        public bool LiftLocked => !PusherRetracted;
        public bool TurntableLocked => !PusherRetracted;

        public void Configure(JointDriveController gateJ, JointDriveController rollerJ, JointDriveController liftJ,
            JointDriveController tableJ, JointDriveController pusherJ, PassiveJointProbe probe, PassiveJointProbe flapProbe,
            LoadCell cell, Collider[] a, Collider[] b)
        {
            gate = gateJ; roller = rollerJ; lift = liftJ; turntable = tableJ; pusher = pusherJ;
            heightProbe = probe; flap = flapProbe; loadCell = cell; ignoreA = a; ignoreB = b;
        }

        private void Awake()
        {
            Drives = new[] { lift, turntable, pusher, gate, roller };
            // Enclavamientos: carro y mesa solo se mueven con el empujador retraído
            lift.CanMove = () => PusherRetracted;
            turntable.CanMove = () => PusherRetracted;

            for (int i = 0; i < Mathf.Min(ignoreA.Length, ignoreB.Length); i++)
                if (ignoreA[i] && ignoreB[i]) Physics.IgnoreCollision(ignoreA[i], ignoreB[i], true);
        }

        public void SetDryRun(bool on)
        {
            DryRun = on;
            foreach (var d in Drives) d.SetDriveEnabled(!on);
        }

        public void ResetStation()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}

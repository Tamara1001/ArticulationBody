using UnityEngine;

namespace Station.Mechanism
{
    /// <summary>
    /// Controla un joint con drive. La entrada escribe un "comando"; en FixedUpdate el target
    /// se acerca al comando con una rampa y se asigna al drive. También cachea la lectura real.
    /// Unidades de cara al usuario: metros (prismático), grados (revoluto) o m/s (rodillo).
    /// </summary>
    [RequireComponent(typeof(ArticulationBody))]
    public class JointDriveController : MonoBehaviour
    {
        public enum DriveMode { Position, Velocity }

        [Header("Identidad (UI)")]
        [SerializeField] private string displayName = "Joint";
        [SerializeField] private string unit = "m";
        [SerializeField] private string keyHint = "";

        [Header("Control")]
        [SerializeField] private DriveMode mode = DriveMode.Position;
        [SerializeField] private float minValue = 0f;
        [SerializeField] private float maxValue = 1f;
        [Tooltip("Velocidad máxima de cambio del target (unidad/s).")]
        [SerializeField] private float rampSpeed = 0.5f;
        [SerializeField] private float initialCommand = 0f;
        [Tooltip("Solo modo velocidad: radio para convertir m/s tangenciales a grados/s.")]
        [SerializeField] private float rollerRadius = 0.05f;

        [Header("Drive")]
        [SerializeField] private float stiffness = 1000f;
        [SerializeField] private float damping = 100f;
        [SerializeField] private float forceLimit = 1000f;

        private ArticulationBody body;
        private float command;
        private float target;
        private float actual;
        private float actualVelocity;
        private float appliedForce;
        private bool driveEnabled = true;

        public string DisplayName => displayName;
        public string Unit => unit;
        public string KeyHint => keyHint;
        public DriveMode Mode => mode;
        public float MinValue => minValue;
        public float MaxValue => maxValue;
        public float Command => command;
        public float Target => target;
        public float Actual => actual;
        public float ActualVelocity => actualVelocity;
        public float AppliedForce => appliedForce;
        public float ForceLimit => forceLimit;
        public bool IsSaturated => driveEnabled && Mathf.Abs(appliedForce) >= forceLimit * 0.98f;
        public bool IsRevolute => body != null && body.jointType == ArticulationJointType.RevoluteJoint;
        public ArticulationBody Body => body;
        public float RampSpeed => rampSpeed;

        /// <summary>Si es false, el comando queda congelado (enclavamiento).</summary>
        public System.Func<bool> CanMove;

        private void Awake()
        {
            body = GetComponent<ArticulationBody>();
            command = target = Mathf.Clamp(initialCommand, minValue, maxValue);
            ApplyDrive();
        }

        /// <summary>Configuración desde el builder del editor.</summary>
        public void Configure(string name, string unitLabel, string key, DriveMode driveMode,
            float min, float max, float ramp, float initial,
            float k, float d, float limit, float radius = 0.05f)
        {
            displayName = name; unit = unitLabel; keyHint = key; mode = driveMode;
            minValue = min; maxValue = max; rampSpeed = ramp; initialCommand = initial;
            stiffness = k; damping = d; forceLimit = limit; rollerRadius = radius;
        }

        public void SetCommand(float value)
        {
            if (CanMove != null && !CanMove()) return;
            command = Mathf.Clamp(value, minValue, maxValue);
        }

        public void Nudge(float delta) => SetCommand(command + delta);

        public void SetDriveEnabled(bool enabled)
        {
            driveEnabled = enabled;
            // Al volver de la prueba en seco, el target parte de la posición real para no dar un tirón.
            if (enabled && mode == DriveMode.Position) command = target = Mathf.Clamp(actual, minValue, maxValue);
            if (enabled && mode == DriveMode.Velocity) command = target = 0f;
            ApplyDrive();
        }

        /// <summary>Paso manual para pruebas automáticas con Physics.Simulate (sin bucle de juego).</summary>
        public void ManualFixedUpdate() => FixedUpdate();

        private void FixedUpdate()
        {
            // 1) Lectura del estado real (jointPosition viene en radianes para joints rotacionales)
            if (body.dofCount > 0)
            {
                float pos = body.jointPosition[0];
                float vel = body.jointVelocity[0];
                if (IsRevolute)
                {
                    if (mode == DriveMode.Velocity)
                    {
                        actual = vel * rollerRadius;                 // m/s tangenciales
                        actualVelocity = actual;
                    }
                    else
                    {
                        float prev = actual;
                        actual = pos * Mathf.Rad2Deg;
                        actualVelocity = (actual - prev) / Time.fixedDeltaTime;
                    }
                }
                else
                {
                    // Velocidad por diferencia de posición: jointVelocity deja un residuo
                    // de unos cm/s cuando el drive sostiene carga, aunque el cuerpo esté quieto.
                    float prev = actual;
                    actual = pos;
                    actualVelocity = (actual - prev) / Time.fixedDeltaTime;
                }
            }

            // 2) Rampa del target hacia el comando
            if (CanMove != null && !CanMove()) command = target; // enclavado: no avanza
            target = Mathf.MoveTowards(target, command, rampSpeed * Time.fixedDeltaTime);

            // 3) Fuerza que el drive aplicó en el último paso (N o N·m), informada por PhysX
            //    Se usa para la balanza y para avisar de saturación.
            if (driveEnabled && body.dofCount > 0)
            {
                ArticulationReducedSpace f = body.driveForce;
                appliedForce = f.dofCount > 0 ? f[0] : 0f;
            }
            else appliedForce = 0f;

            ApplyDrive();
        }

        private void ApplyDrive()
        {
            if (body == null) return;
            ArticulationDrive drive = body.xDrive;
            drive.driveType = ArticulationDriveType.Force;
            drive.stiffness = driveEnabled && mode == DriveMode.Position ? stiffness : 0f;
            drive.damping = driveEnabled ? damping : 0f;
            drive.forceLimit = driveEnabled ? forceLimit : 0f;
            if (mode == DriveMode.Position)
            {
                drive.target = target;
                drive.targetVelocity = 0f;
            }
            else
            {
                drive.target = 0f;
                drive.targetVelocity = target / Mathf.Max(rollerRadius, 1e-4f) * Mathf.Rad2Deg;
            }
            body.xDrive = drive;
        }
    }
}

using Station.Cargo;
using Station.Mechanism;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Station.Controls
{
    /// <summary>
    /// Atajos de teclado (Input System). Solo escribe comandos: los drives se actualizan en FixedUpdate.
    /// </summary>
    public class StationInput : MonoBehaviour
    {
        public const float LevelLoad = 0f, Level1 = 0.40f, Level2 = 0.95f;
        public const float AngleRight = 0f, AngleLeft = 180f;
        public const float RollerRun = 1.8f, RollerReverse = -0.5f;

        [SerializeField] private StationController station;
        [SerializeField] private CargoSpawner spawner;

        public event System.Action OnToggleHelp;
        public event System.Action OnCycleCamera;

        public void Configure(StationController s, CargoSpawner sp) { station = s; spawner = sp; }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            float dt = Time.deltaTime;

            // Movimiento continuo mientras se mantiene la tecla (a la velocidad de rampa del joint)
            Hold(kb.wKey, kb.sKey, station.Lift, dt);
            Hold(kb.aKey, kb.dKey, station.Turntable, dt);
            Hold(kb.eKey, kb.qKey, station.Pusher, dt);

            // Presets
            if (kb.digit1Key.wasPressedThisFrame) station.Lift.SetCommand(LevelLoad);
            if (kb.digit2Key.wasPressedThisFrame) station.Lift.SetCommand(Level1);
            if (kb.digit3Key.wasPressedThisFrame) station.Lift.SetCommand(Level2);
            if (kb.leftArrowKey.wasPressedThisFrame) station.Turntable.SetCommand(AngleLeft);
            if (kb.rightArrowKey.wasPressedThisFrame) station.Turntable.SetCommand(AngleRight);

            if (kb.spaceKey.wasPressedThisFrame) ToggleGate();
            if (kb.rKey.wasPressedThisFrame) ToggleRoller(RollerRun);
            if (kb.fKey.wasPressedThisFrame) ToggleRoller(RollerReverse);
            if (kb.nKey.wasPressedThisFrame) spawner.Spawn();
            if (kb.tKey.wasPressedThisFrame) station.SetDryRun(!station.DryRun);
            if (kb.hKey.wasPressedThisFrame) OnToggleHelp?.Invoke();
            if (kb.cKey.wasPressedThisFrame) OnCycleCamera?.Invoke();
            if (kb.backspaceKey.wasPressedThisFrame) station.ResetStation();
        }

        private static void Hold(KeyControl plus, KeyControl minus, JointDriveController joint, float dt)
        {
            int dir = (plus.isPressed ? 1 : 0) - (minus.isPressed ? 1 : 0);
            if (dir == 0) return;
            // Arranca desde el target actual para que soltar la tecla frene enseguida
            float from = Mathf.Abs(joint.Command - joint.Target) > joint.RampSpeed * 0.25f ? joint.Target : joint.Command;
            joint.SetCommand(from + dir * joint.RampSpeed * dt);
        }

        public void ToggleGate()
        {
            var g = station.Gate;
            g.SetCommand(g.Command > g.MaxValue * 0.5f ? g.MinValue : g.MaxValue);
        }

        public void ToggleRoller(float speed)
        {
            var r = station.Roller;
            r.SetCommand(Mathf.Abs(r.Command - speed) < 0.01f ? 0f : speed);
        }
    }
}

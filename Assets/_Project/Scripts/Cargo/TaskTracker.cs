using System;
using System.Collections.Generic;
using UnityEngine;

namespace Station.Cargo
{
    /// <summary>Lleva la cuenta de la tarea: aciertos, errores, caídas y tiempo por caja.</summary>
    public class TaskTracker : MonoBehaviour
    {
        public enum EventKind { Correct, Wrong, Dropped, Spawned, Info }

        public struct TaskEvent
        {
            public EventKind Kind;
            public string Message;
        }

        [SerializeField] private CargoSpawner spawner;
        [SerializeField] private float despawnDelay = 2.5f;
        [SerializeField] private float dropHeight = 0.35f;

        public int Correct { get; private set; }
        public int Wrong { get; private set; }
        public int Dropped { get; private set; }
        public float AverageTime => Correct + Wrong > 0 ? totalTime / (Correct + Wrong) : 0f;
        public float BestTime { get; private set; } = -1f;

        public event Action<TaskEvent> OnEvent;

        private float totalTime;
        private readonly List<(CargoBox box, float time)> pendingDespawn = new();

        public void Configure(CargoSpawner s) => spawner = s;

        public void ReportDelivered(CargoBox box, SortingBin bin)
        {
            box.Resolved = true;
            float t = Time.time - box.SpawnTime;
            bool ok = CargoBox.SideOf(box.Kind) == bin.Side && CargoBox.LevelOf(box.Kind) == bin.Level;
            totalTime += t;
            if (ok)
            {
                Correct++;
                if (BestTime < 0f || t < BestTime) BestTime = t;
                Raise(EventKind.Correct, $"Caja #{box.Serial} ({CargoBox.Label(box.Kind)}) → {bin.Label}: correcta en {t:0.0} s");
            }
            else
            {
                Wrong++;
                Raise(EventKind.Wrong, $"Caja #{box.Serial} era {CargoBox.Label(box.Kind)}: iba a " +
                      $"{(CargoBox.SideOf(box.Kind) == BinSide.Left ? "Izquierda" : "Derecha")} · Nivel {CargoBox.LevelOf(box.Kind)}");
            }
            pendingDespawn.Add((box, Time.time + despawnDelay));
        }

        public void Raise(EventKind kind, string message) => OnEvent?.Invoke(new TaskEvent { Kind = kind, Message = message });

        private void Update()
        {
            // Retira las cajas entregadas después de un momento
            for (int i = pendingDespawn.Count - 1; i >= 0; i--)
            {
                if (Time.time < pendingDespawn[i].time) continue;
                if (pendingDespawn[i].box) spawner.Release(pendingDespawn[i].box);
                pendingDespawn.RemoveAt(i);
            }

            // Detecta caídas: una caja sin resolver que tocó el piso fuera de un contenedor
            foreach (var box in spawner.Active)
            {
                if (box.Resolved) continue;
                if (box.transform.position.y < dropHeight)
                {
                    box.Resolved = true;
                    Dropped++;
                    Raise(EventKind.Dropped, $"Caja #{box.Serial} se cayó de la línea");
                    pendingDespawn.Add((box, Time.time + despawnDelay));
                }
            }
        }
    }
}

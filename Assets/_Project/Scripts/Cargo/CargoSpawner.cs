using System.Collections.Generic;
using UnityEngine;

namespace Station.Cargo
{
    /// <summary>
    /// Aparición de cajas en la parte alta de la rampa, con pool. Usa una "bolsa" mezclada
    /// para que los 4 tipos salgan parejos.
    /// </summary>
    public class CargoSpawner : MonoBehaviour
    {
        [SerializeField] private CargoBox lowTemplate;
        [SerializeField] private CargoBox tallTemplate;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private TaskTracker tracker;
        [SerializeField] private int maxActive = 6;

        private readonly List<CargoBox> active = new();
        private readonly Stack<CargoBox> poolLow = new();
        private readonly Stack<CargoBox> poolTall = new();
        private readonly List<CargoKind> bag = new();
        private int serial;

        public IReadOnlyList<CargoBox> Active => active;

        public void Configure(CargoBox low, CargoBox tall, Transform point, TaskTracker t)
        {
            lowTemplate = low; tallTemplate = tall; spawnPoint = point; tracker = t;
        }

        private void Awake()
        {
            lowTemplate.gameObject.SetActive(false);
            tallTemplate.gameObject.SetActive(false);
        }

        public bool CanSpawn(out string reason)
        {
            reason = null;
            if (active.Count >= maxActive) { reason = "Hay demasiadas cajas en la línea"; return false; }
            // Una caja por vez en la admisión: hasta que la anterior llegue a la mesa
            foreach (var b in active)
                if (!b.Resolved && b.transform.position.z < -0.45f && b.transform.position.y > 0.6f)
                { reason = "Esperá a que la caja anterior pase a la mesa"; return false; }
            return true;
        }

        public CargoBox Spawn()
        {
            if (!CanSpawn(out string reason))
            {
                tracker.Raise(TaskTracker.EventKind.Info, reason);
                return null;
            }
            if (bag.Count == 0)
            {
                bag.AddRange(new[] { CargoKind.LowLight, CargoKind.TallLight, CargoKind.LowHeavy, CargoKind.TallHeavy });
                for (int i = bag.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (bag[i], bag[j]) = (bag[j], bag[i]); }
            }
            CargoKind kind = bag[^1];
            bag.RemoveAt(bag.Count - 1);

            bool low = CargoBox.HeightOf(kind) < 0.3f;
            var pool = low ? poolLow : poolTall;
            CargoBox box = pool.Count > 0 ? pool.Pop() : Instantiate(low ? lowTemplate : tallTemplate, transform);
            float h = CargoBox.HeightOf(kind);
            box.transform.SetPositionAndRotation(spawnPoint.position + spawnPoint.up * (h * 0.5f + 0.01f), spawnPoint.rotation);
            var rb = box.GetComponent<Rigidbody>();
            box.gameObject.SetActive(true);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            box.Init(kind, ++serial);
            box.name = $"Caja_{serial:00}";
            active.Add(box);
            tracker.Raise(TaskTracker.EventKind.Spawned, $"Caja #{serial} en la rampa de admisión");
            return box;
        }

        public void Release(CargoBox box)
        {
            active.Remove(box);
            box.gameObject.SetActive(false);
            (CargoBox.HeightOf(box.Kind) < 0.3f ? poolLow : poolTall).Push(box);
        }
    }
}

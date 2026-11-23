using UnityEngine;

namespace Station.Cargo
{
    /// <summary>Contenedor de destino. Un trigger en el fondo registra la caja que entra.</summary>
    [RequireComponent(typeof(Collider))]
    public class SortingBin : MonoBehaviour
    {
        [SerializeField] private BinSide side;
        [SerializeField] private int level = 1;
        [SerializeField] private TaskTracker tracker;

        public BinSide Side => side;
        public int Level => level;
        public string Label => $"{(side == BinSide.Left ? "Izquierda" : "Derecha")} · Nivel {level}";

        public void Configure(BinSide s, int lvl, TaskTracker t) { side = s; level = lvl; tracker = t; }

        private void OnTriggerEnter(Collider other)
        {
            var box = other.attachedRigidbody ? other.attachedRigidbody.GetComponent<CargoBox>() : null;
            if (box == null || box.Resolved) return;
            tracker.ReportDelivered(box, this);
        }
    }
}

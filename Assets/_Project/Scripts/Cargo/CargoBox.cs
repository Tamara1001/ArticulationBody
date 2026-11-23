using UnityEngine;

namespace Station.Cargo
{
    public enum CargoKind { LowLight, TallLight, LowHeavy, TallHeavy }

    public enum BinSide { Left, Right }

    /// <summary>Caja de cartón (Rigidbody común). Conoce su tipo y su destino correcto.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CargoBox : MonoBehaviour
    {
        public CargoKind Kind { get; private set; }
        public int Serial { get; private set; }
        public float SpawnTime { get; private set; }
        public bool Resolved { get; set; }

        public static float HeightOf(CargoKind k) => k == CargoKind.LowLight || k == CargoKind.LowHeavy ? 0.20f : 0.40f;
        public static float MassOf(CargoKind k) => k switch
        {
            CargoKind.LowLight => 4f,
            CargoKind.TallLight => 6f,
            CargoKind.LowHeavy => 18f,
            _ => 20f
        };
        public static bool IsHeavy(CargoKind k) => k == CargoKind.LowHeavy || k == CargoKind.TallHeavy;
        public static int LevelOf(CargoKind k) => HeightOf(k) < 0.3f ? 1 : 2;
        public static BinSide SideOf(CargoKind k) => IsHeavy(k) ? BinSide.Right : BinSide.Left;

        public static string Label(CargoKind k) => k switch
        {
            CargoKind.LowLight => "Baja liviana",
            CargoKind.TallLight => "Alta liviana",
            CargoKind.LowHeavy => "Baja pesada",
            _ => "Alta pesada"
        };

        public void Init(CargoKind kind, int serial)
        {
            Kind = kind;
            Serial = serial;
            SpawnTime = Time.time;
            Resolved = false;
            GetComponent<Rigidbody>().mass = MassOf(kind);
        }
    }
}

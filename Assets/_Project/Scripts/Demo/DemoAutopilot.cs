#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Station.CameraRig;
using Station.Cargo;
using Station.Mechanism;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Station.Demo
{
    /// <summary>
    /// Demo grabada (solo Editor): una corrutina opera la estación como lo haría una persona —
    /// un cursor de flecha dibujado se mueve hasta cada botón del HUD, lo "pulsa" y recién ahí
    /// ejecuta la acción— y cada frame se guarda a 1920×1080 con tiempo determinístico
    /// (Time.captureFramerate). Los frames se unen después con ffmpeg. Menú: Estación ▸ Grabar demo.
    /// </summary>
    public class DemoAutopilot : MonoBehaviour
    {
        private const string ArmedKey = "Station.DemoArmed";
        private const int Width = 1920, Height = 1080, Fps = 30;

        private StationController station;
        private CargoSpawner spawner;
        private TaskTracker tracker;
        private OrbitCamera orbit;
        private UIDocument doc;
        private VisualElement root, cursor;
        private RenderTexture rt;
        private Texture2D grab;
        private string frameDir;
        private int frame;
        private bool finished;
        private readonly List<Task> writes = new();
        private Vector2 cursorPos = new(1100f, 620f);

        // ------------------------------------------------------------------ arranque

        [MenuItem("Estación/Grabar demo (frames)")]
        public static void Arm()
        {
            const string scenePath = "Assets/_Project/Scenes/Station.unity";
            if (EditorSceneManager.GetActiveScene().path != scenePath)
                EditorSceneManager.OpenScene(scenePath);
            SessionState.SetBool(ArmedKey, true);
            EditorApplication.EnterPlaymode();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!SessionState.GetBool(ArmedKey, false)) return;
            SessionState.SetBool(ArmedKey, false);
            new GameObject("DemoAutopilot").AddComponent<DemoAutopilot>();
        }

        private IEnumerator Start()
        {
            station = FindFirstObjectByType<StationController>();
            spawner = FindFirstObjectByType<CargoSpawner>();
            tracker = FindFirstObjectByType<TaskTracker>();
            orbit = FindFirstObjectByType<OrbitCamera>();
            doc = FindFirstObjectByType<UIDocument>();

            frameDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Video", "frames"));
            if (Directory.Exists(frameDir)) Directory.Delete(frameDir, true);
            Directory.CreateDirectory(frameDir);

            // Cámara y panel de UI dibujan en la misma RenderTexture 1920×1080
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            rt.Create();
            grab = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            Camera.main.targetTexture = rt;

            var hudGo = doc.gameObject;
            var ps = Instantiate(doc.panelSettings); // copia: no se toca el asset del proyecto
            ps.targetTexture = rt;
            ps.clearColor = false;
            hudGo.SetActive(false);
            doc.panelSettings = ps;
            hudGo.SetActive(true); // el HUD se reconstruye sobre el panel nuevo
            yield return null;

            root = doc.rootVisualElement;
            orbit.ExternalControl = true;
            cursor = new VisualElement { pickingMode = PickingMode.Ignore };
            cursor.style.position = Position.Absolute;
            cursor.style.width = 28; cursor.style.height = 42;
            cursor.style.backgroundImage = new StyleBackground(MakeArrow());
            cursor.style.transformOrigin = new TransformOrigin(2, 2);
            PlaceCursor(cursorPos);
            root.Add(cursor);

            Time.captureFramerate = Fps;
            StartCoroutine(CaptureLoop());
            yield return Script();

            yield return new WaitForSeconds(1.5f);
            finished = true;
            yield return null;
            Task.WaitAll(writes.ToArray());
            Debug.Log($"[Demo] Listo: {frame} frames en {frameDir}");
            EditorApplication.ExitPlaymode();
        }

        private void OnDestroy()
        {
            Time.captureFramerate = 0;
            if (Camera.main) Camera.main.targetTexture = null;
            if (rt) rt.Release();
        }

        private void OnGUI()
        {
            if (rt) GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), rt, ScaleMode.ScaleToFit, false);
        }

        // ------------------------------------------------------------------ captura

        private IEnumerator CaptureLoop()
        {
            while (!finished)
            {
                yield return new WaitForEndOfFrame();
                RenderTexture.active = rt;
                grab.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                RenderTexture.active = null;
                byte[] jpg = grab.EncodeToJPG(95);
                string path = Path.Combine(frameDir, $"f_{frame++:D6}.jpg");
                writes.Add(Task.Run(() => File.WriteAllBytes(path, jpg)));
                if (writes.Count > 64) { Task.WaitAll(writes.ToArray()); writes.Clear(); }
            }
        }

        // ------------------------------------------------------------------ guion

        private IEnumerator Script()
        {
            // Ayuda inicial a la vista unos segundos, con el cursor quieto
            yield return Wait(4f);
            yield return Click("Comenzar", () => root.Q(className: "overlay").RemoveFromClassList("overlay--on"));
            yield return Wait(1f);

            // Un poco de órbita y zoom de cámara con el mouse
            yield return MoveCursor(new Vector2(930f, 700f), 0.8f);
            yield return Drag(new Vector2(150f, -30f), 1.6f);
            yield return Wait(0.4f);
            yield return Drag(new Vector2(-150f, 30f), 1.6f);
            yield return Zoom(3);
            yield return Zoom(-3);
            yield return Wait(0.5f);

            for (int n = 0; n < 4; n++)
                yield return SortOneBox(n);

            // Prueba en seco: sin drives el mecanismo cae por gravedad hasta sus límites
            yield return Wait(1f);
            yield return Click("Nivel 2", () => station.Lift.SetCommand(station.Lift.MaxValue));
            yield return Click("Izquierda", () => station.Turntable.SetCommand(180f));
            yield return WaitUntil(() => Settled(station.Lift) && Settled(station.Turntable), 12f);
            yield return Click("Empujar", () => station.Pusher.SetCommand(station.Pusher.MaxValue));
            yield return Wait(3f);
            yield return Click("Prueba en seco", () => station.SetDryRun(true));
            yield return Wait(7f);
            yield return Click("Prueba en seco", () => station.SetDryRun(false));
            yield return Wait(2f);
            yield return Click("Retraer", () => station.Pusher.SetCommand(0f));
            yield return WaitUntil(() => station.PusherRetracted, 8f);
            yield return Click("Carga", () => station.Lift.SetCommand(0f));
            yield return Click("Derecha", () => station.Turntable.SetCommand(0f));
            yield return WaitUntil(() => Settled(station.Lift) && Settled(station.Turntable), 12f);
            yield return Wait(2.5f);
        }

        private IEnumerator SortOneBox(int index)
        {
            CargoBox box = null;
            yield return Click("Nueva caja", () => box = spawner.Spawn());
            if (box == null) yield break;

            // La caja baja por la rampa y levanta el palpador: esperar lectura estable
            yield return WaitUntil(() => station.HeightProbe.IsStable, 14f);
            yield return Wait(1.8f);
            float height = station.HeightProbe.InContact ? station.HeightProbe.LiveHeight : station.HeightProbe.HeldHeight;

            // Carro y mesa listos en Carga / 0° antes de recibir la caja
            yield return WaitUntil(() => Settled(station.Lift) && Settled(station.Turntable), 12f);

            yield return Click("Bajar", () => station.Gate.SetCommand(0f));
            yield return WaitUntil(() => InRollerZone(box), 12f);
            yield return Click("Subir", () => station.Gate.SetCommand(station.Gate.MaxValue));
            yield return Click("Marcha", () => station.Roller.SetCommand(Station.Controls.StationInput.RollerRun));
            yield return WaitUntil(() => OnDeck(box), 10f);
            yield return Click("Paro", () => station.Roller.SetCommand(0f));

            // Leer la balanza (lectura estable) y decidir según la regla
            yield return WaitUntil(() => station.LoadCell.IsReady && station.LoadCell.IsStable, 8f);
            yield return Wait(2f);
            float mass = station.LoadCell.MassKg;
            bool level2 = height >= 0.30f;
            bool left = mass < 10f;

            // Cámara: la primera caja con la vista general; las demás cambian de vista con el botón
            int wantView = box.Kind == CargoKind.LowHeavy ? 3 : index == 1 ? 2 : index == 3 ? 1 : 0;
            yield return ViewTo(wantView);

            yield return Click(level2 ? "Nivel 2" : "Nivel 1", () => station.Lift.SetCommand(level2 ? Station.Controls.StationInput.Level2 : Station.Controls.StationInput.Level1));
            yield return Click(left ? "Izquierda" : "Derecha", () => station.Turntable.SetCommand(left ? 180f : 0f));
            yield return WaitUntil(() => Settled(station.Lift) && Settled(station.Turntable), 14f);
            yield return Wait(0.6f);

            yield return Click("Empujar", () => station.Pusher.SetCommand(station.Pusher.MaxValue));
            yield return Wait(2.5f);
            yield return Click("Retraer", () => station.Pusher.SetCommand(0f));
            yield return WaitUntil(() => station.PusherRetracted, 8f);
            yield return Click("Carga", () => station.Lift.SetCommand(0f));
            yield return Click("Derecha", () => station.Turntable.SetCommand(0f));
            yield return WaitUntil(() => box.Resolved, 12f);
            yield return Wait(box.Kind == CargoKind.LowHeavy ? 3.5f : 1.5f); // se ve la compuerta pasiva abrirse
            yield return ViewTo(0);
            yield return WaitUntil(() => Settled(station.Lift) && Settled(station.Turntable), 12f);
        }

        // ------------------------------------------------------------------ utilidades de guion

        private static bool Settled(JointDriveController j) =>
            Mathf.Abs(j.Actual - j.Command) < (j.Unit == "°" ? 1.5f : 0.012f);

        private static bool InRollerZone(CargoBox b)
        {
            Vector3 p = b.transform.position;
            return p.z > -1.15f && p.z < -0.45f && Mathf.Abs(p.x) < 0.5f && p.y > 0.6f;
        }

        private static bool OnDeck(CargoBox b)
        {
            Vector3 p = b.transform.position;
            return new Vector2(p.x, p.z).magnitude < 0.3f && p.y > 0.75f;
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        private static IEnumerator WaitUntil(Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
        }

        private IEnumerator ViewTo(int target)
        {
            while (orbit.ViewIndex != target)
                yield return Click("Vista", () => orbit.CycleView());
            if (orbit.ViewIndex == target) yield return Wait(0.3f);
        }

        // ------------------------------------------------------------------ cursor

        private Button FindButton(string prefix)
        {
            foreach (var b in root.Query<Button>().ToList())
                if (b.text != null && b.text.StartsWith(prefix, StringComparison.Ordinal)) return b;
            return null;
        }

        private IEnumerator Click(string prefix, Action action)
        {
            var b = FindButton(prefix);
            if (b == null) { Debug.LogWarning("[Demo] Botón no encontrado: " + prefix); action(); yield break; }
            Rect r = b.worldBound;
            var target = new Vector2(
                r.center.x + UnityEngine.Random.Range(-r.width * 0.18f, r.width * 0.18f),
                r.center.y + UnityEngine.Random.Range(-r.height * 0.12f, r.height * 0.12f));
            yield return MoveCursor(target, -1f);
            yield return Wait(0.15f);
            // Pulsación visible: el botón se hunde un instante y el cursor se achica
            b.style.opacity = 0.65f; b.style.translate = new Translate(0, 1);
            cursor.style.scale = new Scale(new Vector2(0.9f, 0.9f));
            yield return Wait(0.12f);
            action();
            yield return Wait(0.1f);
            b.style.opacity = StyleKeyword.Null; b.style.translate = StyleKeyword.Null;
            cursor.style.scale = StyleKeyword.Null;
            yield return Wait(0.35f);
        }

        private IEnumerator MoveCursor(Vector2 to, float duration)
        {
            Vector2 from = cursorPos;
            float dist = Vector2.Distance(from, to);
            if (duration < 0f) duration = Mathf.Clamp(0.35f + dist / 1500f, 0.4f, 1.2f);
            Vector2 dir = dist > 1f ? (to - from) / dist : Vector2.right;
            Vector2 perp = new(-dir.y, dir.x);
            float arc = dist * 0.05f * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                PlaceCursor(Vector2.Lerp(from, to, k) + perp * (Mathf.Sin(k * Mathf.PI) * arc));
                yield return null;
            }
            PlaceCursor(to);
        }

        /// <summary>Arrastra con el botón pulsado sobre el fondo: el cursor se mueve y la cámara orbita.</summary>
        private IEnumerator Drag(Vector2 delta, float duration)
        {
            Vector2 from = cursorPos;
            cursor.style.scale = new Scale(new Vector2(0.92f, 0.92f));
            float prev = 0f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                PlaceCursor(from + delta * k);
                orbit.Nudge(delta.x * (k - prev), delta.y * (k - prev));
                prev = k;
                yield return null;
            }
            orbit.Nudge(delta.x * (1f - prev), delta.y * (1f - prev));
            PlaceCursor(from + delta);
            cursor.style.scale = StyleKeyword.Null;
        }

        private IEnumerator Zoom(int steps)
        {
            int dir = steps > 0 ? 1 : -1;
            for (int i = 0; i < Mathf.Abs(steps); i++)
            {
                orbit.ZoomSteps(dir);
                yield return Wait(0.25f);
            }
            yield return Wait(0.5f);
        }

        private void PlaceCursor(Vector2 p)
        {
            cursorPos = p;
            cursor.style.left = p.x - 2f; // la punta de la flecha queda en p
            cursor.style.top = p.y - 2f;
        }

        /// <summary>Flecha blanca con borde negro, como el puntero normal de Windows.</summary>
        private static Texture2D MakeArrow()
        {
            const int W = 28, H = 42, SS = 4;
            const float S = 1.7f, Pad = 2f, Stroke = 1.0f;
            Vector2[] raw = { new(0, 0), new(0, 16), new(4f, 12.2f), new(6.9f, 19f), new(9.6f, 17.9f), new(6.8f, 11.2f), new(11.6f, 11.2f) };
            var poly = new Vector2[raw.Length];
            for (int i = 0; i < raw.Length; i++) poly[i] = raw[i] * S + new Vector2(Pad, Pad);

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int white = 0, black = 0;
                for (int j = 0; j < SS; j++)
                for (int i = 0; i < SS; i++)
                {
                    // y de textura hacia arriba; el polígono está en coordenadas de pantalla (y hacia abajo)
                    var p = new Vector2(x + (i + 0.5f) / SS, H - (y + (j + 0.5f) / SS));
                    bool inside = Inside(poly, p);
                    float d = EdgeDistance(poly, p);
                    if (d < Stroke) black++;
                    else if (inside) white++;
                }
                int n = SS * SS, opaque = white + black;
                byte a = (byte)Mathf.RoundToInt(255f * opaque / n);
                byte c = opaque > 0 ? (byte)Mathf.RoundToInt(255f * white / opaque) : (byte)0;
                px[y * W + x] = new Color32(c, c, c, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        private static float EdgeDistance(Vector2[] poly, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }
    }
}
#endif

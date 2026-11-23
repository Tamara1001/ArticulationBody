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
            arrowTex = MakeArrow();
            handTex = MakeHand();
            SetShape(false, true);
            PlaceCursor(cursorBase);
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

        private class BoxInfo
        {
            public float Height = -1f, Mass = -1f;
            public bool Decided;
        }

        private readonly Dictionary<CargoBox, BoxInfo> infos = new();
        private int spawned;
        private float probeStableSince = -1f, scaleStableSince = -1f, pushTime = -10f, startTime;
        private bool introDone;
        private const int TotalBoxes = 4;

        private BoxInfo Info(CargoBox b)
        {
            if (!infos.TryGetValue(b, out var i)) infos[b] = i = new BoxInfo();
            return i;
        }

        private int Finished => tracker.Correct + tracker.Wrong + tracker.Dropped;

        private IEnumerator Script()
        {
            startTime = Time.time;
            // Ayuda inicial: lectura breve y a operar
            yield return MoveCursor(new Vector2(1150f, 600f), 0.6f);
            yield return Wait(1.2f);
            yield return Click("Comenzar", () => root.Q(className: "overlay").RemoveFromClassList("overlay--on"));

            // Operador: cada vuelta elige la próxima acción lista; si no hay, el cursor pasea por los sensores
            while ((Finished < TotalBoxes || station.Pusher.Command > 0.05f) && Time.time - startTime < 170f)
            {
                var act = NextAction();
                if (act.HasValue) yield return Click(act.Value.button, act.Value.run);
                else yield return IdleStep();
            }

            // Prueba en seco: sin drives el mecanismo cae por gravedad hasta sus límites
            yield return Click("Nivel 2", () => station.Lift.SetCommand(station.Lift.MaxValue));
            yield return Click("Izquierda", () => station.Turntable.SetCommand(180f));
            yield return Wait(1.3f);
            yield return Click("Prueba en seco", () => station.SetDryRun(true));
            yield return Hover(3.8f, SensorTarget(2));
            yield return Click("Prueba en seco", () => station.SetDryRun(false));
            yield return Click("Carga", () => station.Lift.SetCommand(0f));
            yield return Click("Derecha", () => station.Turntable.SetCommand(0f));
            yield return Hover(2.5f, SensorTarget(0));
        }

        private struct Act
        {
            public string button;
            public Action run;
        }

        // Sigue los sensores cada cuadro (lecturas estables y tiempos de observación)
        private void Update()
        {
            if (station == null || spawner == null) return;
            CargoBox stop = BoxAt(Zone.Stop), deck = BoxAt(Zone.Deck);

            if (stop != null && station.HeightProbe.IsStable)
            {
                if (probeStableSince < 0f) probeStableSince = Time.time;
                Info(stop).Height = station.HeightProbe.HeldHeight;
            }
            else probeStableSince = -1f;

            bool scaleOk = deck != null && station.Roller.Command == 0f && station.LoadCell.IsReady && station.LoadCell.IsStable;
            if (scaleOk)
            {
                if (scaleStableSince < 0f) scaleStableSince = Time.time;
                Info(deck).Mass = station.LoadCell.MassKg;
            }
            else scaleStableSince = -1f;
        }

        private enum Zone { Stop, Roller, Deck }

        private CargoBox BoxAt(Zone zone)
        {
            foreach (var b in spawner.Active)
            {
                if (b.Resolved) continue;
                Vector3 p = b.transform.position;
                bool hit = zone switch
                {
                    Zone.Stop => p.z < -1.25f && Mathf.Abs(p.x) < 0.5f && p.y > 0.6f,
                    Zone.Roller => p.z > -1.15f && p.z < -0.45f && Mathf.Abs(p.x) < 0.5f && p.y > 0.6f,
                    _ => new Vector2(p.x, p.z).magnitude < 0.3f && p.y > 0.75f
                };
                if (hit) return b;
            }
            return null;
        }

        private Act? NextAction()
        {
            CargoBox stop = BoxAt(Zone.Stop), roller = BoxAt(Zone.Roller), deck = BoxAt(Zone.Deck);
            bool pusherOut = station.Pusher.Command > 0.05f;
            bool home = station.Lift.Command < 0.02f && Mathf.Abs(station.Turntable.Command) < 2f && Settled(station.Lift) && Settled(station.Turntable);
            bool gateOpen = station.Gate.Command < 0.05f;

            // 1) Retraer el empujador cuando la caja ya salió
            if (pusherOut && Time.time - pushTime > 2.3f)
                return new Act { button = "Retraer", run = () => station.Pusher.SetCommand(0f) };

            // 2) Subir el tope en cuanto la caja pasó
            if (gateOpen && roller != null)
                return new Act { button = "Subir", run = () => station.Gate.SetCommand(station.Gate.MaxValue) };

            // 3) Detener el rodillo con la caja ya en la mesa
            if (deck != null && station.Roller.Command != 0f)
                return new Act { button = "Paro", run = () => station.Roller.SetCommand(0f) };

            // 3b) Vista de salidas para ver abrirse la compuerta pasiva con la caja baja pesada
            if (deck != null && deck.Kind == CargoKind.LowHeavy && orbit.ViewIndex != 3 && !pusherOut)
                return new Act { button = "Vista", run = () => orbit.CycleView() };

            // 4) Volver a Carga / 0° con la mesa vacía y el empujador retraído
            if (!pusherOut && station.PusherRetracted && deck == null)
            {
                if (station.Lift.Command > 0.02f)
                    return new Act { button = "Carga", run = () => station.Lift.SetCommand(0f) };
                if (Mathf.Abs(station.Turntable.Command) > 2f)
                    return new Act { button = "Derecha", run = () => station.Turntable.SetCommand(0f) };
            }

            // 5) Pasar la caja del rodillo a la mesa
            if (roller != null && !gateOpen && station.Roller.Command == 0f && deck == null && home && station.PusherRetracted)
                return new Act { button = "Marcha", run = () => station.Roller.SetCommand(Station.Controls.StationInput.RollerRun) };

            // 6) Clasificar la caja de la mesa según la regla (balanza estable ~1,2 s)
            if (deck != null && station.Roller.Command == 0f && (Info(deck).Decided || (scaleStableSince >= 0f && Time.time - scaleStableSince > 0.9f)))
            {
                var info = Info(deck);
                info.Decided = true; // la decisión sigue en pie aunque la balanza se agite al mover el carro
                bool level2 = info.Height >= 0.30f, left = info.Mass < 10f;
                float wantLift = level2 ? Station.Controls.StationInput.Level2 : Station.Controls.StationInput.Level1;
                float wantAngle = left ? 180f : 0f;
                if (station.PusherRetracted && Mathf.Abs(station.Lift.Command - wantLift) > 0.01f)
                    return new Act { button = level2 ? "Nivel 2" : "Nivel 1", run = () => station.Lift.SetCommand(wantLift) };
                if (station.PusherRetracted && Mathf.Abs(station.Turntable.Command - wantAngle) > 1f)
                    return new Act { button = left ? "Izquierda" : "Derecha", run = () => station.Turntable.SetCommand(wantAngle) };
                if (!pusherOut && Settled(station.Lift) && Settled(station.Turntable))
                    return new Act { button = "Empujar", run = () => { station.Pusher.SetCommand(station.Pusher.MaxValue); pushTime = Time.time; } };
            }

            // 7) Pedir la próxima caja apenas la anterior dejó la admisión
            if (spawned < TotalBoxes && spawner.CanSpawn(out _))
                return new Act { button = "Nueva caja", run = () => { if (spawner.Spawn() != null) spawned++; } };

            // 8) Liberar el tope cuando el palpador ya dio su lectura y el rodillo está libre
            if (stop != null && !gateOpen && roller == null && probeStableSince >= 0f && Time.time - probeStableSince > 1.0f)
                return new Act { button = "Bajar", run = () => station.Gate.SetCommand(0f) };

            bool lowHeavyOnDeck = deck != null && deck.Kind == CargoKind.LowHeavy;
            if (!lowHeavyOnDeck && orbit.ViewIndex != 0 && !LowHeavyInFlight() && deck == null)
                return new Act { button = "Vista", run = () => orbit.CycleView() };

            return null;
        }

        private bool LowHeavyInFlight()
        {
            foreach (var b in spawner.Active)
                if (!b.Resolved && b.Kind == CargoKind.LowHeavy && b.transform.position.y > 0.5f && b.transform.position.z > -0.45f) return true;
            return false;
        }

        /// <summary>Sin nada que pulsar: orbita un poco la primera vez y después pasea el cursor por los sensores.</summary>
        private IEnumerator IdleStep()
        {
            if (!introDone && spawned > 0)
            {
                introDone = true;
                yield return MoveCursor(new Vector2(930f, 700f), -1f);
                yield return Drag(new Vector2(130f, -26f), 0.9f);
                yield return Drag(new Vector2(-130f, 26f), 0.9f);
                orbit.ZoomSteps(2);
                yield return Wait(0.35f);
                orbit.ZoomSteps(-2);
                yield break;
            }
            // Mira la lectura relevante: palpador mientras mide, balanza cuando la caja está en la mesa
            int which = BoxAt(Zone.Deck) != null ? 1 : BoxAt(Zone.Stop) != null ? 0 : UnityEngine.Random.Range(0, 3);
            yield return Hover(UnityEngine.Random.Range(0.35f, 0.7f), SensorTarget(which));
        }

        private Vector2 SensorTarget(int index)
        {
            var rows = root.Query(className: "sensor").ToList();
            if (rows.Count == 0) return new Vector2(1700f, 190f);
            Rect r = rows[Mathf.Clamp(index, 0, rows.Count - 1)].worldBound;
            return new Vector2(r.center.x + UnityEngine.Random.Range(-60f, 40f), r.center.y + UnityEngine.Random.Range(-10f, 10f));
        }

        private IEnumerator Hover(float seconds, Vector2 where)
        {
            float t0 = Time.time;
            yield return MoveCursor(where, -1f);
            while (Time.time - t0 < seconds)
            {
                where += new Vector2(UnityEngine.Random.Range(-14f, 14f), UnityEngine.Random.Range(-6f, 6f));
                yield return MoveCursor(where, UnityEngine.Random.Range(0.28f, 0.5f));
                yield return Wait(UnityEngine.Random.Range(0.05f, 0.2f));
            }
        }

        // ------------------------------------------------------------------ utilidades de guion

        private static bool Settled(JointDriveController j) =>
            Mathf.Abs(j.Actual - j.Command) < (j.Unit == "°" ? 1.5f : 0.012f);

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
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
                r.center.x + UnityEngine.Random.Range(-r.width * 0.22f, r.width * 0.22f),
                r.center.y + UnityEngine.Random.Range(-r.height * 0.15f, r.height * 0.15f));
            yield return MoveCursor(target, -1f, true);
            yield return Wait(UnityEngine.Random.Range(0.03f, 0.1f));
            // Pulsación visible: el botón se hunde un instante y el cursor se achica
            b.style.opacity = 0.65f; b.style.translate = new Translate(0, 1);
            cursor.style.scale = new Scale(new Vector2(0.9f, 0.9f));
            yield return Wait(0.09f);
            action();
            yield return Wait(0.07f);
            b.style.opacity = StyleKeyword.Null; b.style.translate = StyleKeyword.Null;
            cursor.style.scale = StyleKeyword.Null;
            yield return Wait(UnityEngine.Random.Range(0.04f, 0.2f));
        }

        private static float Ease(float t) // arranque y frenada suaves, tramo medio rápido
        {
            float a = Mathf.Pow(t, 1.7f), b = Mathf.Pow(1f - t, 1.7f);
            return a / (a + b);
        }

        /// <summary>Movimiento curvo (Bézier con puntos de control desplazados), con frenada, sobrepaso y corrección.</summary>
        private IEnumerator MoveCursor(Vector2 to, float duration, bool overshoot = false)
        {
            Vector2 from = cursorBase;
            float dist = Vector2.Distance(from, to);
            if (dist < 2f) { yield return null; yield break; }
            Vector2 dir = (to - from) / dist;
            Vector2 aim = to;
            bool over = overshoot && dist > 220f;
            if (over) aim = to + dir * UnityEngine.Random.Range(5f, 12f);
            if (duration < 0f) duration = Mathf.Clamp(0.2f + dist / 3200f, 0.25f, 0.5f);
            yield return Curve(from, aim, duration);
            if (over) yield return Curve(aim, to, 0.1f, 0.02f);
        }

        private IEnumerator Curve(Vector2 from, Vector2 to, float duration, float bend = 0.16f)
        {
            float dist = Vector2.Distance(from, to);
            Vector2 d = (to - from) / Mathf.Max(dist, 1f), perp = new(-d.y, d.x);
            Vector2 c1 = from + (to - from) * 0.3f + perp * dist * UnityEngine.Random.Range(-bend, bend);
            Vector2 c2 = from + (to - from) * 0.75f + perp * dist * UnityEngine.Random.Range(-bend, bend) * 0.5f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Ease(t / duration), u = 1f - k;
                PlaceCursor(u * u * u * from + 3f * u * u * k * c1 + 3f * u * k * k * c2 + k * k * k * to);
                yield return null;
            }
            PlaceCursor(to);
        }

        /// <summary>Arrastra con el botón pulsado sobre el fondo: el cursor se mueve y la cámara orbita.</summary>
        private IEnumerator Drag(Vector2 delta, float duration)
        {
            Vector2 from = cursorBase;
            cursor.style.scale = new Scale(new Vector2(0.92f, 0.92f));
            float prev = 0f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Ease(t / duration);
                PlaceCursor(from + delta * k);
                orbit.Nudge(delta.x * (k - prev), delta.y * (k - prev));
                prev = k;
                yield return null;
            }
            orbit.Nudge(delta.x * (1f - prev), delta.y * (1f - prev));
            PlaceCursor(from + delta);
            cursor.style.scale = StyleKeyword.Null;
        }

        private Vector2 cursorBase = new(1100f, 620f);

        private void PlaceCursor(Vector2 p)
        {
            cursorBase = p;
            ApplyCursor();
        }

        // Micro-temblor continuo, como una mano real sobre el mouse
        private void LateUpdate() { if (cursor != null) ApplyCursor(); }

        private Texture2D arrowTex, handTex;
        private bool handShape;
        private static readonly Vector2 ArrowHot = new(2f, 2f), HandHot = new(15f, 3f);

        private void SetShape(bool hand, bool force = false)
        {
            if (!force && hand == handShape) return;
            handShape = hand;
            var tex = hand ? handTex : arrowTex;
            Vector2 hot = hand ? HandHot : ArrowHot;
            cursor.style.width = tex.width; cursor.style.height = tex.height;
            cursor.style.backgroundImage = new StyleBackground(tex);
            cursor.style.transformOrigin = new TransformOrigin(hot.x, hot.y);
        }

        // Como en Windows: mano sobre botones y presets del HUD, flecha en el resto
        private bool OverClickable(Vector2 p)
        {
            var panel = root.panel;
            if (panel == null) return false;
            for (var e = panel.Pick(p); e != null; e = e.parent)
                if (e is Button) return true;
            return false;
        }

        private void ApplyCursor()
        {
            SetShape(OverClickable(cursorBase));
            Vector2 hot = handShape ? HandHot : ArrowHot;
            float t = Time.time;
            float jx = (Mathf.PerlinNoise(t * 2.3f, 0.37f) - 0.5f) * 1.8f;
            float jy = (Mathf.PerlinNoise(0.81f, t * 2.1f) - 0.5f) * 1.8f;
            cursor.style.left = cursorBase.x + jx - hot.x; // la punta (o el dedo índice) queda en la posición
            cursor.style.top = cursorBase.y + jy - hot.y;
        }

        /// <summary>Mano de enlace de Windows: índice extendido, palma y tres dedos, con borde negro.</summary>
        private static Texture2D MakeHand()
        {
            const int W = 36, H = 44, SS = 4;
            const float S = 1.75f, Pad = 2f, Stroke = 0.9f;
            float Seg(Vector2 p, Vector2 a, Vector2 b, float r)
            {
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                return Vector2.Distance(p, a + ab * t) - r;
            }
            // Distancia con signo (negativa dentro) de la unión de dedos, palma y pulgar, en unidades de diseño
            float Sdf(Vector2 q)
            {
                float d = Seg(q, new(7.4f, 2.4f), new(7.4f, 11f), 1.9f);                  // índice
                d = Mathf.Min(d, Seg(q, new(11.4f, 8.2f), new(11.4f, 12f), 1.7f));       // medio
                d = Mathf.Min(d, Seg(q, new(14.6f, 9f), new(14.6f, 12f), 1.6f));         // anular
                d = Mathf.Min(d, Seg(q, new(17.6f, 10.4f), new(17.6f, 12.5f), 1.5f));    // meñique
                d = Mathf.Min(d, Seg(q, new(5.6f, 15.5f), new(2.4f, 11.6f), 1.7f));      // pulgar
                Vector2 c = new(11.6f, 16.2f), h = new(6.3f, 4.6f);                        // palma (caja redondeada)
                Vector2 e = new Vector2(Mathf.Abs(q.x - c.x), Mathf.Abs(q.y - c.y)) - h;
                float box = new Vector2(Mathf.Max(e.x, 0f), Mathf.Max(e.y, 0f)).magnitude + Mathf.Min(Mathf.Max(e.x, e.y), 0f) - 1.8f;
                return Mathf.Min(d, box);
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int white = 0, black = 0;
                for (int j = 0; j < SS; j++)
                for (int i = 0; i < SS; i++)
                {
                    var p = new Vector2(x + (i + 0.5f) / SS, H - (y + (j + 0.5f) / SS));
                    float d = Sdf((p - new Vector2(Pad, Pad)) / S) * S;
                    if (d > Stroke) continue;
                    if (d > -Stroke) black++; else white++;
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

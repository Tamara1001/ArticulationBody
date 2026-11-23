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
            UnityEngine.Random.InitState(7); // orden de cajas reproducible
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
            arrowTex = LoadCursor("arrow", out arrowHot);
            handTex = LoadCursor("link", out handHot);
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
        private readonly System.Random rng = new(20260523);
        private float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
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

        /// <summary>Sin nada que pulsar: orbita un poco la primera vez; después señala la pieza que se mueve,
        /// pasa por encima de algún botón, mira un sensor o deriva con calma, como alguien que explica.</summary>
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
            CargoBox moving = null;
            foreach (var b in spawner.Active) if (!b.Resolved) { moving = b; break; }
            double r = rng.NextDouble();
            if (moving != null && r < 0.42) yield return FollowBox(moving, Rand(0.9f, 1.7f));
            else if (r < 0.58) yield return PassOverButton();
            else
            {
                int which = BoxAt(Zone.Deck) != null ? 1 : BoxAt(Zone.Stop) != null ? 0 : rng.Next(0, 3);
                yield return Drift(SensorTarget(which), Rand(0.7f, 1.3f), Rand(10f, 26f));
            }
        }

        private Vector2 BoxOnScreen(CargoBox b)
        {
            Vector3 sp = Camera.main.WorldToScreenPoint(b.transform.position);
            return new Vector2(Mathf.Clamp(sp.x + 24f, 420f, 1500f), Mathf.Clamp(Height - sp.y - 10f, 140f, 940f));
        }

        /// <summary>Va hasta la caja que se mueve y la acompaña con un poco de retraso, como señalándola.</summary>
        private IEnumerator FollowBox(CargoBox b, float seconds)
        {
            yield return MoveCursor(BoxOnScreen(b), -1f);
            for (float t = 0f; t < seconds && b != null && b.gameObject.activeInHierarchy; t += Time.deltaTime)
            {
                cursorBase = Vector2.Lerp(cursorBase, BoxOnScreen(b), 1f - Mathf.Exp(-5f * Time.deltaTime));
                ApplyCursor();
                yield return null;
            }
        }

        /// <summary>Cruza un botón del HUD sin pulsarlo (la manito aparece y se va).</summary>
        private IEnumerator PassOverButton()
        {
            var buttons = root.Query<Button>().ToList();
            var b = buttons[rng.Next(0, buttons.Count)];
            Rect r = b.worldBound;
            Vector2 over = new(r.center.x + Rand(-r.width * 0.3f, r.width * 0.3f), r.center.y + Rand(-r.height * 0.2f, r.height * 0.2f));
            yield return MoveCursor(over, -1f);
            yield return Wait(Rand(0.08f, 0.3f));
            yield return MoveCursor(over + new Vector2(Rand(-140f, 140f), Rand(60f, 160f)), -1f);
        }

        /// <summary>Deriva lenta alrededor de un punto (vaivén en ocho), sin quedarse quieto.</summary>
        private IEnumerator Drift(Vector2 center, float seconds, float radius)
        {
            float phase = Rand(0f, 6.28f), w = Rand(1.4f, 2.6f);
            Vector2 start = center + new Vector2(Mathf.Cos(phase) * radius, Mathf.Sin(2f * phase) * radius * 0.5f);
            yield return MoveCursor(start, -1f);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float a = phase + t * w;
                cursorBase = Vector2.Lerp(cursorBase, center + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(2f * a) * radius * 0.5f), 1f - Mathf.Exp(-6f * Time.deltaTime));
                ApplyCursor();
                yield return null;
            }
        }

        private Vector2 SensorTarget(int index)
        {
            var rows = root.Query(className: "sensor").ToList();
            if (rows.Count == 0) return new Vector2(1700f, 190f);
            Rect r = rows[Mathf.Clamp(index, 0, rows.Count - 1)].worldBound;
            return new Vector2(r.center.x + Rand(-60f, 40f), r.center.y + Rand(-10f, 10f));
        }

        private IEnumerator Hover(float seconds, Vector2 where) => Drift(where, seconds, 14f);

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
                r.center.x + Rand(-r.width * 0.38f, r.width * 0.38f),
                r.center.y + Rand(-r.height * 0.3f, r.height * 0.3f));
            yield return MoveCursor(target, -1f, true);
            yield return Wait(rng.NextDouble() < 0.25 ? Rand(0.15f, 0.4f) : Rand(0.03f, 0.1f)); // a veces duda antes de pulsar
            // Pulsación visible: el botón se hunde un instante y el cursor se achica
            b.style.opacity = 0.65f; b.style.translate = new Translate(0, 1);
            cursor.style.scale = new Scale(new Vector2(0.9f, 0.9f));
            yield return Wait(0.09f);
            action();
            yield return Wait(0.07f);
            b.style.opacity = StyleKeyword.Null; b.style.translate = StyleKeyword.Null;
            cursor.style.scale = StyleKeyword.Null;
            yield return Wait(Rand(0.04f, 0.2f));
        }

        private static float Ease(float t) // arranque y frenada suaves, tramo medio rápido
        {
            float a = Mathf.Pow(t, 1.7f), b = Mathf.Pow(1f - t, 1.7f);
            return a / (a + b);
        }

        /// <summary>Movimiento curvo (Bézier de curvatura distinta cada vez): a veces rápido y decidido, a veces con
        /// duda (pausa a mitad de camino); sobrepaso con corrección solo de vez en cuando.</summary>
        private IEnumerator MoveCursor(Vector2 to, float duration, bool overshoot = false)
        {
            Vector2 from = cursorBase;
            float dist = Vector2.Distance(from, to);
            if (dist < 2f) { yield return null; yield break; }
            Vector2 dir = (to - from) / dist, perp = new(-dir.y, dir.x);
            if (duration < 0f) duration = Mathf.Clamp(0.2f + dist / 3000f, 0.25f, 0.5f) * Rand(0.8f, 1.5f);
            bool hesitate = dist > 260f && rng.NextDouble() < 0.2;
            bool over = overshoot && dist > 160f && rng.NextDouble() < 0.4;
            Vector2 aim = over ? to + dir * Rand(5f, 16f) : to;
            float bend = Rand(0.04f, 0.3f);
            if (hesitate)
            {
                Vector2 mid = Vector2.Lerp(from, aim, Rand(0.55f, 0.75f)) + perp * dist * Rand(-0.08f, 0.08f);
                yield return Curve(from, mid, duration * 0.6f, bend);
                yield return Wait(Rand(0.08f, 0.22f));
                yield return Curve(mid, aim, duration * 0.55f, bend * 0.6f);
            }
            else yield return Curve(from, aim, duration, bend);
            if (over) yield return Curve(aim, to, Rand(0.08f, 0.16f), 0.02f);
        }

        private IEnumerator Curve(Vector2 from, Vector2 to, float duration, float bend = 0.16f)
        {
            float dist = Vector2.Distance(from, to);
            Vector2 d = (to - from) / Mathf.Max(dist, 1f), perp = new(-d.y, d.x);
            float sign = rng.NextDouble() < 0.5 ? -1f : 1f;
            Vector2 c1 = from + (to - from) * Rand(0.2f, 0.4f) + perp * dist * bend * sign * Rand(0.6f, 1.2f);
            Vector2 c2 = from + (to - from) * Rand(0.65f, 0.85f) + perp * dist * bend * -sign * Rand(0f, 0.5f);
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
        private Vector2 arrowHot, handHot;

        private void SetShape(bool hand, bool force = false)
        {
            if (!force && hand == handShape) return;
            handShape = hand;
            var tex = hand ? handTex : arrowTex;
            Vector2 hot = hand ? handHot : arrowHot;
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
            Vector2 hot = handShape ? handHot : arrowHot;
            float t = Time.time;
            float jx = (Mathf.PerlinNoise(t * 0.9f, 0.37f) - 0.5f) * 4f + (Mathf.PerlinNoise(t * 3f, 5.1f) - 0.5f) * 0.8f;
            float jy = (Mathf.PerlinNoise(0.81f, t * 0.8f) - 0.5f) * 4f + (Mathf.PerlinNoise(7.7f, t * 3f) - 0.5f) * 0.8f;
            cursor.style.left = cursorBase.x + jx - hot.x; // la punta (o el dedo índice) queda en la posición
            cursor.style.top = cursorBase.y + jy - hot.y;
        }

        /// <summary>Cursores reales de Windows (aero_arrow / aero_link) convertidos a PNG de 32 px en Video/cursors
        /// (carpeta ignorada por Git): filtro Point, sin mipmaps, tamaño nativo y hotspot del .cur.</summary>
        private static Texture2D LoadCursor(string key, out Vector2 hotspot)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Video", "cursors"));
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.LoadImage(File.ReadAllBytes(Path.Combine(dir, key + ".png")), false);
            tex.filterMode = FilterMode.Point;
            hotspot = Vector2.zero;
            foreach (var line in File.ReadAllLines(Path.Combine(dir, "hotspots.txt")))
            {
                var parts = line.Split(' ');
                if (parts.Length == 3 && parts[0] == key) hotspot = new Vector2(float.Parse(parts[1]), float.Parse(parts[2]));
            }
            return tex;
        }
    }
}
#endif

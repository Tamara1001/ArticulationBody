using System.Collections.Generic;
using Station.CameraRig;
using Station.Cargo;
using Station.Controls;
using Station.Mechanism;
using UnityEngine;
using UnityEngine.UIElements;

namespace Station.UI
{
    /// <summary>
    /// HUD en UI Toolkit, construido por código: mando de cada joint (Target vs Real),
    /// sensores, reglas, tarea, guía de maniobra, avisos y ayuda.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class StationHud : MonoBehaviour
    {
        [SerializeField] private StationController station;
        [SerializeField] private StationInput input;
        [SerializeField] private CargoSpawner spawner;
        [SerializeField] private TaskTracker tracker;
        [SerializeField] private OrbitCamera orbitCamera;
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private float refreshRate = 15f;

        private VisualElement root;
        private readonly List<JointCard> cards = new();
        private Label chipMode, chipView, chipTime;
        private Label probeAngle, probeHeight, scaleValue, scaleSub, flapAngle, probeSub;
        private Label statOk, statBad, statDrop, statAvg;
        private Label guideStep, guideText;
        private VisualElement toasts, overlay, dryBanner;
        private Button dryButton;
        private float nextRefresh;
        private readonly List<(VisualElement el, float until)> liveToasts = new();

        private class JointCard
        {
            public JointDriveController Joint;
            public Label Target, Real, Lock, Sat;
            public VisualElement Fill, Marker;
            public Slider Slider;
            public bool Dragging;
            public System.Func<bool> Locked;
        }

        public void Configure(StationController s, StationInput i, CargoSpawner sp, TaskTracker t, OrbitCamera cam, StyleSheet sheet)
        {
            station = s; input = i; spawner = sp; tracker = t; orbitCamera = cam; styleSheet = sheet;
        }

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            root = doc.rootVisualElement;
            root.Clear();
            if (styleSheet) root.styleSheets.Add(styleSheet);
            root.pickingMode = PickingMode.Ignore;

            var main = Box("root");
            root.Add(main);
            main.Add(BuildTopBar());

            var body = Box("body");
            main.Add(body);
            body.Add(BuildLeft());
            body.Add(BuildRight());

            main.Add(BuildBottom());

            dryBanner = new Label("PRUEBA EN SECO · drives anulados: el mecanismo cae por gravedad hasta sus límites");
            dryBanner.AddToClassList("dry-banner");
            root.Add(dryBanner);
            root.Add(BuildHelp());
            overlay.EnableInClassList("overlay--on", true); // la ayuda se muestra al empezar

            tracker.OnEvent += OnTaskEvent;
            input.OnToggleHelp += ToggleHelp;
            input.OnCycleCamera += orbitCamera.CycleView;
            orbitCamera.IsPointerOverUI = IsPointerOverUI;
        }

        private void OnDisable()
        {
            if (tracker) tracker.OnEvent -= OnTaskEvent;
            if (input) { input.OnToggleHelp -= ToggleHelp; input.OnCycleCamera -= orbitCamera.CycleView; }
        }

        // ------------------------------------------------------------------ construcción

        private static VisualElement Box(params string[] classes)
        {
            var v = new VisualElement();
            foreach (var c in classes) v.AddToClassList(c);
            v.pickingMode = PickingMode.Ignore;
            return v;
        }

        private static Label Text(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        private static Button Btn(string text, System.Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("btn");
            foreach (var c in classes) b.AddToClassList(c);
            b.focusable = false; // así las teclas siguen yendo al simulador
            return b;
        }

        private static VisualElement Panel(string title)
        {
            var p = new VisualElement();
            p.AddToClassList("panel");
            if (!string.IsNullOrEmpty(title)) p.Add(Text(title, "section-title"));
            return p;
        }

        private VisualElement BuildTopBar()
        {
            var bar = Panel(null);
            bar.AddToClassList("topbar");

            var brand = Box("brand");
            brand.Add(Text("▲", "brand-mark"));
            var titles = Box();
            titles.Add(Text("Estación de Transferencia y Clasificación", "title"));
            titles.Add(Text("Simulador · Unity ArticulationBody · 9 cuerpos · 4 tipos de joint", "subtitle"));
            brand.Add(titles);
            bar.Add(brand);

            var chips = Box("chips");
            chipMode = Text("Operación", "chip", "chip--ok");
            chipView = Text("Vista: General", "chip");
            chipTime = Text("00:00", "chip");
            chips.Add(chipMode); chips.Add(chipView); chips.Add(chipTime);
            bar.Add(chips);

            var actions = Box("actions");
            actions.pickingMode = PickingMode.Position;
            actions.Add(Btn("Nueva caja  [N]", () => spawner.Spawn(), "btn--primary"));
            actions.Add(Btn("Vista  [C]", orbitCamera.CycleView));
            dryButton = Btn("Prueba en seco  [T]", () => station.SetDryRun(!station.DryRun), "btn--danger");
            actions.Add(dryButton);
            actions.Add(Btn("Reiniciar", station.ResetStation));
            actions.Add(Btn("Ayuda  [H]", ToggleHelp));
            bar.Add(actions);
            return bar;
        }

        private VisualElement BuildLeft()
        {
            var side = Box("side");
            var p = Panel("MANDO · TARGET vs REAL");
            side.Add(p);

            AddCard(p, station.Lift, "Prismatic · Y", () => station.LiftLocked, new[]
            {
                ("Carga", StationInput.LevelLoad), ("Nivel 1", StationInput.Level1), ("Nivel 2", StationInput.Level2)
            }, "1 2 3");
            AddCard(p, station.Turntable, "Revolute · Y", () => station.TurntableLocked, new[]
            {
                ("Derecha 0°", StationInput.AngleRight), ("Izquierda 180°", StationInput.AngleLeft)
            }, "← →");
            AddCard(p, station.Pusher, "Prismatic · X local", null, new[]
            {
                ("Retraer", 0f), ("Empujar", station.Pusher.MaxValue)
            }, null);
            AddCard(p, station.Gate, "Prismatic · Y", null, new[]
            {
                ("Subir (retiene)", station.Gate.MaxValue), ("Bajar (libera)", 0f)
            }, "Espacio");
            AddCard(p, station.Roller, "Revolute · X · velocidad", null, new[]
            {
                ("Reversa", StationInput.RollerReverse), ("Paro", 0f), ("Marcha", StationInput.RollerRun)
            }, "R / F");
            cards[^1].Slider.parent.AddToClassList("joint--last");
            return side;
        }

        private void AddCard(VisualElement parent, JointDriveController joint, string type,
            System.Func<bool> locked, (string label, float value)[] presets, string presetKeys)
        {
            var card = new JointCard { Joint = joint, Locked = locked };
            var el = Box("joint");
            el.pickingMode = PickingMode.Position;

            var head = Box("joint-head");
            var nameRow = Box("joint-meta");
            nameRow.Add(Text(joint.DisplayName, "joint-name"));
            nameRow.Add(Text(type, "joint-type"));
            head.Add(nameRow);
            var meta = Box("joint-meta");
            card.Sat = Text("SATURADO", "sat");
            card.Lock = Text("ENCLAVADO", "lock");
            meta.Add(card.Sat); meta.Add(card.Lock);
            foreach (var k in joint.KeyHint.Split(' ')) if (k.Length > 0) meta.Add(Text(k, "key"));
            head.Add(meta);
            el.Add(head);

            var values = Box("values");
            var vt = Box("value-block"); vt.Add(Text("TARGET", "value-label")); card.Target = Text("-", "value-target"); vt.Add(card.Target);
            var vr = Box("value-block"); vr.Add(Text("REAL", "value-label")); card.Real = Text("-", "value-real"); vr.Add(card.Real);
            values.Add(vt); values.Add(vr);
            el.Add(values);

            var bar = Box("bar");
            card.Fill = Box("bar-fill");
            card.Marker = Box("bar-target");
            bar.Add(card.Fill); bar.Add(card.Marker);
            el.Add(bar);

            card.Slider = new Slider(joint.MinValue, joint.MaxValue) { value = joint.Command };
            card.Slider.AddToClassList("joint-slider");
            card.Slider.focusable = false;
            card.Slider.RegisterValueChangedCallback(e => { if (card.Dragging) joint.SetCommand(e.newValue); });
            card.Slider.RegisterCallback<PointerDownEvent>(_ => card.Dragging = true, TrickleDown.TrickleDown);
            card.Slider.RegisterCallback<PointerUpEvent>(_ => card.Dragging = false, TrickleDown.TrickleDown);
            card.Slider.RegisterCallback<PointerCaptureOutEvent>(_ => card.Dragging = false);
            el.Add(card.Slider);

            var row = Box("presets");
            foreach (var (label, value) in presets)
            {
                float v = value;
                row.Add(Btn(label, () => joint.SetCommand(v), "btn--small"));
            }
            if (!string.IsNullOrEmpty(presetKeys)) row.Add(Text(presetKeys, "key"));
            el.Add(row);

            parent.Add(el);
            cards.Add(card);
        }

        private VisualElement BuildRight()
        {
            var side = Box("side");

            var sensors = Panel("SENSORES");
            sensors.Add(SensorRow("Palpador", "Spherical pasivo · altura", out probeHeight, out probeSub));
            sensors.Add(SensorRow("Balanza", "fuerza del drive del carro", out scaleValue, out scaleSub));
            sensors.Add(SensorRow("Compuerta", "Revolute pasivo · salida Der. N1", out flapAngle, out _));
            probeAngle = probeSub;
            side.Add(sensors);

            var rules = Panel("REGLA DE CLASIFICACIÓN");
            rules.Add(Rule("Altura < 30 cm", "Nivel 1"));
            rules.Add(Rule("Altura ≥ 30 cm", "Nivel 2"));
            rules.Add(Rule("Masa < 10 kg", "Izquierda (180°)"));
            rules.Add(Rule("Masa ≥ 10 kg", "Derecha (0°)"));
            side.Add(rules);

            var task = Panel("TAREA");
            var stats = Box("stats");
            stats.Add(Stat(out statOk, "Correctas", "stat--ok"));
            stats.Add(Stat(out statBad, "Errores", "stat--bad"));
            stats.Add(Stat(out statDrop, "Caídas", "stat--warn"));
            stats.Add(Stat(out statAvg, "Tiempo medio", null));
            task.Add(stats);
            side.Add(task);
            return side;
        }

        private static VisualElement SensorRow(string name, string sub, out Label value, out Label subLabel)
        {
            var row = Box("sensor");
            var left = Box();
            left.Add(Text(name, "sensor-name"));
            subLabel = Text(sub, "sensor-sub");
            left.Add(subLabel);
            row.Add(left);
            value = Text("—", "sensor-value");
            row.Add(value);
            return row;
        }

        private static VisualElement Rule(string k, string v)
        {
            var r = Box("rule");
            r.Add(Text(k, "rule-k"));
            r.Add(Text(v, "rule-v"));
            return r;
        }

        private static VisualElement Stat(out Label n, string label, string cls)
        {
            var s = Box("stat");
            n = Text("0", "stat-n");
            if (cls != null) n.AddToClassList(cls);
            s.Add(n);
            s.Add(Text(label, "stat-l"));
            return s;
        }

        private VisualElement BuildBottom()
        {
            var bottom = Box("bottom");
            var guide = Panel(null);
            guide.AddToClassList("guide");
            guideStep = Text("1", "guide-step");
            guideText = Text("", "guide-text");
            guide.Add(guideStep); guide.Add(guideText);
            bottom.Add(guide);
            toasts = Box("toasts");
            bottom.Add(toasts);
            return bottom;
        }

        private VisualElement BuildHelp()
        {
            overlay = new VisualElement();
            overlay.AddToClassList("overlay");
            var help = Panel(null);
            help.AddToClassList("help");
            help.Add(Text("Estación de Transferencia y Clasificación", "help-title"));
            help.Add(Text("Llegan cajas de cartón que por fuera se ven iguales. Medí su ALTURA con el palpador y su MASA con la balanza del elevador, " +
                          "y llevá cada una a su destino: 2 lados × 2 niveles. El fantasma cian muestra el TARGET de cada joint; la pieza real lo sigue.", "help-intro"));

            var cols = Box("help-cols");
            var c1 = Box("help-col");
            c1.Add(HelpRow("W / S", "Elevar / bajar carro"));
            c1.Add(HelpRow("1 · 2 · 3", "Carga · Nivel 1 · Nivel 2"));
            c1.Add(HelpRow("A / D", "Girar mesa"));
            c1.Add(HelpRow("← / →", "Mesa a Izquierda / Derecha"));
            c1.Add(HelpRow("E / Q", "Empujar / retraer"));
            c1.Add(HelpRow("Espacio", "Tope: liberar / retener"));
            c1.Add(HelpRow("R / F", "Rodillo: marcha / reversa"));
            var c2 = Box("help-col");
            c2.Add(HelpRow("N", "Nueva caja"));
            c2.Add(HelpRow("Clic der./izq. + arrastrar", "Orbitar cámara"));
            c2.Add(HelpRow("Rueda · botón medio", "Zoom · desplazar"));
            c2.Add(HelpRow("C", "Cambiar vista"));
            c2.Add(HelpRow("T", "Prueba en seco (sin drives)"));
            c2.Add(HelpRow("Retroceso", "Reiniciar"));
            c2.Add(HelpRow("H", "Mostrar / ocultar ayuda"));
            cols.Add(c1); cols.Add(c2);
            help.Add(cols);
            help.Add(Text("Maniobra: N → Espacio (libera, el palpador mide) → R con carro en Carga y mesa a 0° → leé la balanza → " +
                          "elevá y girá según la regla → E para empujar → Q para retraer → volvé a Carga. " +
                          "Enclavamiento: el carro y la mesa solo se mueven con el empujador retraído.", "help-steps"));
            var footer = Box("help-footer");
            footer.pickingMode = PickingMode.Position;
            footer.Add(Btn("Comenzar", ToggleHelp, "btn--primary"));
            help.Add(footer);
            overlay.Add(help);
            return overlay;
        }

        private static VisualElement HelpRow(string k, string v)
        {
            var r = Box("help-row");
            r.Add(Text(k, "help-k"));
            r.Add(Text(v, "help-v"));
            return r;
        }

        // ------------------------------------------------------------------ actualización

        private void ToggleHelp() => overlay.EnableInClassList("overlay--on", !overlay.ClassListContains("overlay--on"));

        private bool IsPointerOverUI(Vector2 screenPos)
        {
            var panel = root?.panel;
            if (panel == null) return false;
            if (overlay.ClassListContains("overlay--on")) return true;
            Vector2 p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            var picked = panel.Pick(p);
            return picked != null && picked != root;
        }

        private void Update()
        {
            UpdateToasts();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 1f / refreshRate;

            foreach (var c in cards) UpdateCard(c);

            bool dry = station.DryRun;
            chipMode.text = dry ? "Prueba en seco" : "Operación";
            chipMode.EnableInClassList("chip--ok", !dry);
            chipMode.EnableInClassList("chip--warn", dry);
            dryButton.EnableInClassList("btn--on", dry);
            dryBanner.EnableInClassList("dry-banner--on", dry);
            chipView.text = "Vista: " + orbitCamera.CurrentViewName;
            int s = Mathf.FloorToInt(Time.timeSinceLevelLoad);
            chipTime.text = $"{s / 60:00}:{s % 60:00}";

            // Sensores
            var probe = station.HeightProbe;
            if (probe.InContact)
            {
                probeHeight.text = $"{probe.LiveHeight * 100f:0} cm";
                probeHeight.EnableInClassList("sensor-value--live", true);
            }
            else
            {
                probeHeight.text = probe.HeldHeight < 0f ? "—" : $"{probe.HeldHeight * 100f:0} cm";
                probeHeight.EnableInClassList("sensor-value--live", false);
            }
            probeAngle.text = $"Spherical pasivo · ángulo {Mathf.Abs(probe.Angle):0}°" + (probe.HeldHeight >= 0f && !probe.InContact ? " · última lectura" : "");

            var cell = station.LoadCell;
            if (!cell.IsReady) { scaleValue.text = "tarando…"; scaleSub.text = "fuerza del drive del carro"; }
            else
            {
                scaleValue.text = $"{cell.MassKg:0.0} kg";
                scaleValue.EnableInClassList("sensor-value--dim", !cell.IsStable);
                scaleSub.text = cell.IsStable ? "lectura estable" : "carro en movimiento…";
            }
            flapAngle.text = $"{Mathf.Abs(station.Flap.Angle):0}°";

            statOk.text = tracker.Correct.ToString();
            statBad.text = tracker.Wrong.ToString();
            statDrop.text = tracker.Dropped.ToString();
            statAvg.text = tracker.Correct + tracker.Wrong > 0 ? $"{tracker.AverageTime:0}s" : "—";

            UpdateGuide();
        }

        private void UpdateCard(JointCard c)
        {
            var j = c.Joint;
            string fmt = j.Unit == "°" ? "0.0" : "0.000";
            c.Target.text = $"{j.Target.ToString(fmt)} {j.Unit}";
            c.Real.text = $"{j.Actual.ToString(fmt)} {j.Unit}";
            float range = Mathf.Max(1e-4f, j.MaxValue - j.MinValue);
            float fr = Mathf.Clamp01((j.Actual - j.MinValue) / range);
            float ft = Mathf.Clamp01((j.Target - j.MinValue) / range);
            c.Fill.style.width = Length.Percent(fr * 100f);
            c.Marker.style.left = Length.Percent(ft * 100f);
            if (!c.Dragging) c.Slider.SetValueWithoutNotify(j.Command);
            c.Lock.EnableInClassList("lock--on", c.Locked != null && c.Locked());
            c.Sat.EnableInClassList("sat--on", j.IsSaturated);
        }

        private void UpdateGuide()
        {
            // Paso sugerido según dónde está la caja más avanzada sin resolver
            CargoBox box = null;
            foreach (var b in spawner.Active)
                if (!b.Resolved && (box == null || b.transform.position.z > box.transform.position.z)) box = b;

            string step, text;
            if (station.DryRun) { step = "!"; text = "Prueba en seco activa: los drives están anulados. Pulsá T para volver a operar."; }
            else if (box == null) { step = "1"; text = "Pedí una caja nueva con N (o el botón «Nueva caja»)."; }
            else
            {
                Vector3 p = box.transform.position;
                bool onDeck = new Vector2(p.x, p.z).magnitude < 0.42f && p.y > 0.75f;
                if (p.z < -1.15f && Mathf.Abs(p.x) < 0.5f)
                {
                    step = "2";
                    text = station.Gate.Command > 0.05f ? "La caja espera en el tope. Bajalo con Espacio para que pase bajo el palpador."
                                                        : "Tope abierto: la caja baja hacia la placa de transferencia.";
                }
                else if (p.z < -0.42f && Mathf.Abs(p.x) < 0.5f)
                {
                    step = "3";
                    bool ready = station.Lift.Actual < 0.02f && Mathf.Abs(station.Turntable.Actual) < 2f;
                    text = ready ? "Anotá la altura del palpador. Subí el tope (Espacio) y poné el rodillo en marcha (R) para pasar la caja a la mesa."
                                 : "Antes de transferir: carro en Carga (1) y mesa a 0° (→).";
                }
                else if (onDeck)
                {
                    step = "4";
                    bool aligned = (Mathf.Abs(station.Lift.Actual - StationInput.Level1) < 0.02f || Mathf.Abs(station.Lift.Actual - StationInput.Level2) < 0.02f)
                                   && (Mathf.Abs(station.Turntable.Actual) < 1.5f || Mathf.Abs(station.Turntable.Actual - 180f) < 1.5f);
                    text = station.Roller.Command != 0f ? "Caja en la mesa: detené el rodillo (R) y leé la balanza."
                         : aligned ? "Alineado con una salida: empujá con E y retraé con Q."
                         : "Leé la balanza. Elevá (2 / 3) y girá (← / →) según la regla de clasificación.";
                }
                else { step = "5"; text = "Caja en camino al contenedor… Retraé el empujador (Q) y volvé a Carga (1) y 0° (→)."; }
            }
            guideStep.text = step;
            guideText.text = text;
        }

        private void OnTaskEvent(TaskTracker.TaskEvent e)
        {
            var t = Text(e.Message, "toast");
            t.AddToClassList(e.Kind switch
            {
                TaskTracker.EventKind.Correct => "toast--ok",
                TaskTracker.EventKind.Wrong => "toast--bad",
                TaskTracker.EventKind.Dropped => "toast--warn",
                _ => "toast"
            });
            toasts.Add(t);
            liveToasts.Add((t, Time.unscaledTime + 5f));
            while (liveToasts.Count > 4) { liveToasts[0].el.RemoveFromHierarchy(); liveToasts.RemoveAt(0); }
        }

        private void UpdateToasts()
        {
            for (int i = liveToasts.Count - 1; i >= 0; i--)
            {
                var (el, until) = liveToasts[i];
                float left = until - Time.unscaledTime;
                if (left < 0.5f) el.style.opacity = Mathf.Max(0f, left / 0.5f);
                if (left <= 0f) { el.RemoveFromHierarchy(); liveToasts.RemoveAt(i); }
            }
        }
    }
}

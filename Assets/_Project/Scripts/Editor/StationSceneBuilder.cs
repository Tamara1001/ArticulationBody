using System.Collections.Generic;
using Station.CameraRig;
using Station.Cargo;
using Station.Controls;
using Station.Mechanism;
using Station.UI;
using Station.Visual;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Station.EditorTools
{
    /// <summary>
    /// Construye la escena completa de la estación desde código. Todas las medidas, anclas,
    /// límites, masas y drives coinciden con Docs/DESIGN_AND_JOINTS.md.
    /// Menú: Estación ▸ Construir escena.
    /// </summary>
    public static class StationSceneBuilder
    {
        private const string Root = "Assets/_Project";
        private const string ScenePath = Root + "/Scenes/Station.unity";
        private const string MatFolder = Root + "/Materials";
        private const string PhysFolder = Root + "/Physics";

        // Geometría (ver DESIGN_AND_JOINTS.md §2 y §8)
        private const float Tan7 = 0.12278f;
        // Placa de transferencia inclinada 3° (z −1.05 → −0.80): la caja siempre termina sobre el rodillo
        private const float RollerZ = -0.75f;
        private const float Tan3 = 0.0524f;
        // Alturas escalonadas que siempre bajan en el sentido de avance: rodillo > placa de salida > mesa
        private const float RollerCrest = 0.012f, OutPlateTop = 0.003f;
        private const float TransferRise = 0.30f * Tan3;           // altura extra en z = −1.05
        private const float SlopeBase = 0.80f + TransferRise;      // altura de la rampa en z = −1.05
        private const float DeckTop = 0.80f;

        private static readonly Dictionary<string, Material> Mats = new();
        private static Material ghostMat;
        private static PhysicsMaterial pmCardboard, pmFreeRollers, pmTransfer, pmRubber, pmDeck, pmSteel;

        [MenuItem("Estación/Construir escena", priority = 0)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            PrepareFolders();
            ApplyProjectSettings();
            CreateMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var env = new GameObject("Entorno").transform;
            var lookGo = BuildLighting(env);
            BuildFloor(env);

            var mech = BuildMechanism(out var refs);
            var statics = BuildStatics(env, refs);

            var systems = new GameObject("Sistemas");
            var tracker = systems.AddComponent<TaskTracker>();
            var spawner = BuildCargo(systems.transform, tracker);
            tracker.Configure(spawner);

            var station = mech.gameObject.AddComponent<StationController>();
            var loadCell = mech.gameObject.AddComponent<LoadCell>();
            loadCell.Configure(refs.lift);
            station.Configure(refs.gate, refs.roller, refs.lift, refs.table, refs.pusher, refs.probe, refs.flap, loadCell,
                new[] { refs.gatePinCol, refs.gatePinCol }, new[] { statics.slopeCol, statics.transferCol });

            var input = systems.AddComponent<StationInput>();
            input.Configure(station, spawner);

            var cam = BuildCamera();
            BuildHud(station, input, spawner, tracker, cam);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Estación] Escena construida en " + ScenePath);
        }

        // ================================================================== preparación

        private static void PrepareFolders()
        {
            foreach (var f in new[] { MeshFactory.Folder, MatFolder, PhysFolder })
                if (AssetDatabase.IsValidFolder(f)) AssetDatabase.DeleteAsset(f);
            foreach (var f in new[] { "Scenes", "Meshes", "Materials", "Physics", "Settings", "UI" })
                if (!AssetDatabase.IsValidFolder($"{Root}/{f}")) AssetDatabase.CreateFolder(Root, f);
            MeshFactory.ResetCache();
            Mats.Clear();
        }

        private static void ApplyProjectSettings()
        {
            // Física: 100 Hz y solver TGS (más estable para articulaciones)
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
            // Fixed Timestep es racional: m_Count / m_Rate (141120000 Hz) → 1411200 = 0.01 s
            var rate = tm.FindProperty("Fixed Timestep.m_Rate.m_Numerator").longValue;
            tm.FindProperty("Fixed Timestep.m_Count").longValue = rate / 100;
            tm.FindProperty("Maximum Allowed Timestep").floatValue = 0.05f;
            tm.ApplyModifiedProperties();
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            var dyn = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
            var solver = dyn.FindProperty("m_SolverType");
            if (solver != null) { solver.intValue = 1; dyn.ApplyModifiedProperties(); }

            // WebGL para itch.io
            PlayerSettings.productName = "Estación de Clasificación";
            PlayerSettings.companyName = "Simuladores";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.runInBackground = true;

            // Sombras de URP: distancia acotada a la estación
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (rp == null) continue;
                rp.shadowDistance = 18f;
                rp.shadowCascadeCount = 1;
                rp.mainLightShadowmapResolution = 2048;
                EditorUtility.SetDirty(rp);
            }
        }

        // ================================================================== materiales

        private static Material Mat(string name, Color color, float toon = 0.7f, float rim = 0.2f)
        {
            var m = new Material(Shader.Find("Station/IndustrialToon")) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Toon", toon);
            m.SetFloat("_RimStrength", rim);
            AssetDatabase.CreateAsset(m, $"{MatFolder}/{name}.mat");
            Mats[name] = m;
            return m;
        }

        private static PhysicsMaterial Phys(string name, float friction, PhysicsMaterialCombine combine = PhysicsMaterialCombine.Average)
        {
            var p = new PhysicsMaterial(name)
            {
                dynamicFriction = friction, staticFriction = friction, bounciness = 0f,
                frictionCombine = combine, bounceCombine = PhysicsMaterialCombine.Minimum
            };
            AssetDatabase.CreateAsset(p, $"{PhysFolder}/{name}.physicMaterial");
            return p;
        }

        private static void CreateMaterials()
        {
            Mat("Frame", new Color(0.20f, 0.24f, 0.29f));
            Mat("Steel", new Color(0.60f, 0.64f, 0.68f), 0.6f, 0.3f);
            Mat("Yellow", new Color(0.96f, 0.71f, 0.06f));
            Mat("Orange", new Color(0.94f, 0.42f, 0.12f));
            Mat("Rubber", new Color(0.09f, 0.09f, 0.10f), 0.8f, 0.35f);
            Mat("Red", new Color(0.88f, 0.22f, 0.18f));
            Mat("BinLeft", new Color(0.16f, 0.50f, 0.80f));
            Mat("BinRight", new Color(0.82f, 0.28f, 0.20f));
            Mat("Line", new Color(0.98f, 0.78f, 0.10f), 1f, 0f).SetFloat("_OutlineScale", 0f);

            var hazard = Mat("Hazard", new Color(0.96f, 0.71f, 0.06f));
            hazard.SetFloat("_StripeStrength", 1f);
            hazard.SetFloat("_StripeScale", 7f);

            var deck = Mat("Deck", new Color(0.33f, 0.37f, 0.42f));
            deck.SetFloat("_PanelStrength", 0.5f);
            deck.SetFloat("_PanelScale", 0.175f);
            deck.SetFloat("_PanelVariation", 0.03f);

            var floor = Mat("Floor", new Color(0.34f, 0.36f, 0.39f), 0.5f, 0f);
            floor.SetFloat("_PanelStrength", 0.7f);
            floor.SetFloat("_PanelScale", 1.5f);
            floor.SetFloat("_PanelVariation", 0.05f);
            floor.SetFloat("_OutlineScale", 0f);

            foreach (var (name, top) in new[] { ("CardboardLow", 0.10f), ("CardboardTall", 0.20f) })
            {
                var c = Mat(name, new Color(0.74f, 0.53f, 0.32f));
                c.SetFloat("_TapeStrength", 1f);
                c.SetFloat("_TapeWidth", 0.05f);
                c.SetFloat("_TapeTop", top);
                c.SetColor("_TapeColor", new Color(0.86f, 0.73f, 0.50f));
            }

            ghostMat = new Material(Shader.Find("Station/TargetGhost")) { name = "Ghost" };
            AssetDatabase.CreateAsset(ghostMat, $"{MatFolder}/Ghost.mat");

            pmCardboard = Phys("Carton", 0.4f);
            pmFreeRollers = Phys("RodillosLibres", 0.03f, PhysicsMaterialCombine.Minimum);
            pmTransfer = Phys("PlacaTransferencia", 0.03f, PhysicsMaterialCombine.Minimum); // rodillos libres
            pmRubber = Phys("Goma", 0.9f, PhysicsMaterialCombine.Maximum);
            pmDeck = Phys("PlataformaMesa", 0.10f, PhysicsMaterialCombine.Minimum);
            pmSteel = Phys("Acero", 0.3f);
        }

        // ================================================================== helpers de geometría

        /// <summary>Pieza visual (sin física). Opcionalmente agrega un BoxCollider del mismo tamaño.</summary>
        private static GameObject Block(Transform parent, string name, Vector3 size, Vector3 localPos, string mat,
            Quaternion? rot = null, PhysicsMaterial collider = null, float bevel = 0.008f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Box(size, bevel);
            go.AddComponent<MeshRenderer>().sharedMaterial = Mats[mat];
            if (collider != null)
            {
                var c = go.AddComponent<BoxCollider>();
                c.size = size;
                c.sharedMaterial = collider;
            }
            return go;
        }

        private static GameObject Part(Transform parent, string name, Mesh mesh, Vector3 localPos, string mat, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mats[mat];
            return go;
        }

        /// <summary>Crea un ArticulationBody sin rotar en mundo, con el eje del joint = X del ancla.</summary>
        private static ArticulationBody Body(string name, Transform parent, Vector3 worldPos, ArticulationJointType type,
            Quaternion anchorRot, float mass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.identity;
            var ab = go.AddComponent<ArticulationBody>();
            ab.mass = mass;
            ab.jointType = type;
            ab.matchAnchors = true;
            ab.anchorPosition = Vector3.zero;
            ab.anchorRotation = anchorRot;
            ab.linearDamping = 0.05f;
            ab.angularDamping = 0.05f;
            return ab;
        }

        private static Quaternion AxisTo(Vector3 axis) => Quaternion.FromToRotation(Vector3.right, axis);

        private static void Limits(ArticulationBody ab, float lower, float upper)
        {
            var d = ab.xDrive;
            d.lowerLimit = lower; d.upperLimit = upper;
            d.stiffness = 0f; d.damping = 0f; d.forceLimit = 0f;
            ab.xDrive = d;
        }

        private static void Prismatic(ArticulationBody ab, float lower, float upper)
        {
            ab.linearLockX = ArticulationDofLock.LimitedMotion;
            ab.linearLockY = ArticulationDofLock.LockedMotion;
            ab.linearLockZ = ArticulationDofLock.LockedMotion;
            Limits(ab, lower, upper);
        }

        private static void Revolute(ArticulationBody ab, float lower, float upper, bool free = false)
        {
            ab.twistLock = free ? ArticulationDofLock.FreeMotion : ArticulationDofLock.LimitedMotion;
            Limits(ab, lower, upper);
        }

        // ================================================================== mecanismo

        private struct MechRefs
        {
            public JointDriveController gate, roller, lift, table, pusher;
            public PassiveJointProbe probe, flap;
            public Collider gatePinCol;
        }

        private static Transform BuildMechanism(out MechRefs r)
        {
            r = default;

            // ---------- Base (raíz Immovable)
            var baseAb = Body("Base", null, Vector3.zero, ArticulationJointType.FixedJoint, Quaternion.identity, 500f);
            baseAb.immovable = true;
            var bt = baseAb.transform;
            // Columna del elevador (visual, sin collider: nada la toca)
            Block(bt, "Columna", new Vector3(0.16f, 2.30f, 0.16f), new Vector3(0f, 1.15f, 0.62f), "Frame");
            Block(bt, "Columna_Pie", new Vector3(0.50f, 0.06f, 0.40f), new Vector3(0f, 0.03f, 0.62f), "Frame");
            Block(bt, "Columna_Tapa", new Vector3(0.24f, 0.08f, 0.24f), new Vector3(0f, 2.34f, 0.62f), "Yellow");
            Block(bt, "Riel_Izq", new Vector3(0.03f, 2.1f, 0.03f), new Vector3(-0.06f, 1.15f, 0.53f), "Steel");
            Block(bt, "Riel_Der", new Vector3(0.03f, 2.1f, 0.03f), new Vector3(0.06f, 1.15f, 0.53f), "Steel");
            // Marcas de nivel en la columna
            foreach (var (lvl, y) in new[] { ("Carga", DeckTop), ("N1", DeckTop + StationInput.Level1), ("N2", DeckTop + StationInput.Level2) })
                Block(bt, "Marca_" + lvl, new Vector3(0.20f, 0.015f, 0.02f), new Vector3(0f, y, 0.52f), "Line");

            // ---------- Tope (Prismatic Y)
            var gate = Body("Tope", bt, new Vector3(0f, 0.721f + TransferRise, -1.30f), ArticulationJointType.PrismaticJoint, AxisTo(Vector3.up), 1.5f);
            Prismatic(gate, 0f, 0.13f);
            var pin = Part(gate.transform, "Perno", MeshFactory.Prism(10, 0.03f, 0.20f, 1), Vector3.zero, "Hazard");
            var pinCol = pin.AddComponent<CapsuleCollider>();
            pinCol.radius = 0.03f; pinCol.height = 0.20f; pinCol.direction = 1; pinCol.sharedMaterial = pmSteel;
            r.gatePinCol = pinCol;
            r.gate = gate.gameObject.AddComponent<JointDriveController>();
            r.gate.Configure("Tope", "m", "Espacio", JointDriveController.DriveMode.Position, 0f, 0.13f, 0.4f, 0.13f, 5000f, 150f, 300f);
            Block(bt, "Tope_Guia", new Vector3(0.12f, 0.12f, 0.12f), new Vector3(0f, 0.62f + TransferRise, -1.30f), "Frame");

            // ---------- Rodillo motorizado (Revolute X, velocidad)
            var roller = Body("Rodillo", bt, new Vector3(0f, DeckTop + RollerCrest - 0.05f, RollerZ), ArticulationJointType.RevoluteJoint, Quaternion.identity, 4f);
            Revolute(roller, 0f, 0f, true);
            roller.maxAngularVelocity = 60f;
            Part(roller.transform, "Rodillo_Goma", MeshFactory.Prism(14, 0.05f, 0.52f, 0), Vector3.zero, "Rubber");
            Part(roller.transform, "Rodillo_Marca", MeshFactory.Box(new Vector3(0.53f, 0.012f, 0.02f), 0.002f), new Vector3(0f, 0.045f, 0f), "Yellow");
            var rollCol = roller.gameObject.AddComponent<CapsuleCollider>();
            rollCol.direction = 0; rollCol.radius = 0.05f; rollCol.height = 0.62f; rollCol.sharedMaterial = pmRubber;
            r.roller = roller.gameObject.AddComponent<JointDriveController>();
            r.roller.Configure("Rodillo", "m/s", "R F", JointDriveController.DriveMode.Velocity, -0.5f, 1.8f, 3.0f, 0f, 0f, 20f, 30f, 0.05f); // damping alto: detenido funciona como freno
            Block(bt, "Rodillo_Soporte_Izq", new Vector3(0.04f, 0.16f, 0.10f), new Vector3(-0.30f, 0.70f, RollerZ), "Frame");
            Block(bt, "Rodillo_Soporte_Der", new Vector3(0.04f, 0.16f, 0.10f), new Vector3(0.30f, 0.70f, RollerZ), "Frame");
            Part(bt, "Motor_Rodillo", MeshFactory.Prism(10, 0.06f, 0.16f, 0), new Vector3(0.40f, 0.70f, RollerZ), "Orange");

            // ---------- Palpador (Spherical pasivo) sobre la rampa, antes del tope
            // La caja baja por gravedad, lo levanta y se detiene en el tope con la bola apoyada encima.
            // Ancla: X = abajo (twist, bloqueado), Y = mundo X (swing principal), Z = mundo Z
            const float probeArm = 0.60f, probeTip = 0.04f;
            Vector3 probePivot = new(0f, 1.60f, -1.95f);
            var probe = Body("Palpador", bt, probePivot, ArticulationJointType.SphericalJoint,
                Quaternion.LookRotation(Vector3.forward, Vector3.right), 0.4f);
            probe.twistLock = ArticulationDofLock.LockedMotion;
            probe.swingYLock = ArticulationDofLock.LimitedMotion;
            probe.swingZLock = ArticulationDofLock.LimitedMotion;
            var yd = probe.yDrive; yd.lowerLimit = -85f; yd.upperLimit = 85f; yd.stiffness = 0f; yd.damping = 0f; probe.yDrive = yd;
            var zd = probe.zDrive; zd.lowerLimit = -10f; zd.upperLimit = 10f; zd.stiffness = 0f; zd.damping = 0f; probe.zDrive = zd;
            probe.jointFriction = 0.05f;
            Part(probe.transform, "Palpador_Eje", MeshFactory.Prism(8, 0.025f, 0.08f, 0), Vector3.zero, "Steel");
            Block(probe.transform, "Palpador_Brazo", new Vector3(0.02f, probeArm - 0.03f, 0.02f), new Vector3(0f, -(probeArm - 0.03f) * 0.5f, 0f), "Steel", null, null, 0.004f);
            var tip = Part(probe.transform, "Palpador_Bola", MeshFactory.Sphere(probeTip, 10), new Vector3(0f, -probeArm, 0f), "Red");
            var tipCol = tip.AddComponent<SphereCollider>();
            tipCol.radius = probeTip; tipCol.sharedMaterial = pmFreeRollers;
            r.probe = probe.gameObject.AddComponent<PassiveJointProbe>();
            // Plano de la rampa: pasa por (0, 0.80, -1.05) con normal inclinada 7°
            r.probe.Configure("Palpador", true, tip.transform, probeTip, new Vector3(0f, SlopeBase, -1.05f), Quaternion.Euler(7f, 0f, 0f) * Vector3.up);
            // Pórtico del palpador
            Block(bt, "Portico_Izq", new Vector3(0.05f, 1.66f, 0.05f), new Vector3(-0.42f, 0.83f, probePivot.z), "Frame");
            Block(bt, "Portico_Der", new Vector3(0.05f, 1.66f, 0.05f), new Vector3(0.42f, 0.83f, probePivot.z), "Frame");
            Block(bt, "Portico_Viga", new Vector3(0.92f, 0.06f, 0.06f), new Vector3(0f, 1.66f, probePivot.z), "Yellow");

            // ---------- Compuerta (Revolute Z pasiva) en la salida Derecha · Nivel 1
            var flap = Body("Compuerta", bt, new Vector3(0.85f, 1.578f, 0f), ArticulationJointType.RevoluteJoint, AxisTo(Vector3.forward), 2f);
            Revolute(flap, -2f, 85f);
            flap.jointFriction = 0.05f;
            Part(flap.transform, "Compuerta_Eje", MeshFactory.Prism(8, 0.018f, 0.66f, 2), Vector3.zero, "Steel");
            var leaf = Block(flap.transform, "Compuerta_Hoja", new Vector3(0.02f, 0.40f, 0.56f), new Vector3(0f, -0.21f, 0f), "Hazard", null, pmSteel, 0.005f);
            r.flap = flap.gameObject.AddComponent<PassiveJointProbe>();
            r.flap.Configure("Compuerta", false);
            _ = leaf;

            // ---------- Carro (Prismatic Y) → Mesa (Revolute Y) → Empujador (Prismatic X) → Paleta (Fixed)
            var lift = Body("Carro", bt, new Vector3(0f, 0.70f, 0f), ArticulationJointType.PrismaticJoint, AxisTo(Vector3.up), 25f);
            Prismatic(lift, -0.03f, 0.97f); // 3 cm bajo "Carga": en reposo sostiene el drive, no el límite (balanza)
            var lt = lift.transform;
            Block(lt, "Carro_Placa", new Vector3(0.46f, 0.04f, 0.46f), new Vector3(0f, -0.02f, 0f), "Yellow");
            Part(lt, "Carro_Rodamiento", MeshFactory.Prism(16, 0.20f, 0.04f, 1), new Vector3(0f, 0.02f, 0f), "Steel");
            Block(lt, "Carro_Brazo", new Vector3(0.18f, 0.06f, 0.40f), new Vector3(0f, -0.05f, 0.36f), "Yellow");
            Block(lt, "Carro_Patin", new Vector3(0.26f, 0.26f, 0.10f), new Vector3(0f, -0.10f, 0.48f), "Yellow");
            Block(lt, "Carro_Faja", new Vector3(0.27f, 0.05f, 0.11f), new Vector3(0f, -0.02f, 0.48f), "Hazard");
            r.lift = lift.gameObject.AddComponent<JointDriveController>();
            r.lift.Configure("Carro elevador", "m", "W S", JointDriveController.DriveMode.Position, 0f, 0.97f, 0.35f, 0f, 150000f, 6000f, 3000f);

            var table = Body("Mesa", lt, new Vector3(0f, 0.74f, 0f), ArticulationJointType.RevoluteJoint, AxisTo(Vector3.up), 12f);
            Revolute(table, -5f, 185f);
            var tt = table.transform;
            Block(tt, "Mesa_Plataforma", new Vector3(0.70f, 0.06f, 0.70f), new Vector3(0f, 0.03f, 0f), "Deck", null, pmDeck, 0.01f);
            // Pared tope: la caja que entra empujada por el rodillo frena contra ella centrada en z = 0
            Block(tt, "Mesa_Pared", new Vector3(0.70f, 0.12f, 0.03f), new Vector3(0f, 0.12f, 0.215f), "Frame", null, pmSteel, 0.005f);
            Block(tt, "Mesa_Poste_Izq", new Vector3(0.03f, 0.22f, 0.03f), new Vector3(-0.335f, 0.17f, 0.335f), "Frame");
            Block(tt, "Mesa_Poste_Der", new Vector3(0.03f, 0.22f, 0.03f), new Vector3(0.335f, 0.17f, 0.335f), "Frame");
            Block(tt, "Mesa_Riel", new Vector3(0.70f, 0.02f, 0.05f), new Vector3(0f, 0.27f, 0.335f), "Steel", null, null, 0.004f);
            Block(tt, "Mesa_Flecha", new Vector3(0.16f, 0.004f, 0.05f), new Vector3(0.22f, 0.062f, -0.30f), "Line", null, null, 0.001f);
            r.table = table.gameObject.AddComponent<JointDriveController>();
            r.table.Configure("Mesa giratoria", "°", "A D", JointDriveController.DriveMode.Position, -5f, 185f, 45f, 0f, 300f, 60f, 150f);

            var pusher = Body("Empujador", tt, tt.TransformPoint(new Vector3(-0.31f, 0.29f, 0.335f)), ArticulationJointType.PrismaticJoint, AxisTo(Vector3.right), 3f);
            Prismatic(pusher, 0f, 0.75f);
            Block(pusher.transform, "Empujador_Patin", new Vector3(0.10f, 0.06f, 0.08f), Vector3.zero, "Orange");
            r.pusher = pusher.gameObject.AddComponent<JointDriveController>();
            r.pusher.Configure("Empujador", "m", "E Q", JointDriveController.DriveMode.Position, 0f, 0.75f, 0.45f, 0f, 20000f, 800f, 800f);

            var paddle = Body("Paleta", pusher.transform, tt.TransformPoint(new Vector3(-0.33f, 0.165f, -0.065f)), ArticulationJointType.FixedJoint, Quaternion.identity, 1.5f);
            var pt = paddle.transform;
            Block(pt, "Paleta_Placa", new Vector3(0.04f, 0.17f, 0.51f), Vector3.zero, "Hazard", null, pmSteel, 0.006f);
            Block(pt, "Paleta_Soporte", new Vector3(0.03f, 0.16f, 0.03f), new Vector3(0.02f, 0.10f, 0.30f), "Orange");
            Block(pt, "Paleta_Brazo", new Vector3(0.03f, 0.03f, 0.08f), new Vector3(0.02f, 0.025f, 0.27f), "Orange");

            // Todos los cuerpos conservan escala (1,1,1); la raíz gobierna las iteraciones del solver
            baseAb.solverIterations = 20;
            baseAb.solverVelocityIterations = 4;

            // Fantasmas de target
            foreach (var j in new[] { r.lift, r.table, r.pusher, r.gate })
                j.gameObject.AddComponent<TargetGhost>().Configure(ghostMat);

            return bt;
        }

        // ================================================================== estructura estática

        private struct StaticRefs { public Collider slopeCol, transferCol; }

        private static StaticRefs BuildStatics(Transform env, MechRefs refs)
        {
            var s = new StaticRefs();
            var root = new GameObject("Estructura").transform;
            root.SetParent(env, false);

            // ---------- Admisión: rampa a 7° (z −3.0 → −1.05) con rodillos libres
            var slopeRot = Quaternion.Euler(7f, 0f, 0f);
            Vector3 slopeTopMid = new(0f, SlopeBase + 1.95f * Tan7 * 0.5f, -2.025f);
            Vector3 slopeNormal = slopeRot * Vector3.up;
            float slopeLen = 1.95f / Mathf.Cos(7f * Mathf.Deg2Rad);
            var slope = Block(root, "Rampa_Admision", new Vector3(0.60f, 0.04f, slopeLen), slopeTopMid - slopeNormal * 0.02f, "Frame", slopeRot, pmFreeRollers);
            s.slopeCol = slope.GetComponent<Collider>();
            slope.GetComponent<MeshRenderer>().enabled = false; // la superficie se ve como rodillos
            for (float z = -2.95f; z < -1.10f; z += 0.12f)
            {
                float y = SlopeBase + (-1.05f - z) * Tan7 - 0.022f;
                Part(root, "RodilloLibre", MeshFactory.Prism(8, 0.022f, 0.58f, 0), new Vector3(0f, y, z), "Steel");
            }
            foreach (float x in new[] { -0.32f, 0.32f })
            {
                Block(root, "Rampa_Guia", new Vector3(0.03f, 0.10f, slopeLen), slopeTopMid + slopeNormal * 0.05f + Vector3.right * x, "Yellow", slopeRot, pmSteel, 0.005f);
                Block(root, "Rampa_Larguero", new Vector3(0.05f, 0.08f, slopeLen), slopeTopMid - slopeNormal * 0.06f + Vector3.right * x, "Frame", slopeRot);
                Block(root, "Transfer_Guia", new Vector3(0.03f, 0.10f, 0.54f), new Vector3(x, DeckTop + 0.06f, -0.78f), "Yellow", null, pmSteel, 0.005f);
                foreach (float z in new[] { -2.85f, -1.95f, -1.10f, -0.70f })
                {
                    float top = z > -1.05f ? DeckTop : SlopeBase + (-1.05f - z) * Tan7;
                    Block(root, "Pata", new Vector3(0.05f, top - 0.06f, 0.05f), new Vector3(x, (top - 0.06f) * 0.5f, z), "Frame");
                }
            }
            // Placa de transferencia: tramo inclinado 3° (z −1.13 → −0.80, se mete 8 cm bajo la rampa para que
            // no quede cara expuesta en el quiebre), rodillo motorizado en z −0.75 y tramo plano hasta z −0.51.
            float InclineY(float z) => DeckTop + (RollerZ - z) * Tan3;
            var transfer = Block(root, "Placa_Transferencia", new Vector3(0.60f, 0.04f, 0.3305f), new Vector3(0f, InclineY(-0.965f) - 0.02f, -0.965f), "Steel", Quaternion.Euler(3f, 0f, 0f), pmTransfer);
            s.transferCol = transfer.GetComponent<Collider>();
            transfer.GetComponent<MeshRenderer>().enabled = false; // se ve como rodillos libres
            var outPlate = Block(root, "Placa_Salida", new Vector3(0.60f, 0.04f, 0.19f), new Vector3(0f, DeckTop + OutPlateTop - 0.02f, -0.605f), "Steel", null, pmTransfer);
            outPlate.GetComponent<MeshRenderer>().enabled = false;
            for (float z = -1.02f; z < -0.80f; z += 0.08f)
                Part(root, "RodilloLibre", MeshFactory.Prism(8, 0.022f, 0.58f, 0), new Vector3(0f, InclineY(z) - 0.022f, z), "Steel");
            for (float z = -0.66f; z < -0.52f; z += 0.08f)
                Part(root, "RodilloLibre", MeshFactory.Prism(8, 0.022f, 0.58f, 0), new Vector3(0f, DeckTop + OutPlateTop - 0.022f, z), "Steel");
            Block(root, "Transfer_Bastidor", new Vector3(0.62f, 0.05f, 0.60f), new Vector3(0f, DeckTop - 0.07f, -0.80f), "Frame");

            // ---------- Salidas: rampas a ±X en Nivel 1 (10°) y Nivel 2 (5°)
            foreach (int side in new[] { 1, -1 })
            {
                string sideName = side > 0 ? "Der" : "Izq";
                BuildExit(root, side, 1, DeckTop + StationInput.Level1 - 0.02f, 10f, 1.20f, sideName);
                BuildExit(root, side, 2, DeckTop + StationInput.Level2 - 0.02f, 5f, 2.00f, sideName);
            }

            // Ménsulas de la compuerta colgadas de la rampa del Nivel 2
            foreach (float z in new[] { -0.33f, 0.33f })
                Block(root, "Compuerta_Mensula", new Vector3(0.04f, 0.12f, 0.03f), new Vector3(0.85f, 1.63f, z), "Frame");

            // Barandas de seguridad del perímetro (piso)
            foreach (var (pos, size) in new[]
            {
                (new Vector3(0f, 0.002f, -3.4f), new Vector3(3.0f, 0.004f, 0.08f)),
                (new Vector3(-1.5f, 0.002f, -2.0f), new Vector3(0.08f, 0.004f, 2.8f)),
                (new Vector3(1.5f, 0.002f, -2.0f), new Vector3(0.08f, 0.004f, 2.8f)),
            })
                Block(root, "Linea_Piso", size, pos, "Line", null, null, 0.001f);

            return s;
        }

        private static void BuildExit(Transform root, int side, int level, float topY, float angle, float length, string sideName)
        {
            float a = angle * Mathf.Deg2Rad;
            var rot = Quaternion.Euler(0f, 0f, -side * angle);
            Vector3 dir = new(side * Mathf.Cos(a), -Mathf.Sin(a), 0f);
            Vector3 normal = rot * Vector3.up;
            Vector3 start = new(side * 0.53f, topY, 0f); // la esquina de la mesa barre r = 0.495 m
            Vector3 mid = start + dir * (length * 0.5f);
            var exit = new GameObject($"Salida_{sideName}_N{level}").transform;
            exit.SetParent(root, false);

            var surf = Block(exit, "Superficie", new Vector3(length, 0.04f, 0.60f), mid - normal * 0.02f, "Frame", rot, pmFreeRollers);
            surf.GetComponent<MeshRenderer>().enabled = false;
            for (float d = 0.06f; d < length - 0.03f; d += 0.12f)
                Part(exit, "RodilloLibre", MeshFactory.Prism(8, 0.022f, 0.58f, 2), start + dir * d - normal * 0.022f, "Steel");
            foreach (float z in new[] { -0.32f, 0.32f })
            {
                Block(exit, "Guia", new Vector3(length, 0.10f, 0.03f), mid + normal * 0.05f + Vector3.forward * z, "Yellow", rot, pmSteel, 0.005f);
                Block(exit, "Larguero", new Vector3(length, 0.08f, 0.05f), mid - normal * 0.06f + Vector3.forward * z, "Frame", rot);
            }
            // Patas (fuera del ancho de la otra rampa)
            float legZ = level == 1 ? 0.36f : 0.48f;
            foreach (float d in new[] { 0.25f, length - 0.1f })
            {
                Vector3 p = start + dir * d;
                foreach (float z in new[] { -legZ, legZ })
                    Block(exit, "Pata", new Vector3(0.05f, p.y - 0.08f, 0.05f), new Vector3(p.x, (p.y - 0.08f) * 0.5f, z), "Frame");
            }

            // Contenedor de destino
            Vector3 end = start + dir * length;
            string mat = side < 0 ? "BinLeft" : "BinRight";
            float floorY = level == 1 ? 0f : 1.25f;
            float near = side * (Mathf.Abs(end.x) + 0.06f);
            float width = 0.70f;
            float cx = near + side * width * 0.5f;
            var bin = new GameObject($"Contenedor_{sideName}_N{level}").transform;
            bin.SetParent(exit, false);
            bin.position = new Vector3(cx, floorY, 0f);
            float hNear = level == 1 ? 0.45f : 0.15f, hFar = 0.45f;
            Block(bin, "Fondo", new Vector3(width, 0.05f, 0.86f), new Vector3(0f, 0.025f, 0f), mat, null, pmCardboard);
            Block(bin, "Pared_Cerca", new Vector3(0.04f, hNear, 0.86f), new Vector3(-side * width * 0.5f, hNear * 0.5f, 0f), mat, null, pmSteel);
            Block(bin, "Pared_Lejos", new Vector3(0.04f, hFar, 0.86f), new Vector3(side * width * 0.5f, hFar * 0.5f, 0f), mat, null, pmSteel);
            Block(bin, "Pared_A", new Vector3(width, hFar, 0.04f), new Vector3(0f, hFar * 0.5f, 0.43f), mat, null, pmSteel);
            Block(bin, "Pared_B", new Vector3(width, hFar, 0.04f), new Vector3(0f, hFar * 0.5f, -0.43f), mat, null, pmSteel);
            Block(bin, "Rotulo", new Vector3(0.30f, 0.06f, 0.012f), new Vector3(0f, hFar - 0.08f, -0.45f), level == 1 ? "Line" : "Yellow", null, null, 0.002f);
            if (level == 2)
            {
                // Estantería del nivel 2
                foreach (float x in new[] { -0.30f, 0.30f })
                foreach (float z in new[] { -0.38f, 0.38f })
                    Block(bin, "Estante_Pata", new Vector3(0.05f, floorY, 0.05f), new Vector3(x, -floorY * 0.5f, z), "Frame");
                Block(bin, "Estante_Viga", new Vector3(width + 0.06f, 0.06f, 0.90f), new Vector3(0f, -0.03f, 0f), "Frame");
            }
            var trig = new GameObject("Deteccion");
            trig.transform.SetParent(bin, false);
            trig.transform.localPosition = new Vector3(0f, 0.30f, 0f);
            var tc = trig.AddComponent<BoxCollider>();
            tc.isTrigger = true;
            tc.size = new Vector3(width - 0.1f, 0.45f, 0.78f);
            trig.AddComponent<SortingBin>().Configure(side < 0 ? BinSide.Left : BinSide.Right, level, null);
        }

        // ================================================================== cargas

        private static CargoSpawner BuildCargo(Transform parent, TaskTracker tracker)
        {
            var spawnerGo = new GameObject("Cajas");
            spawnerGo.transform.SetParent(parent, false);
            var spawner = spawnerGo.AddComponent<CargoSpawner>();

            var point = new GameObject("PuntoDeAparicion").transform;
            point.SetParent(spawnerGo.transform, false);
            point.SetPositionAndRotation(new Vector3(0f, SlopeBase + (2.7f - 1.05f) * Tan7, -2.7f), Quaternion.Euler(7f, 0f, 0f));

            CargoBox Template(string name, float h, string mat)
            {
                var go = new GameObject(name);
                go.transform.SetParent(spawnerGo.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Box(new Vector3(0.40f, h, 0.40f), 0.012f);
                go.AddComponent<MeshRenderer>().sharedMaterial = Mats[mat];
                var c = go.AddComponent<BoxCollider>();
                c.size = new Vector3(0.40f, h, 0.40f);
                c.sharedMaterial = pmCardboard;
                var rb = go.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.1f;
                return go.AddComponent<CargoBox>();
            }

            var low = Template("Plantilla_Baja", 0.20f, "CardboardLow");
            var tall = Template("Plantilla_Alta", 0.40f, "CardboardTall");
            spawner.Configure(low, tall, point, tracker);

            foreach (var bin in Object.FindObjectsByType<SortingBin>(FindObjectsSortMode.None))
                bin.Configure(bin.Side, bin.Level, tracker);
            return spawner;
        }

        // ================================================================== luz, piso, cámara, HUD

        private static GameObject BuildLighting(Transform env)
        {
            var sun = new GameObject("Sol");
            sun.transform.SetParent(env, false);
            sun.transform.rotation = Quaternion.Euler(50f, -40f, 0f);
            var l = sun.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(1f, 0.95f, 0.88f);
            l.intensity = 1.15f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.75f;

            var look = sun.AddComponent<StationLook>();

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.52f);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.12f, 0.14f, 0.17f);
            RenderSettings.fogStartDistance = 10f;
            RenderSettings.fogEndDistance = 32f;

            // Post-procesado liviano (apto WebGL)
            var volGo = new GameObject("PostProceso");
            volGo.transform.SetParent(env, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            string profilePath = Root + "/Settings/StationVolume.asset";
            AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            var tm = profile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.35f); bloom.threshold.Override(1.0f); bloom.scatter.Override(0.6f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.22f); vig.smoothness.Override(0.5f);
            var ca = profile.Add<ColorAdjustments>(true); ca.contrast.Override(10f); ca.saturation.Override(6f);
            foreach (var comp in profile.components) AssetDatabase.AddObjectToAsset(comp, profile);
            vol.sharedProfile = profile;
            return look.gameObject;
        }

        private static void BuildFloor(Transform env)
        {
            var floor = Block(env, "Piso", new Vector3(30f, 0.2f, 30f), new Vector3(0f, -0.1f, 0f), "Floor", null, pmSteel, 0.02f);
            floor.isStatic = true;
        }

        private static OrbitCamera BuildCamera()
        {
            var go = new GameObject("Camara");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            go.AddComponent<AudioListener>();
            return go.AddComponent<OrbitCamera>();
        }

        private static void BuildHud(StationController station, StationInput input, CargoSpawner spawner, TaskTracker tracker, OrbitCamera cam)
        {
            string themePath = Root + "/UI/StationTheme.tss";
            if (!System.IO.File.Exists(themePath))
            {
                System.IO.File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(themePath);
            }
            string panelPath = Root + "/UI/StationPanel.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, panelPath);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);

            var go = new GameObject("HUD");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            var hud = go.AddComponent<StationHud>();
            hud.Configure(station, input, spawner, tracker, cam, AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "/UI/StationHud.uss"));
        }
    }
}

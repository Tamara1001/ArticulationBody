# Estructura del proyecto

```
Assets/
  _Project/
    Scenes/
      Station.unity              Escena principal (la única de la build)
    Scripts/
      Mechanism/                 Control y lectura de la articulación
        JointDriveController.cs  Un drive controlado: target con rampa, lectura de jointPosition
        PassiveJointProbe.cs     Joint pasivo: ángulo, altura del palpador
        LoadCell.cs              Balanza derivada de la fuerza del drive del carro
        StationController.cs     Agrega los joints, enclavamientos, prueba en seco, colisiones ignoradas
      Cargo/
        CargoBox.cs              Datos de la caja (tipo, alto, masa)
        CargoSpawner.cs          Pool y aparición de cajas
        SortingBin.cs            Contenedor destino (trigger)
        TaskTracker.cs           Puntaje, errores, caídas, tiempos
      Input/
        StationInput.cs          Atajos de teclado (Input System) → comandos
      UI/
        StationHud.cs            HUD en UI Toolkit (mando, sensores, tarea, ayuda)
      CameraRig/
        OrbitCamera.cs           Cámara orbital con presets
      Visual/
        TargetGhost.cs           "Fantasma" que muestra el target de cada joint
        StationLook.cs           Globals de shader (ambiente, outline)
      Editor/
        StationSceneBuilder.cs   Construye la escena entera desde código (menú Estación)
        MeshFactory.cs           Mallas low-poly (cajas biseladas, prismas) con normales para outline
        WebGLBuilder.cs          Build WebGL para itch.io (menú Estación ▸ Build WebGL → Builds/WebGL)
    Shaders/
      IndustrialToon.shader      Toon/flat + líneas de panel + rayas de peligro + outline (casco invertido)
      TargetGhost.shader         Transparente con fresnel para el fantasma
    Materials/                   Generados por el builder
    Meshes/                      Mallas generadas (.asset)
    Physics/                     PhysicsMaterials
    UI/
      StationHud.uxml / .uss     Layout y estilo del HUD
      PanelSettings.asset, StationTheme.tss
    Settings/                    Volume profile de la estación
Docs/                            Documentación (este archivo, diseño, estilo, roadmap)
```

## Decisiones

- **La escena se genera por código** (`Estación ▸ Construir escena`). La geometría, las anclas, los límites y los drives quedan en un solo lugar versionado y coinciden con `DESIGN_AND_JOINTS.md`. La escena guardada también se versiona, así que el repo abre y ejecuta sin pasos manuales.
- **Mallas generadas, no importadas.** Así los pivotes coinciden con las anclas y no aparecen escalas ocultas. Blender queda para piezas puntuales si hiciera falta.
- **Sin prefabs para el mecanismo.** Es una sola instancia y el builder la crea. Las cajas sí usan un pool en tiempo de ejecución.
- **Una sola escena** en la build WebGL.

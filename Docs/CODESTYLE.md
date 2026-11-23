# Convenciones de código

## Idioma
- **Identificadores en inglés** (clases, métodos, campos).
- **Comentarios en español.**
- **Textos de la UI en español.**

## C#
- `PascalCase` para tipos, métodos y propiedades; `camelCase` para campos privados serializados (`[SerializeField] private float rampSpeed;`). Constantes en `PascalCase`.
- Un `MonoBehaviour` por archivo; el archivo lleva el nombre de la clase.
- Campos públicos solo como propiedades de lectura (`public float Target => target;`). La configuración va con `[SerializeField] private`.
- Sin `FindObjectOfType` en caliente: las referencias se cablean en el builder o en `Awake`.
- Nada de lógica en `Update` que toque física.

## Física (reglas de la cátedra y propias)
1. **Drives solo en `FixedUpdate`.** La entrada (teclado o UI) escribe un *comando*; `FixedUpdate` mueve el target hacia el comando con una rampa (`Mathf.MoveTowards`) y asigna el drive.
2. **La lectura de `jointPosition` y `jointVelocity` también se hace en `FixedUpdate`** y se cachea. La UI lee el caché a ~15 Hz.
3. **Escala (1, 1, 1)** en todo `ArticulationBody`. Lo visual va en hijos sin física.
4. **Eje del joint = eje X del ancla.** Se define con `anchorRotation` y `matchAnchors = true`.
5. Unidades: los drives angulares de Unity usan **grados** en target y velocidad, pero `jointPosition` viene en **radianes**. La conversión se hace en un solo lugar (`JointDriveController`).
6. Partes pasivas: `stiffness = 0`, `damping = 0` y sin target; se permite `jointFriction`.
7. Colisiones: solo tienen collider las piezas que tocan cajas. Los pares que se solapan por diseño (perno del tope ↔ rampa) se ignoran explícitamente.

## Arquitectura
- `StationController` es el punto de acceso único: conoce los joints, aplica enclavamientos y expone `SetCommand`, `DryRun` y `Reset`. La entrada y la UI dependen de él; el mecanismo no conoce a la UI.
- Los sensores (`PassiveJointProbe`, `LoadCell`) solo leen.
- Los eventos de tarea (`TaskTracker.OnEvent`) se usan para los avisos de la UI.

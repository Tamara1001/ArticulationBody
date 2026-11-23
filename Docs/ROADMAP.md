# Roadmap

| Fase | Contenido | Criterio de salida |
|---|---|---|
| 0. Base | Repo público, `.gitignore`, documentación, ajustes de física (TGS, 100 Hz) y de WebGL | Commit inicial con la documentación |
| 1. Mecanismo en seco | Builder: jerarquía, anclas, límites y masas **sin drives** | En Play todo cae por gravedad y se detiene en los límites sin vibrar |
| 2. Drives | Stiffness, damping y forceLimit de la raíz a la punta; `JointDriveController` con rampas | Cada joint llega a su target sin oscilar, con y sin la caja de 20 kg |
| 3. **Build WebGL temprana** | Compilar y probar en el navegador | Mismo comportamiento que en el Editor |
| 4. Tarea | Rampa, tope, palpador, rodillo, salidas, contenedores, cajas y puntaje | La maniobra completa sale de forma repetible con los 4 tipos de caja |
| 5. UI/UX | HUD (Target y Real por joint), presets, atajos, ayuda, avisos, fantasma de target | Se opera sin leer el código |
| 6. Look | Shader industrial toon + outline, iluminación y post | Se ve bien y corre fluido en WebGL |
| 7. Entrega | Build final en itch.io, video y documento (diagrama + explicación) | Checklist de entrega completa |

## Pruebas de cada fase
- **Prueba en seco:** el botón "Prueba en seco" del HUD anula todos los drives en vivo. Sirve para mostrar en el video que los límites se respetan.
- **WebGL:** probar en una ventana privada después de subir a itch.io.

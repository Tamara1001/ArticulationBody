# Estación de Transferencia y Clasificación (ArticulationBody)

Simulador en Unity de una **estación de clasificación de paquetería** resuelta con `ArticulationBody`: 9 cuerpos, 4 tipos de joint (Fixed, Prismatic, Revolute, Spherical), 5 drives controlados (tope, rodillo, carro elevador, mesa giratoria, empujador) y 2 partes pasivas (palpador esférico que mide la altura y compuerta con bisagra). Trabajo para la materia Simuladores.

Llegan cajas de cartón (`Rigidbody`) que por fuera se ven iguales pero tienen distinta altura (0.20 / 0.40 m) y masa (4 a 20 kg). El operador mide la altura con el palpador, la masa con la balanza del elevador, y lleva cada caja a uno de 4 destinos (2 lados × 2 niveles). El fantasma cian muestra el *target* de cada joint; la pieza real lo sigue.

## Enlaces

- Build jugable (itch.io): **[COMPLETAR: link de itch.io]** <!-- LINK itch.io -->
- Video (YouTube): **[COMPLETAR: link de YouTube]** <!-- LINK YouTube -->

## Controles

| Tecla | Acción |
|---|---|
| W / S | Elevar / bajar carro |
| 1 · 2 · 3 | Carga · Nivel 1 · Nivel 2 |
| A / D | Girar mesa |
| ← / → | Mesa a Izquierda / Derecha |
| E / Q | Empujar / retraer |
| Espacio | Tope: liberar / retener |
| R / F | Rodillo: marcha / reversa |
| N | Nueva caja |
| Clic der./izq. + arrastrar | Orbitar cámara |
| Rueda · botón medio | Zoom · desplazar |
| C | Cambiar vista |
| T | Prueba en seco (sin drives) |
| Retroceso | Reiniciar |
| H | Mostrar / ocultar ayuda |

**Maniobra:** N (la caja baja y el palpador mide su altura en el tope) → Espacio (libera) → Espacio (retiene) → R con carro en Carga y mesa a 0° (la caja pasa a la mesa) → R (detiene) → leer la balanza → elevar y girar según la regla → E para empujar → Q para retraer → volver a Carga. Enclavamiento: el carro y la mesa solo se mueven con el empujador retraído.

**Regla:** altura < 0.30 m → Nivel 1, si no Nivel 2. Masa < 10 kg → Izquierda, si no Derecha.

## Abrir el proyecto

1. Instalar **Unity 6.3 LTS**.
2. Abrir esta carpeta como proyecto desde Unity Hub.
3. Abrir `Assets/_Project/Scenes/Station.unity` y dar **Play**.

## Regenerar escena y build

- Escena: menú **Estación ▸ Construir escena** (regenera `Station.unity` desde código, con `StationSceneBuilder`).
- Build: menú **Estación ▸ Build WebGL** (genera la build para itch.io).

## Documentación

- [Docs/ENTREGA.pdf](Docs/ENTREGA.pdf): documento de entrega con el formato de la cátedra (editable en [DOCX](Docs/ENTREGA.docx); versión en texto: [ENTREGA.md](Docs/ENTREGA.md)).
- [Docs/DESIGN_AND_JOINTS.md](Docs/DESIGN_AND_JOINTS.md): diseño del mecanismo, tabla de joints y fuente de los valores.
- [Docs/arbol_articulacion.svg](Docs/arbol_articulacion.svg): diagrama del árbol.
- [Docs/STRUCTURE.md](Docs/STRUCTURE.md): estructura del proyecto.
- [Docs/CODESTYLE.md](Docs/CODESTYLE.md): estilo de código.
- [Docs/ROADMAP.md](Docs/ROADMAP.md): hoja de ruta.

# Página de itch.io

URL: https://tamara1001.itch.io/clasificacioncajas · canal subido con butler: `tamara1001/clasificacioncajas:html5` (versión 1.0.0).

Para volver a subir una build nueva (Estación ▸ Build WebGL, salida `Builds/WebGL`):

```powershell
D:\Programas\butler\butler.exe push Builds/WebGL tamara1001/clasificacioncajas:html5 --userversion 1.0.1
```

## Ajustes del panel (Edit game) — hay que hacerlos en la web

| Campo | Valor |
|---|---|
| Title | Estación de Clasificación |
| Short description / tagline | Simulador de una estación de clasificación de cajas con Unity ArticulationBody |
| Kind of project | HTML (el archivo del canal `html5` se marca como «This file will be played in the browser» solo) |
| Embed options | Manually set size: 1280 × 720 (o 1600 × 900, que es el tamaño por defecto de la build) · activar «Fullscreen button» |
| Mobile friendly | No (requiere teclado y mouse) |
| SharedArrayBuffer / Frame options | Dejar desactivados |
| Genre | Simulation |
| Tags | simulation, physics, unity, industrial, sandbox, educational, 3d, spanish |
| Community | Comments activados |
| Visibility | Publicar cuando la descripción y las imágenes estén cargadas |

## Imágenes (carpeta `Video/itch/`, ignorada por Git)

- Portada: `portada_630x500.png` (630 × 500, mínimo recomendado 315 × 250).
- Capturas: `captura_12.png`, `captura_38.png`, `captura_62.png`, `captura_88.png` (1920 × 1080).
- Video: subir `Video/demo.mp4` a YouTube (no listado o público) y pegar el enlace en «Add a video»; itch.io no aloja mp4 propios en la galería.

## Descripción (pegar en «Description»)

**Estación de Clasificación** es un simulador de una estación industrial de transferencia y clasificación de cajas, hecho en Unity con **ArticulationBody**. Corre en el navegador.

Llegan cajas de cartón que por fuera se ven iguales. Hay que medir su **altura** con el palpador y su **masa** con la balanza del elevador, y llevar cada una a su destino: dos lados y dos niveles.

### Regla de clasificación

- Altura menor a 30 cm → **Nivel 1**; 30 cm o más → **Nivel 2**.
- Masa menor a 10 kg → **Izquierda (180°)**; 10 kg o más → **Derecha (0°)**.

### Cómo se juega

1. **N** (o «Nueva caja»): una caja baja por la rampa y levanta el palpador, que lee su altura.
2. **Espacio**: libera el tope y la caja baja al rodillo. **Espacio** otra vez: retiene.
3. **R**: el rodillo pasa la caja a la mesa (con el carro en Carga y la mesa a 0°). **R** de nuevo: detiene.
4. Leé la balanza, elevá el carro (**W/S**, **1/2/3**) y girá la mesa (**A/D**, **←/→**) según la regla.
5. **E** empuja la caja hacia su contenedor y **Q** retrae el empujador. Después volvé a Carga.

El carro y la mesa solo se mueven con el empujador retraído (enclavamiento). Con **T** se activa la **prueba en seco**: se anulan los drives y el mecanismo cae por gravedad hasta sus límites.

### Controles

| Tecla / mouse | Acción |
|---|---|
| W / S | Elevar / bajar carro |
| 1 · 2 · 3 | Carga · Nivel 1 · Nivel 2 |
| A / D · ← / → | Girar mesa · mesa a Izquierda / Derecha |
| E / Q | Empujar / retraer |
| Espacio | Tope: liberar / retener |
| R / F | Rodillo: marcha / reversa |
| N | Nueva caja |
| T | Prueba en seco |
| C | Cambiar vista de cámara |
| Backspace | Reiniciar |
| H | Mostrar / ocultar ayuda |
| Clic izquierdo o derecho + arrastrar | Orbitar la cámara |
| Rueda · botón medio | Zoom · desplazar |

También se puede manejar todo con los botones y sliders del panel de la izquierda. El fantasma celeste muestra el **target** de cada joint y la pieza real lo sigue.

### Detalles técnicos

- 9 cuerpos articulados, 4 tipos de joint (prismático, revoluto, esférico y fijo), 5 drives y 2 joints pasivos (palpador y compuerta).
- Física a 100 Hz con solver TGS.
- URP con shader HLSL propio (toon con contorno) y HUD en UI Toolkit.
- Hecho en Unity 6 (6000.3). Trabajo práctico universitario.

Recomendado: Chrome o Edge en una computadora, con teclado y mouse. La primera carga tarda unos segundos.

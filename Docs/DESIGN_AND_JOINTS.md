# Diseño del mecanismo y árbol de articulaciones

**Máquina simulada:** estación de transferencia y clasificación de paquetería (centro de distribución).
**Operación:** el operador recibe cajas de cartón de aspecto idéntico pero con distinta **altura** y **masa**. Mide la altura con un palpador mecánico pasivo y la masa con una balanza integrada en el elevador. Después eleva, gira y empuja cada caja hacia uno de **4 destinos** (2 lados × 2 niveles). La maniobra es repetible y se puntúa.

Unidades: metros, kilogramos, grados. Ejes en coordenadas de mundo con el mecanismo en reposo (Unity: +X derecha, +Y arriba, +Z adelante, que es el sentido de avance de las cajas). El origen es el eje de giro de la mesa, a nivel del piso.

---

## 1. Árbol de la articulación (1 árbol, 9 ArticulationBody)

```
Base  [raíz · Immovable]
├── Palpador ············ Spherical           PASIVO (sin stiffness)
├── Tope ················ Prismatic  (Y)      drive de posición
├── Rodillo ············· Revolute   (X)      drive de velocidad
├── Compuerta ··········· Revolute   (Z)      PASIVO (sin stiffness)
└── Carro ··············· Prismatic  (Y)      drive de posición  → balanza
    └── Mesa ············ Revolute   (Y)      drive de posición
        └── Empujador ··· Prismatic  (X local) drive de posición
            └── Paleta ·· Fixed
```

| Requisito de la cátedra | Mínimo | Este diseño |
|---|---|---|
| Árboles con raíz Immovable | 1 | 1 (`Base`) |
| ArticulationBodies | 6–10 | **9** |
| Tipos de joint distintos | 3 | **4** (Fixed, Prismatic, Revolute, Spherical) |
| Joints con drive controlado | 4 | **5** (Tope, Rodillo, Carro, Mesa, Empujador) |
| Partes pasivas | 1 | **2** (Palpador, Compuerta) |
| Interacción con Rigidbodies | sí | cajas de cartón (`Rigidbody`) |

## 2. Tabla de joints

| Cuerpo | Padre | Tipo | Eje | Límites | Masa | Drive (stiffness · damping · forceLimit) |
|---|---|---|---|---|---|---|
| Base | — | raíz Immovable | — | — | 500 kg | — |
| Palpador | Base | Spherical | swing en torno a X (±85°) y a Z (±10°); twist bloqueado | ver eje | 0.4 kg | **ninguno** (solo `jointFriction` 0.05) |
| Tope | Base | Prismatic | +Y | 0 … 0.13 m | 1.5 kg | 5 000 N/m · 150 N·s/m · 300 N |
| Rodillo | Base | Revolute (libre) | +X | sin límite | 4 kg | velocidad: 0 · 20 · 30 N·m |
| Compuerta | Base | Revolute | +Z | −2° … 85° | 2 kg | **ninguno** (solo `jointFriction` 0.05) |
| Carro | Base | Prismatic | +Y | −0.03 … 0.97 m | 25 kg | 150 000 N/m · 6 000 N·s/m · 3 000 N |
| Mesa | Carro | Revolute | +Y | −5° … 185° | 12 kg | 300 · 60 · 150 N·m |
| Empujador | Mesa | Prismatic | +X local de la mesa | 0 … 0.75 m | 3 kg | 20 000 N/m · 800 N·s/m · 800 N |
| Paleta | Empujador | Fixed | — | — | 1.5 kg | — |

Las anclas (`anchorPosition`) están en el origen de cada cuerpo, que es el pivote real de la unión. El eje de movimiento se define siempre con el **eje X del ancla** (`anchorRotation`) y con `matchAnchors = true`. Ningún `ArticulationBody` tiene escala distinta de (1, 1, 1): las mallas visuales son hijos sin física. El límite inferior del carro está 3 cm por debajo de "Carga", así en reposo **lo sostiene el drive y no el límite**, y la balanza siempre puede leer.

### Posiciones de reposo (mundo)

| Cuerpo | Posición | Nota |
|---|---|---|
| Palpador | pivote (0, 1.60, −1.95) | brazo de 0.60 m + bola de r 0.04 m, sobre la rampa |
| Tope | (0, 0.737, −1.30) | perno r 0.03 m; con q = 0.13 sobresale 12 cm de la rampa |
| Rodillo | (0, 0.762, −0.75) | r 0.05 m; cresta 1.2 cm sobre la placa inclinada |
| Compuerta | bisagra (0.85, 1.578, 0) | aleta de 0.40 m en la salida Derecha · Nivel 1 |
| Carro | (0, 0.70, 0) | — |
| Mesa | (0, 0.74, 0) | plataforma de 0.70 × 0.70 m; cara superior a 0.80 m con el carro en Carga |
| Empujador | local de la mesa (−0.31, 0.29, 0.335) | patín sobre el riel trasero |
| Paleta | local de la mesa (−0.33, 0.165, −0.065) | 0.04 × 0.17 × 0.51 m |

### Posiciones de trabajo (presets)

| Joint | Presets |
|---|---|
| Carro | Carga 0.00 m · Nivel 1 0.40 m · Nivel 2 0.95 m |
| Mesa | Derecha 0° · Izquierda 180° |
| Empujador | Retraído 0.00 m · Empuje 0.75 m |
| Tope | Arriba (retiene) 0.13 m · Abajo (libera) 0.00 m |
| Rodillo | Paro 0 · Marcha 1.8 m/s · Reversa −0.5 m/s (velocidad tangencial) |

## 3. Cómo se eligieron los valores de los drives

Criterio general: la **constante elástica** se elige según el error estático admisible bajo la peor carga, el **damping** cerca del crítico (ζ ≈ 1, `d ≈ 2·√(k·m)`) y el **forceLimit** con un margen de alrededor de 3× sobre la fuerza estática máxima. El ajuste se hizo de la raíz a la punta. Los targets nunca saltan: cada drive tiene una **rampa de velocidad** (rate limit) aplicada en `FixedUpdate`.

- **Carro:** masa suspendida = carro 25 + mesa 12 + empujador 3 + paleta 1.5 + caja máx. 20 = 61.5 kg, peso ≈ 603 N. Con k = 150 000 N/m el hundimiento estático es de unos 4 mm. Damping crítico = 2·√(150 000·61.5) ≈ 6 075 → 6 000. forceLimit 3 000 N (≈ 5× el peso). Rampa de 0.35 m/s.
- **Mesa:** inercia en Y ≈ 1 (plataforma) + 0.5 (pared y empujador) + 0.8 (caja de 20 kg) ≈ 2.3 kg·m². Con k = 300 N·m/rad, ω ≈ 11 rad/s; d = 2·√(300·2.3) ≈ 52 → 60. La rampa de 45 °/s mantiene la aceleración tangencial muy por debajo de μ·g, para que la caja no deslice.
- **Empujador:** debe vencer la fricción de la caja más pesada, 0.10·20·9.81 ≈ 20 N, más la inercia. k = 20 000 N/m, d = 800, forceLimit 800 N. Rampa de 0.45 m/s.
- **Tope:** resiste la componente de la caja sobre la rampa de 7° (≈ 24 N, lateral). Rigidez moderada; rampa de 0.4 m/s.
- **Rodillo:** drive de velocidad pura (stiffness 0). Con damping 20 y la marcha en 0 funciona además como **freno**: la caja que baja queda detenida encima. La tracción alcanza ≈ 0.9·N, sobrada para acelerar la caja a 1.8 m/s en los 0.4 m de contacto.

## 4. Partes pasivas

- **Palpador (Spherical, sin stiffness):** es un péndulo colgado sobre la rampa, antes del tope. La caja que baja por gravedad lo levanta y queda detenida en el tope con la bola apoyada encima, así que la lectura es estable. La altura se mide de forma exacta con la posición de la bola respecto del plano de la rampa:
  `altura = (p_bola − p_rampa) · n_rampa − r_bola`.
  Ángulos medidos: caja baja (0.20 m) ≈ 35°; caja alta (0.40 m) ≈ 59°.
- **Compuerta (Revolute, sin stiffness):** cortina batiente en la salida Derecha · Nivel 1. La caja la abre al pasar (≈ 45°) y la gravedad la cierra.

Ninguna de las dos tiene stiffness ni target. Solo una `jointFriction` pequeña para amortiguar la oscilación.

## 5. Balanza (sensor derivado del drive del carro)

`ArticulationBody.driveForce` informa la fuerza que el drive del carro aplicó en el último paso. Con el carro quieto, esa fuerza iguala el peso de todo lo que cuelga de él. La tara es la suma de masas del carro y sus hijos (41.5 kg):
`masa = F̄ / g − 41.5 kg` (F̄ filtrada con un pasa-bajos). La lectura se marca "estable" cuando la posición real del carro no cambia.
En las pruebas leyó 4.0 / 6.0 / 18.0 / 20.0 kg para las cuatro cajas.

## 6. Cargas (Rigidbody comunes)

Las cuatro cajas tienen planta de 0.40 × 0.40 m y el mismo aspecto exterior. **Solo los sensores distinguen el tipo.**

| Tipo | Alto | Masa | Destino |
|---|---|---|---|
| Baja liviana | 0.20 m | 4 kg | Izquierda · Nivel 1 |
| Alta liviana | 0.40 m | 6 kg | Izquierda · Nivel 2 |
| Baja pesada | 0.20 m | 18 kg | Derecha · Nivel 1 (atraviesa la compuerta) |
| Alta pesada | 0.40 m | 20 kg | Derecha · Nivel 2 |

**Regla de clasificación:** altura < 0.30 m → Nivel 1; altura ≥ 0.30 m → Nivel 2. Masa < 10 kg → Izquierda; masa ≥ 10 kg → Derecha.

Materiales físicos: cartón μ 0.4; rodillos libres (rampa y placas) μ 0.03; goma del rodillo μ 0.9 (combinación *Maximum*); plataforma de la mesa μ 0.10.

## 7. Maniobra completa

1. **Nueva caja** (N): aparece en la parte alta de la rampa, baja, levanta el **palpador** y se detiene en el **tope**. Se lee la altura.
2. **Bajar el tope** (Espacio): la caja baja por la placa inclinada y queda frenada sobre el **rodillo** detenido. **Subir el tope** (Espacio).
3. Con el **carro en Carga y la mesa a 0°**, poner el **rodillo en marcha** (R). La caja pasa a la mesa y frena contra su pared trasera, centrada. Detener el rodillo (R).
4. Leer la **balanza**: masa.
5. **Elevar** al nivel (W/S o 1·2·3) y **girar** al lado (A/D o ←/→).
6. **Empujar** (E): la caja cae por la rampa de salida a su contenedor. **Retraer** (Q).
7. Volver a Carga y a 0°.

Enclavamientos (como en una máquina real): la **mesa y el carro solo se mueven con el empujador retraído**, y entra **una caja por vez** a la admisión.

## 8. Geometría estática y lecciones de las pruebas

- **Rampa de admisión:** de z = −3.0 a −1.05, con 7° de inclinación y rodillos libres.
- **Placa inclinada a 3°:** de −1.05 a −0.80. Se prolonga 8 cm por debajo de la rampa para no dejar una cara expuesta en el quiebre de pendiente.
- **Rodillo motorizado** en −0.75 y **placa de salida plana** hasta −0.51.
- **Alturas escalonadas que siempre bajan en el sentido de avance:** cresta del rodillo 0.812 > placa de salida 0.803 > plataforma de la mesa 0.800. Con un escalón hacia arriba, PhysX traba la arista inferior de la caja contra la cara del siguiente collider.
- **Salidas:** rampas a ±X que empiezan en |x| = 0.53 m. La esquina de la plataforma barre un radio de 0.495 m; con 0.50 la rampa del Nivel 1 trababa la mesa a 40°.
  - Nivel 1: 10° hacia contenedores de piso.
  - Nivel 2: 5° hacia contenedores en estantería a 1.25 m.
- **Pared trasera de la mesa** en z local = 0.215, de modo que la caja queda centrada en z = 0. Las guías de salida aceptan ±0.1 m.

**Prueba automatizada:** las 4 clases de caja se llevaron a su destino 6 de 6 veces seguidas, con 0 errores y 0 caídas.

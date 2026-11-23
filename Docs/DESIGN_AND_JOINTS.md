# Diseño del mecanismo y árbol de articulaciones

**Máquina simulada:** estación de transferencia y clasificación de paquetería (centro de distribución).
**Operación:** el operador recibe cajas de cartón de aspecto similar pero con distinta **altura** y **masa**. Mide la altura con un palpador mecánico pasivo y la masa con una balanza integrada en el elevador. Después eleva, gira y empuja cada caja hacia uno de **4 destinos** (2 lados × 2 niveles). La maniobra es repetible y se puntúa.

Unidades: metros, kilogramos, grados. Ejes en coordenadas de mundo con el mecanismo en reposo (Unity: +X derecha, +Y arriba, +Z adelante). El origen es el eje de giro de la mesa, a nivel del piso.

---

## 1. Árbol de la articulación (1 árbol, 9 ArticulationBody)

```
Base  [raíz · Immovable]
├── Tope ················ Prismatic  (Y)      drive de posición
├── Rodillo ············· Revolute   (X)      drive de velocidad
├── Palpador ············ Spherical           PASIVO (sin stiffness)
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

| Cuerpo | Padre | Tipo | Eje | Límites | Masa | Drive (stiffness / damping / forceLimit) |
|---|---|---|---|---|---|---|
| Base | — | raíz Immovable | — | — | 500 kg (irrelevante) | — |
| Tope | Base | Prismatic | +Y | 0 … 0.13 m | 1.5 kg | 5 000 N/m · 150 N·s/m · 300 N |
| Rodillo | Base | Revolute (libre) | +X | sin límite | 4 kg | velocidad: 0 · 5 N·m·s/rad · 20 N·m |
| Palpador | Base | Spherical | swing en X ±85°, swing en Z ±10°, twist bloqueado | ver eje | 0.8 kg | **ninguno** (solo `jointFriction`) |
| Compuerta | Base | Revolute | +Z | −2° … 85° | 2 kg | **ninguno** (solo `jointFriction`) |
| Carro | Base | Prismatic | +Y | 0 … 0.97 m | 25 kg | 150 000 N/m · 6 000 N·s/m · 3 000 N |
| Mesa | Carro | Revolute | +Y | −5° … 185° | 12 kg | 300 N·m/rad · 60 N·m·s/rad · 150 N·m |
| Empujador | Mesa | Prismatic | +X local de la mesa | 0 … 0.75 m | 3 kg | 20 000 N/m · 800 N·s/m · 800 N |
| Paleta | Empujador | Fixed | — | — | 1.5 kg | — |

Las anclas (`anchorPosition`) están en el origen de cada cuerpo, que es el pivote real de la unión. El eje de movimiento se define siempre con el **eje X del ancla** (`anchorRotation`), con `matchAnchors = true`. Ningún `ArticulationBody` tiene escala distinta de (1, 1, 1): las mallas visuales son hijos sin física.

### Posiciones de reposo (mundo)

| Cuerpo | Posición | Nota |
|---|---|---|
| Tope | (0, 0.721, −1.30) | perno bajo la rampa; con q = 0.13 sobresale 12 cm |
| Rodillo | (0, 0.753, −0.55) | radio 0.05 m; su cara superior queda 3 mm sobre la placa de transferencia |
| Palpador | pivote (0, 1.35, −0.82) | brazo de 0.45 m + bola de r 0.03 m |
| Compuerta | bisagra (0.85, 1.578, 0) | aleta de 0.42 m en la salida derecha del nivel 1 |
| Carro | (0, 0.70, 0) | — |
| Mesa | (0, 0.74, 0) | plataforma de 0.70 × 0.70 m; cara superior a 0.80 m con q_carro = 0 |
| Empujador | local de la mesa (−0.31, 0.29, 0.335) | patín sobre el riel de la pared trasera |
| Paleta | local de la mesa (−0.33, 0.165, −0.01) | 0.04 × 0.17 × 0.62 m |

### Posiciones de trabajo (presets)

| Joint | Presets |
|---|---|
| Carro | Carga 0.00 m · Nivel 1 0.40 m · Nivel 2 0.95 m |
| Mesa | Derecha 0° · Izquierda 180° |
| Empujador | Retraído 0.00 m · Empuje 0.75 m |
| Tope | Arriba (retiene) 0.13 m · Abajo (libera) 0.00 m |
| Rodillo | Paro 0 m/s · Marcha 1.0 m/s · Reversa −0.5 m/s (velocidad tangencial) |

## 3. Cómo se eligieron los valores de los drives

Criterio general: **constante elástica** según el error estático admisible bajo la peor carga, **damping cercano al crítico** (ζ ≈ 1, `d ≈ 2·√(k·m)`) y **forceLimit** con margen de alrededor de 3× sobre la fuerza estática máxima. El ajuste se hizo de la raíz a la punta. Los targets nunca saltan: cada drive tiene una **rampa de velocidad** (rate limit) aplicada en `FixedUpdate`.

- **Carro:** masa suspendida = carro 25 + mesa 12 + empujador 3 + paleta 1.5 + caja máx. 20 = 61.5 kg → peso ≈ 603 N. Con k = 150 000 N/m el hundimiento estático es de unos 4 mm. Damping crítico = 2·√(150 000·61.5) ≈ 6 075 → 6 000. forceLimit 3 000 N (≈ 5× el peso). Rampa de 0.35 m/s.
- **Mesa:** inercia en Y ≈ 1 (plataforma) + 0.5 (pared y empujador) + 0.8 (caja de 20 kg descentrada) ≈ 2.3 kg·m². k = 300 N·m/rad da ω ≈ 11 rad/s; d = 2·√(300·2.3) ≈ 52 → 60. La rampa de 45 °/s mantiene la aceleración tangencial muy por debajo de μ·g, para que la caja no deslice.
- **Empujador:** debe vencer la fricción de la caja más pesada: 0.35·20·9.81 ≈ 69 N. k = 20 000 N/m, d = 800, forceLimit 800 N. Rampa de 0.45 m/s.
- **Tope:** resiste la componente de la caja sobre la rampa de 7° (≈ 24 N, lateral). Rigidez moderada; rampa de 0.4 m/s.
- **Rodillo:** drive de velocidad pura (stiffness 0). Tracción ≈ μ_goma·N ≈ 0.9·100 N = 90 N → par de 90·0.05 = 4.5 N·m. damping 5 N·m·s/rad y forceLimit 20 N·m.

## 4. Partes pasivas

- **Palpador (Spherical, sin stiffness):** péndulo que la caja levanta al pasar. Con el pivote a 1.35 m, el brazo L = 0.45 m, la bola r = 0.03 m y la superficie a 0.80 m:
  `altura = 1.35 − 0.45·cos θ − 0.03 − 0.80 = 0.52 − 0.45·cos θ`.
  Caja baja (0.20 m) → θ ≈ 45°. Caja alta (0.40 m) → θ ≈ 75°. La lectura se toma cuando la velocidad angular es casi nula (lectura estable).
- **Compuerta (Revolute, sin stiffness):** cortina batiente en la salida derecha del nivel 1. La caja la abre al pasar y la gravedad la cierra.

Las dos tienen una `jointFriction` pequeña para amortiguar la oscilación. **No tienen stiffness ni target.**

## 5. Balanza (sensor derivado del drive del carro)

El drive del carro hace `F = k·(target − x) − d·v`. En equilibrio, F iguala el peso de todo lo que cuelga del carro. Al iniciar se toma la **tara** con la mesa vacía y después se muestra `masa = (F − F_tara) / g`, solo cuando el carro está quieto.

## 6. Cargas (Rigidbody comunes)

Las cuatro cajas tienen planta de 0.40 × 0.40 m y el mismo aspecto exterior. **Solo los sensores distinguen el tipo.**

| Tipo | Alto | Masa | Destino |
|---|---|---|---|
| Baja liviana | 0.20 m | 4 kg | Izquierda · Nivel 1 |
| Alta liviana | 0.40 m | 6 kg | Izquierda · Nivel 2 |
| Baja pesada | 0.20 m | 18 kg | Derecha · Nivel 1 (atraviesa la compuerta) |
| Alta pesada | 0.40 m | 20 kg | Derecha · Nivel 2 |

**Regla de clasificación:** altura < 0.30 m → Nivel 1; altura ≥ 0.30 m → Nivel 2. Masa < 10 kg → Izquierda; masa ≥ 10 kg → Derecha.

Materiales físicos: cartón μ 0.4; rampa de rodillos libres μ 0.03; placa de transferencia μ 0.15; goma del rodillo μ 0.9 (combinación *Maximum*); plataforma de la mesa μ 0.35.

## 7. Maniobra completa

1. **Nueva caja** (N): aparece en la parte alta de la rampa y baja hasta el tope.
2. **Bajar el tope** (Espacio): la caja baja a la placa de transferencia y pasa bajo el **palpador** → lectura de altura.
3. Con el **carro en Carga y la mesa en 0°**, poner el **rodillo en marcha** (R) → la caja pasa a la mesa. Subir el tope otra vez.
4. Leer la **balanza** → masa.
5. **Elevar** al nivel (W/S o presets) y **girar** al lado (A/D o presets).
6. **Empujar** (E) → la caja cae por la rampa de salida a su contenedor. **Retraer** (Q).
7. Volver a Carga y a 0°.

Enclavamientos (como en una máquina real): la **mesa y el carro solo se mueven con el empujador retraído**.

## 8. Geometría estática relevante

- Rampa de admisión: de z = −3.0 a z = −1.05, inclinación 7°, ancho 0.6 m con guías laterales.
- Placa de transferencia: z ∈ [−1.05, −0.61], cara superior a 0.80 m.
- Salidas: rampas a ±X que empiezan en |x| = 0.50 m, 2 cm por debajo de la plataforma. Nivel 1 a 10° hacia contenedores de piso; Nivel 2 a 5° hacia contenedores en estantería a 1.25 m.
- La plataforma de 0.70 m barre un radio de 0.495 m al girar, así que no toca la admisión ni las salidas. El hueco de 0.15 m lo puentea la caja de 0.40 m.

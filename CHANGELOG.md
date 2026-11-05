# Changelog

Versionado semántico (`MAJOR.MINOR.PATCH`). Mientras esté en `0.x`, las funciones nuevas suben `MINOR` y los arreglos `PATCH`.
La versión se define en `<Version>` de `BuildingCollections/BuildingCollections.csproj` y debe coincidir con `ModVersion`
en `BuildingCollections/Properties/PublishConfiguration.xml`.

## 0.1.14
- Arreglado: al cambiar Overlap con una manzana elegida (modo Block), se volvía a elegir con el solape viejo y en cada
  vista previa se repetía la selección.
- Publicación: `publish.sh` (ModPublisher del Modding Toolchain) y `PublishConfiguration.xml` con el formato oficial,
  privado de momento; imágenes reducidas.

## 0.1.13
- Los errores del constructor (zona sin calles, manzana por encima del límite, nada cabe, elementos en rojo...) salen
  en rojo en el panel, con un doble destello al aparecer.

## 0.1.12
- Vista previa / Reshuffle más rápidos: al cerrar huecos, cada edificio de la cadena se comprueba una sola vez (no con
  cada candidato), el hueco se mide de metro en metro antes de afinar, y los choques se descartan antes por la caja
  envolvente. En la manzana de Cobblestone esa fase tardaba 600-750 ms.
- Log: tiempo total de la vista previa.

## 0.1.11
- Curvas: el sobrante (la curva casi nunca mide un múltiplo de 8 m) ya no queda en un solo hueco de hasta ~7 m (en la
  esquina o en medio de la curva). Con "Overlap buildings" se mete una celda más y el exceso se reparte como solape
  entre las juntas de la curva (como mucho el de las opciones por junta); si no cabe o no hay solape, se reparte en
  rendijas pequeñas iguales.
- Cambiar Overlap/Anarchy con una zona elegida la vuelve a elegir al pedir la vista previa (las tiras dependen del solape).

## 0.1.10
- "Overlap buildings" vuelve a ser una casilla en el panel (0,5 m por defecto). La cantidad por junta (0,5, 1, 1,5 o
  2 m) se elige en las opciones del mod ("Overlap per joint").

## 0.1.9
- Arreglado (regresión de 0.1.6): en una curva que acaba en esquina, el sobrante final (menos de una celda) se perdía y
  el esquinero quedaba ~4 m antes de la esquina, dejando un hueco en la calle de al lado (Cobblestone/Mill Lane).
  Ahora el último tramo se alarga por su recta hasta la esquina.

## 0.1.8
- Block: un clic en una zona que no está cerrada por calles ya no selecciona los bloques cercanos; avisa de usar Polygon.
- Nueva opción "Largest block for Block mode" (1-50 ha, 5 por defecto): una manzana más grande no se selecciona.
- Log: tiempos de la selección y de cada fase de la vista previa (para ver qué la hace lenta).

## 0.1.7
- Arreglado: en modo Block, algunos clics dentro de una manzana no la seleccionaban (error al alargar un lado hasta
  una esquina redondeada cuando el lado anterior ya se había alargado; dependía de por qué lado empezaba el contorno).
  El mismo error hacía fallar Polygon.

## 0.1.6
- Curvas: tramos más cortos (tolerancia de 0,5 m en vez de 2 m), así el giro se reparte entre más juntas
  (Cobblestone: de 40 m girando 21° a 16 m girando ~8°).
- La excepción de 0.1.5 se limita a tramos contiguos y solo si la cuña de los fondos es de 3 m o menos (a 16 m de fondo).

## 0.1.5
- Curvas con overlap: los tramos de una misma curva ya no se tapan entre sí por detrás; se acaban los huecos de ~8 m en cada junta.

## 0.1.4
Primera versión numerada (PR #3).
- Colecciones de edificios y constructor de manzanas (bloque o polígono), filtros, densidad y mezcla de colecciones.
- Esquinas: el primer y último tramo de cada lado llegan a la esquina real.
- Curvas: tramos de múltiplos exactos de 8 m, pegados por delante (sin huecos en la fachada).
- Densidad sobre toda la selección; contador de celdas a la calle.
- "Overlap buildings" con Anarchy: control de 0 (Off), 0,5, 1, 1,5 o 2 m por junta.
- Quitada la opción "Leave gaps".

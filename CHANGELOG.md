# Changelog

Versionado semántico (`MAJOR.MINOR.PATCH`). Mientras esté en `0.x`, las funciones nuevas suben `MINOR` y los arreglos `PATCH`.
La versión se define en `<Version>` de `BuildingCollections/BuildingCollections.csproj` y debe coincidir con `ModVersion`
en `BuildingCollections/Properties/PublishConfiguration.xml`.

## 0.1.35
- Shift+clic en Find It ya no congela el juego ~0,45 s en cada edificio: las etiquetas de Find It se aplican cuando
  llevas 1 s sin añadir ni quitar (varios clics seguidos = un solo guardado de Find It), y la lista de ids de Find It
  solo se vuelve a leer si falta algún edificio o si un id ya no corresponde al mismo edificio (así nunca se etiqueta
  otro edificio con un id caducado).
- El log apunta cuánto tarda añadir un edificio y el guardado de etiquetas (ids y guardado de Find It por separado).

## 0.1.34
- El mod se llama Collections Builder en Opciones, en el panel y en la publicación.
- La versión ya no va en el nombre de las opciones: sale como etiqueta al final de la pestaña principal.
- "Overlap per joint" solo está en el panel del constructor; se quita de Opciones.

## 0.1.33
- Atajo nuevo para construir la vista previa (Y por defecto); solo actúa con la herramienta de manzanas activa y una
  vista previa generada.
- Teclas por defecto: Ctrl+Y abre y cierra la herramienta (antes Ctrl+B), T repite la vista previa (antes Ctrl+R) e
  Y construye. Si en un mismo frame se pulsan varios atajos, solo se aplica el primero (Ctrl+Y no construye).
- La nota del log que explica la alternativa elegida en un lado (alineado a la esquina o curva recortada) la decide
  quien crea la alternativa, en vez de deducirla del número de tramos.
- El log ya no repite los edificios existentes en cada vista previa: solo se escriben al elegir la zona.
  `extract_blocks.py` usa los de la selección cuando la vista previa no los trae.
- Log de creación del sistema en inglés, tooltip del solape corregido y comentarios quitados del harness de la UI.
- Tests: la selección recortada por un polígono no deja ningún edificio fuera del recorte ni de la manzana.

## 0.1.32
- Atajo de teclado para repetir la vista previa (Reshuffle), Ctrl+R por defecto y reasignable en Opciones > Atajos; solo actúa con la herramienta de manzanas activa y una zona elegida.

## 0.1.31
- Log de análisis: líneas `[datos]` con los puntos completos de cada acera y el contorno de cada edificio existente al
  elegir la zona, y en cada vista previa los existentes y el contorno en el mundo de cada edificio propuesto (tamaño,
  esquinero y nombre).
- `tools/extract_blocks.py` las usa si están (manzana exacta, existentes y última propuesta por cada solape); con
  logs antiguos reconstruye la manzana como antes. El simulador usa los existentes como obstáculos.

## 0.1.30
- Relleno final de celdas a la calle que quedan sin cubrir: se intenta meter un edificio normal de 1 o 2 celdas de
  ancho corriéndolo hasta 4 m (en pasos de 0,1 m), sin pisar a los demás más que el solape y sin salirse de la
  manzana. Cubre los huecos entre dos esquineros de calles largas en una calle corta (manzana de 40,7 m entre calles
  de 81 y 87 m: resuelta con 0,5 m si el catálogo tiene 1x1) y muchos huecos sueltos en curvas: en el simulador no
  empeora ningún escenario y, por ejemplo, Prospect con 2 m pasa de 32,9 a 24,9 m sin cubrir y Whitestone con
  Prospect de 8,2 a 1,9 m. Solo con densidad 100 %.
- Simulador: 10 manzanas nuevas reconstruidas del log del juego (sin sus edificios existentes).

## 0.1.29
- Curva que acaba en una esquina casi recta (90° ± 10°): si al meter "una celda más" el solape de las juntas de los
  últimos 16 m empujaría las celdas bajo el fondo del esquinero de la otra calle más de lo que permite el solape
  (solape de esas juntas + 16 m × desviación de los 90°), esas juntas van sin solape; si así no cabe la celda más,
  el sobrante se reparte en rendijas. En la manzana de Cobblestone (curva larga, 0,5 m) el esquinero se metía
  0,68 m en la penúltima celda de la curva y dejaba un hueco de 7,3 m; ahora la curva queda cubierta entera
  (12,7 -> 5,4 m sin cubrir en toda la manzana). Con 1 y 2 m no cambia.
- Simulador: manzana "Cobblestone, curva larga y calle corta", reconstruida del log del juego.

## 0.1.28
- Anarchy: el constructor lo enciende desde el propio mod (no desde el panel) mientras la pestaña Builder está abierta
  y el solape no es Off, comprobando que de verdad queda encendido; si se apaga a mano con la pestaña abierta, lo
  vuelve a encender. Al cerrar la pestaña, poner el solape en Off o salir del juego lo apaga solo si lo había
  encendido él. Antes lo hacía el panel con un "cambiar" a ciegas, que podía apagarlo si leía un estado antiguo. Si
  no se puede encender, el panel lo avisa junto a "Overlap per joint".
- Selección por polígono con varias manzanas: los tramos de manzanas distintas ya no se toman por tramos seguidos de
  la misma curva (podían cruzar los fondos y pisarse más que el solape). Un solo criterio de "tramos seguidos"
  (misma manzana, mismo lado, índices consecutivos y extremos a menos de 2,1 m) para el cruce de fondos y el cierre
  de huecos.
- Cruce de fondos entre tramos de una curva: nunca con giros de 90° o más (la fórmula lo permitía por error).
- Densidad 100 % ya no puede quitar edificios (ni el esquinero) cuando parte de la fila delantera está tapada.
- Elección de alineación por manzana con programación dinámica: mismo resultado que probar todas las combinaciones,
  sin el límite de 12 lados (antes, por encima, se abandonaba sin avisar).
- Una curva recortada que se queda sin tramos ya no cuenta como opción.
- Sin vista previa fallida cuando un mismo edificio está en las dos colecciones de la mezcla.
- Robustez: un fallo al dibujar la selección no bloquea la herramienta ni llena el log (los errores repetidos se
  registran una vez cada 10 s), cuadriláteros degenerados, listas vacías por defecto en la petición, lectura de
  edificios existentes por bloques, menos registros repetidos al quitar "Condemned".
- Código: `VirtualLots` y `EndGapCloser` repartidos en clases pequeñas (fusión de lados, tramos de curva, celdas,
  esquinas, elección de alineación, cadena del extremo, prueba de encaje y una estrategia por forma de cerrar el
  hueco); mismo resultado en el simulador. 10 tests nuevos.

## 0.1.27
- Si una calle tiene sobrante y con edificios de anchura normal no habría juntas suficientes para cerrarlo con el
  solape ni para repartirlo en rendijas de 1,5 m como mucho, se rellena solo con edificios de una celda de ancho.
  En un barrido de 6.864 rectángulos con medidas decimales (30 a 130 m) los resueltos pasan del 50,6 % al 57,4 % sin
  solape y del 65,3 % al 68,9 % con 0,5 m.
- Simulador: rectángulos con medidas decimales (35,8 × 35,8, 35,8 × 88,6, 67,3 × 52,1, 99,9 × 35,8).

## 0.1.26
- Cierre de huecos en los empalmes de tramos de una curva cuando un tramo tiene sobrante (antes solo en las puntas),
  intentándolo desde los dos tramos.
- Al meter un edificio o repartir el sobrante se prueba primero moviendo todos los edificios de la cadena y, si
  alguno choca, con menos; antes bastaba un choque en una curva larga para descartarlo todo.
- El extremo de la calle con más sobrante se atiende primero, para que los dos extremos no compitan por los mismos
  edificios.
- En calles cortas con esquina en las dos puntas, la reserva de la fila delantera deja libre al menos la celda central.

## 0.1.25
- Esquina donde llega una curva: si el esquinero no cabe, la curva se recorta por esa punta (hasta 4 m, en pasos de
  0,25 m) y se vuelve a repartir en tramos, de modo que el sobrante se reparte entre todas las juntas. Es una opción
  más de la elección por manzana: solo se usa si gana esquinas, y si empatan se prefiere la que deja más celdas.
- Un edificio metido en un hueco puede tocar de lado al esquinero inclinado de la otra calle sin pisarlo más que el
  solape permitido.

## 0.1.24
- En las curvas que giran hacia la manzana, los fondos de dos tramos seguidos pueden cruzarse hasta 4 m (antes 3 m)
  cuando hay solape: en una curva de radio 80 m con tramos de 2 celdas ya no se pierde la primera celda de cada tramo.

## 0.1.23
- La fila delantera de cada calle queda reservada: los edificios profundos de otra calle no pueden ocuparla con sus
  filas de fondo (salvo las dos celdas junto a una esquina, que son del esquinero). En manzanas estrechas se usan
  edificios menos profundos en vez de dejar la otra calle sin fachada. Los esquineros se reservan antes y no se ven
  afectados.

## 0.1.22
- Si el hueco del final de una calle no se puede cerrar ensanchando ni metiendo un edificio (o no hay solape), se
  reparte en rendijas iguales corriendo los edificios hacia él, siempre que cada rendija quede en 1,5 m o menos.
- El log de cada tira muestra el fondo libre de cada celda.
- Simulador: manzanas reales del juego reconstruidas desde el log (Whitestone, Prospect), medida de la acera con
  rayos hacia dentro (4 m), rendijas y cuñas inevitables de esquinas no rectas separadas de los huecos, y escenarios
  resueltos marcados en verde.

## 0.1.21
- Las esquinas redondeadas pequeñas (giro de 60° o más en menos de 40 m, con las calles rectas antes y después)
  cuentan como esquina: la calle se corta ahí, las dos acaban en esquina con esquinero y el arco queda libre.
  Las ondas de una calle sinuosa no se cortan.
- Si dos alineaciones dan las mismas esquinas, se prefiere la que llega hasta la esquina redondeada, para no dejar
  el sobrante junto al arco.

## 0.1.20
- Esquina donde acaba una curva: la calle recta se alinea a esa esquina (el sobrante pasa al otro extremo) si así
  hay más esquinas con esquinero; se prueban todas las combinaciones de la manzana y, si empatan, se queda la de antes.
- Si el esquinero de 2 de fondo no cabe en la esquina de una calle recta, se retranquea lo justo (en pasos de
  0,25 m) según el contorno real, no solo por el ángulo.
- Simulador: escenarios de esquinas (paralelogramos, trapecios, triángulos, lados curvos que acaban en esquina,
  esquinas redondeadas pequeñas), fachada marcada en cada edificio (en L en los esquineros), comprobación de
  esquinas, comparación de solapes por escenario y log del planificador.

## 0.1.19
- Sin cambios de comportamiento: la planificación de la vista previa (máscaras entre calles, esquinas, relleno,
  densidad, cierre de huecos y diagnóstico de acera) pasa del sistema del juego a `Core` (`PreviewPlanner`,
  `EndGapCloser`, `FootprintMask`, `FrontCoverage`, `CellFrame`), en C# puro y con tests.
- `BlockBuilderToolSystem` dividido por responsabilidad (selección, vista previa, Build, dibujo).
- Fuera los comentarios del código.
- Nuevo `BuildingCollections.Simulator` (no va en la release): ejecuta el mismo código de `Core` sobre rectángulos,
  diagonales y curvas con varios solapes y genera un informe HTML.

## 0.1.18
- El constructor trabaja con Anarchy encendido: si estaba apagado, lo enciende al abrir la pestaña Builder y lo vuelve
  a apagar al cerrarla. Fuera del panel las casillas "Anarchy" y "Overlap buildings": queda solo "Overlap per joint",
  de 0 (Off, sin solape) a 2 m (también en las opciones).

## 0.1.17
- Esquinas agudas (menos de 85° entre las dos aceras): el esquinero se corre hacia atrás lo justo para no salirse por
  la otra calle (16 m / tan α: ~2,8 m a 80°); queda un triángulo pequeño en la punta. Antes la celda de la esquina no
  cabía y las dos calles perdían casi todo el tramo.

## 0.1.16
- Giros muy cerrados (radio < ~16 m): en vez de saltarlos (hueco), se pone un tramo de una celda aunque la fachada se
  separe hasta 1,5 m de la acera.
- El sobrante de la curva se reparte también cuando un giro obligó a saltar vértices (entre los tramos desde el salto
  hasta la esquina); antes se quedaba entero en el último tramo.
- Las juntas entre tramos seguidos de una curva ya no se miden como "huecos" al cerrar finales de calle (salían
  huecos falsos de ~6 m y pruebas inútiles).
- Log: cada tira de la vista previa dice su lado y tramo.

## 0.1.15
- Curvas: el sobrante se reparte sin volver a elegir los tramos (al hacerlo, el final no caía en la esquina y la celda
  de más se descartaba aunque el solape lo permitiera). Con solape: los mismos tramos más uno de una celda al final, y
  el solape por junta justo para acabar en la esquina (bisección), ≈ (8 − sobrante) / (juntas + 1). Si no cabe, rendijas
  iguales; el log dice cuánto solape haría falta.
- Panel: "Overlap per joint" (0,5 a 2 m) vuelve debajo de "Overlap buildings" (es el mismo valor que en las opciones).

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

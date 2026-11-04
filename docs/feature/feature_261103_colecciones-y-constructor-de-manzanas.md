# Feature: Colecciones de edificios y constructor de manzanas

**Fecha:** 2026-11-03
**Estado:** Interfaz diseñada y verificada en un arnés de navegador (sesión 2); lado C# de la interfaz pendiente. Plan revisado en la sesión 3 (ver "Revisión del plan"). Ver "Estado de la implementación".

## Resumen
Mod para Cities: Skylines 2 (`BuildingCollections`) con dos piezas, en un panel propio al que se llega con un
botón de la barra superior: (1) **Colecciones** de edificios que se rellenan con lo que muestre Find It y se
etiquetan en Find It, y (2) un **constructor de manzanas** propio (no conectado a StreetBuilder) que, sobre un
bloque o polígono, planta edificios aleatorios de una colección, con esquineros en las esquinas y normales en
el resto.

## Problema / motivación
Find It filtra muy bien pero no genera manzanas. StreetBuilder genera manzanas pero obliga a elegir los
edificios uno a uno. Se quiere filtrar con Find It, guardar el resultado como colección y usarla para
autogenerar manzanas, además de poder buscarla en Find It.

## Decisiones cerradas (no reabrir sin motivo)
- **Mod independiente.** Sin dependencia, importación ni relación con Shelve It, StreetBuilder ni ningún otro
  mod. Lo único que se lee de otro mod es lo que muestra Find It (y no es obligatorio que esté).
- **Interfaz**: botón en la barra superior (`GameTopLeft`) que abre un panel flotante con **dos pestañas:
  Collections y Builder**, en React/TypeScript. Diseño cerrado en la sesión 2 (ver "Interfaz"). La página de
  opciones queda solo con el atajo de teclado.
- **Criterio de código**: lo más simple posible y reutilizando lo que ya trae el juego (`cs2/ui`: `Button`,
  `Tooltip`, `Dropdown`...) y solo hablando con otros mods cuando haga falta (Find It, por reflexión y
  validando). Se probó una versión 100 % con componentes del juego (`Panel`, `PanelSectionRow`...) y se
  descartó: no se puede verificar su aspecto sin arrancar el juego y depende de props que pueden cambiar.
  Queda como propio lo sencillo (cabecera, pestañas, filas de filtros, deslizador) y como nativo del juego
  solo el desplegable de colección y el botón de la barra.
- **Etiquetas de Find It, no el gancho de búsqueda.** Cada colección es una etiqueta `#nombre` de Find It, que
  se escribe con `FindItUtil.AddTag` / `RemoveTag` por reflexión. Se descarta el gancho `GetFindItSearchMethod`
  porque Find It guarda uno solo (`Filters.GetCustomSearchFunction = ...` sobrescribe) y chocaría con otros
  mods que lo usen.
- **Los datos de los edificios** (tamaño, nivel, esquina) se leen del **propio juego** (`BuildingData`,
  `SpawnableBuildingData`, `BuildingPrefab.m_AccessType`), no de Find It, para que el constructor funcione
  aunque Find It cambie o falte. Find It solo se usa para "añadir todo" y las etiquetas.
- **No se escribirá al autor de Find It** pidiendo una API. Hay que convivir con la reflexión y degradarla bien.
- **Platter se ignora** (sus bloques no se tocan). **Las manzanas irregulares entran en v1.**
- Sin "Zone space", sin Undo (se borra con el bulldozer del juego), sin zonificar antes.

## Alcance
### Dentro de alcance
- **Colecciones** (nombre + lista de prefabs), persistidas en `ModsData/BuildingCollections/collections.json`
  (escritura atómica). Crear, vaciar, borrar y **quitar edificios sueltos** (× en cada tarjeta y "Remove shown").
  La pestaña Collections muestra una **tarjeta por edificio** (miniatura, nombre, tamaño, nivel) con buscador y
  filtros de ancho, fondo y nivel.
- Botón **"Add all Find It items to this collection"**: suma a la colección todos los edificios de la lista
  filtrada actual de Find It (`FindItUtil.GetFilteredPrefabs()` por reflexión), sin duplicados y sin quitar
  los que ya había. Es obligatorio y tiene que existir.
- **Shift+clic en Find It añade el edificio a la colección activa** (lo pide el usuario; **todavía no
  implementado**, hubo un malentendido en la sesión 2). Se apoya en la multi-selección documentada de Find It
  (`SelectPrefab`, `DeselectPrefab`, `GetSelectedPrefabs`; Ctrl+clic como respaldo). Hay que leer qué expone
  Find It antes de decidir cómo engancharlo.
- **Etiquetas**: todos los edificios de la colección llevan la etiqueta de Find It, y se busca por ella.
  Se puede **adoptar una etiqueta ya existente** de Find It como colección (Find It la reutiliza sin
  duplicar). Al quitar un edificio, se le retira la etiqueta solo si la puso el mod o el usuario la adoptó.
  **No se escribe `CustomPrefabData.json`**: Find It lo mantiene en memoria y lo reescribe entero.
- **Constructor de manzanas**:
  - Selección de **bloque** (clic dentro de un bloque rodeado de calles) o de **polígono**.
  - Elección de **colección**.
  - **Filtros de ancho, fondo y nivel** (All, 1–5) con operadores `=`, `!=`, `>`, `<`. El tamaño se compara
    por ancho y fondo por separado, como Find It (`LotSize.x` y `LotSize.y`). **Sin filtro de tipo de zona,
    pack, tema ni DLC** (la colección ya es el filtro "bueno"; esos se hacen en Find It). **Sin filtro de
    esquinas.** La interfaz los muestra como Find It: `[op] ∨ All ∧`, y el operador solo aparece con un valor.
  - **Densidad** de 1 a 100 % con deslizador propio (el motor no pinta `<input type="range">`): el objetivo es
    cubrir `floor(celdas libres × densidad / 100)` celdas (100 cubre todas, 50 cubre la mitad; el número de
    edificios varía según su tamaño). No existe el 0 %: sin
    nada que construir, el botón queda deshabilitado.
  - Botones, de arriba abajo: **Generate preview** (pasa a **New combination** al generar), **Clear** (rojo,
    borra selección y vista previa) y **Build** (verde, deshabilitado hasta que hay una combinación). Ya no
    hay "Cancel preview" ni "Start selecting": abrir la pestaña Builder activa la herramienta y salir la
    desactiva; Ctrl+B sigue funcionando.
  - **Vista previa** transparente antes de construir.
  - **Build** planta edificios reales (como un plop). No hace falta zonificar.
  - Esquinas: edificios esquineros; el resto, normales. Si la colección no tiene esquineros, las esquinas se
    rellenan con normales.
  - **New combination** conserva las reglas (colección, filtros, densidad) y los candidatos, y sortea otro
    resultado con otra semilla.

### Fuera de alcance (explícito)
- Integración con StreetBuilder, Shelve It, Platter u otros mods. Sin importar sus datos.
- Parchear la UI de Find It.
- Bloque "Zone space" y otras mediciones de StreetBuilder.
- Undo last build.
- Pesos por edificio dentro de una colección.
- Cálculo propio de contornos o parcelas: se usa la rejilla de zonas del juego.
- Decoración del interior de la manzana (árboles, jardines).
- Parte comunitaria o de red (publicar, compartir, servidores).

## Reglas de negocio e invariantes
- Una colección no tiene edificios duplicados. "Add all" suma, nunca sustituye.
- Un edificio de NxM ocupa NxM celdas contiguas, con la fachada hacia la calle (orientación del bloque).
- Esquina = celda con calle en un lado lateral de la fila frontal. Solo ahí van esquineros, y solo si existen.
- Solo se construye sobre celdas libres (ni bloqueadas, ni ocupadas, ni compartidas, en v1). No hace falta zona.
- La densidad es el porcentaje de celdas cubiertas, no de edificios.
- El mod solo quita etiquetas de Find It que él mismo puso o que el usuario adoptó.
- El resultado depende solo de las entradas y de una semilla que cambia en cada New combination.
- Nada de lo que falle (Find It ausente o cambiado, datos raros) debe tumbar el juego: `try/catch`, aviso en
  el log y la función afectada se desactiva.

## Interfaz (decidida en la sesión 2)
Verificada con un arnés que ejecuta el **React real** en un navegador con un simulador del lado C#
(`BuildingCollections/UI/harness/`, ver "Cómo trabajar la UI sin arrancar el juego"). Maqueta publicada en un
artifact privado de claude.ai (versión 7): https://claude.ai/artifact/74XQ44T9RGA3UnNFyvEVFs

- **Botón de la barra**: `Button variant="floating"` + `Tooltip` de `cs2/ui`, añadido con
  `moduleRegistry.append("GameTopLeft", ...)`. Icono propio: una **grúa torre** (`UI/icon.svg`). El motor
  **no admite `data:` URIs** para el icono (salía el icono por defecto), así que el mod registra su carpeta con
  `UIManager.defaultUISystem.AddHostLocation("buildingcollections", dir)` y el botón usa
  `coui://buildingcollections/icon.svg` (el csproj copia `icon.svg` junto al DLL). Si el registro falla, el log avisa.
- **Collections** (panel ancho): lista de colecciones a la izquierda (crear, "Add all Find It items", Empty y
  Delete con confirmación en línea) y a la derecha título, buscador, filtros de ancho/fondo/nivel y rejilla de
  tarjetas con × para quitar; abajo "Remove shown" y una nota de Shift+clic.
- **Builder** (panel estrecho): Block / Polygon con su texto de ayuda; colección con el **desplegable nativo
  del juego** (`Dropdown`, `DropdownToggle`, `DropdownItem` de `cs2/ui`; los tipos declaran `DropdownItem` solo
  como interfaz, por eso se usa `(ui as any).DropdownItem`); filtros de ancho/fondo/nivel; densidad; resumen
  ("N of M buildings match · K lots to fill"); y los tres botones. Generate preview está deshabilitado hasta
  que haya área seleccionada y candidatos; Build, hasta que haya combinación.
- **Panel**: altura máxima de la ventana con scroll interno (no debe cortarse).
- **Contrato de bindings** (grupo `BuildingCollections`, definido en `UI/src/bindings.ts`):
  valores `panelOpen`, `toolActive`, `findItAvailable`, `selected`, `status`, y JSON en `collections`
  (`[{name, tag, count}]`), `buildings` (de la colección seleccionada: `{id, name, w, d, level, icon}`),
  `builder` (`{mode: "block"|"polygon", widthOp/Value, depthOp/Value, levelOp/Value, density}`) y `plan`
  (`{areaSelected, previewing, lots}`). Disparadores: `setPanelOpen`, `select`, `create`, `clear`, `delete`,
  `removeBuildings` (JSON con ids), `addAll`, `setBuilder` (JSON), `setToolActive`, `generatePreview`,
  `clearSelection`, `build`. El filtrado de la pestaña Collections se hace en el cliente.
- **Estado del lado C#**: existen `Systems/BcUISystem.cs` (bindings antiguos: `filters`, `setFilters`,
  `toggleTool`) y el contrato viejo; **hay que adaptarlo al contrato nuevo de arriba**.

## Cómo trabajar la UI sin arrancar el juego
El arranque del juego tarda ~26 min hasta el menú (y ~56 min hasta que acaba Extra Assets Importer), así que
la UI se verifica fuera. En `BuildingCollections/UI/`:
- `harness/run.sh <carpeta>`: compila el arnés (webpack, con `cs2/api`, `cs2/ui` y `cs2/modding` sustituidos
  por simulaciones en `harness/`), genera `harness/dist/harness.html` y hace capturas con el Chromium de
  Playwright (`~/.cache/ms-playwright/chromium-*/chrome-linux64/chrome`). Hashes: `#builder`, `#builder-demo`,
  `#builder-picker`. `harness/backend.ts` simula el C# (colecciones, selección, vista previa).
- `npx webpack` (con `BC_OUT` opcional) compila el módulo real a `Mods/BuildingCollections/` (`.mjs` + `.css`);
  `dotnet build -p:Deploy=true` despliega el DLL y el icono. Con el juego abierto el DLL queda bloqueado.
- El arnés **no reproduce el aspecto real** de los componentes de `cs2/ui`; eso solo se ve en el juego.
- Una importación circular `backend → bindings` rompía el arnés (TDZ); el simulador define sus propios valores.
- Los `rem` del SCSS se mapean en el arnés a `calc(N * var(--u))` para poder hacer zoom.

## Hallazgos de la sesión 2 (para no repetirlos)
- **El clic no llegaba a la herramienta**: `ToolBaseSystem.UpdateActions()` es `private protected` y vacío en la
  base; las herramientas del juego activan `applyAction`, `secondaryApplyAction` y `cancelAction` ahí. Un mod
  externo no puede sobrescribirlo, así que `BlockBuilderToolSystem.OnStartRunning` ahora hace
  `applyAction.shouldBeEnabled = true` (y las otras dos). Corregido en el código, **sin probar en el juego**.
  La causa se dedujo del log: había "Herramienta activada" pero ninguna línea `[sondeo`.
- **Protección contra `Condemned`** (de ApartmentPlan, sin licencia, solo leído): los edificios de crecimiento
  (`SpawnableBuildingData`) construidos sin zonificar se marcan `Condemned` y se demuelen. Hay que quitar
  `Condemned` y el icono de notificación a las entidades propias antes del consumidor nativo, y persistir la
  lista de entidades propias (buffer remapeado al guardar). **Requisito del Build real.** Plop the Growables
  (Apache-2.0) hace lo mismo y se puede adaptar con atribución.
- **Colocación real**: entidad con `CreationDefinition { m_Prefab, m_RandomSeed }` + `ObjectDefinition
  { m_Position, m_Rotation, m_Probability = 100, m_PrefabSubIndex = -1, m_Scale = 1, m_Intensity = 1,
  m_ParentMesh = -1 }` + `Updated`; el juego genera la vista previa (`Temp`) y las comprobaciones de error;
  Apply explícito. Ciclo: borrar definiciones, crear, esperar unos fotogramas, validar, Apply. Altura con
  `TerrainUtils.SampleHeight` y `TerrainSystem.GetHeightData()` (solo lectura, compatible con MapExt2).
  Trabajo largo repartido en fotogramas con corrutinas.
- **Tamaño real de un edificio**: `BuildingData.m_LotSize` y `ObjectGeometryData.m_Size`.
- **Filtros de Find It**: son una clase `Filters` (propiedades públicas: `LotWidthFilter`, `LotWidthSign`,
  `BuildingLevelFilter`, `SelectedZoneType`, `SelectedAssetPacks`, `SelectedTheme`, `SelectedDlc`...) sobre un
  `PrefabIndex` propio. Su panel React no se puede incrustar (otro mod, sin licencia). Los packs, temas, DLC y
  zonas salen de datos del juego (`AssetPackPrefab`, `ThemePrefab`...), por si algún día se quieren.
- **Selección de polígono** (idea de ApartmentPlan y Parking Lot Tool, GPL, solo ideas): snap de 8 m, cierre a
  12 m, guías punteadas con `OverlayRenderSystem.Buffer.DrawLine`.
- **Integración opcional por reflexión**: comprobar firmas y campos antes de usar y desactivar si no coinciden.
- Fallos de otros mods que salen en el log y **no son nuestros**: `ExtraLib.Debugger.Logger.Info` revienta
  dentro de `UnityLogger.Internal_WriteStream` (Extra Assets Importer, ExtraNetworksAndAreas); avisos
  "Duplicate prefab ID". Ver `docs/startup_analysis_261103.md` para el arranque lento.

## Estado de la implementación (fin de la sesión 2)
Código en `BuildingCollections/` (net48, compila sin avisos), tests en `BuildingCollections.Tests/` (23 pasan).
En el juego se probó (sesión 2): el mod carga, las colecciones se crean, llenan, vacían y borran bien, "Add all
Find It items" funciona (etiquetas incluidas) y el botón y el panel aparecen. **Aún no desplegado**: el arreglo
del clic, el icono de grúa y el contrato nuevo de bindings (el juego estaba abierto).

| Pieza | Estado |
|---|---|
| `Core/` lógica pura: filtros `=,!=,>,<`, colecciones sin duplicados, `GridPlanner` (esquinas, densidad, semilla) | Hecho y con tests |
| `CollectionStore` (JSON atómico), `CollectionService` (crear, añadir todo, vaciar, borrar) | Hecho, sin probar en juego |
| `FindItBridge` (reflexión: lista filtrada, todas por nombre, etiquetas) | Hecho, sin probar en juego |
| Página de opciones (colecciones, filtros, densidad, Ctrl+B) | Hecho, **temporal** |
| `BlockBuilderToolSystem` (sondeo: lee bloques y celdas bajo el cursor, ignora Platter, registra el plan) | Hecho, sin probar en juego. **No construye nada** |
| Panel React (`UI/`): diseño final con arnés de navegador | Hecho y revisado por el usuario. En el juego solo se vio la primera versión |
| `BcUISystem` con el contrato nuevo (edificios con miniatura, `removeBuildings`, `setBuilder`, `plan`...) | **Pendiente (siguiente paso)** |
| Icono de grúa y `AddHostLocation` en `Mod.cs` | Hecho, sin probar en el juego |
| Arreglo del clic (acciones de entrada habilitadas) | Hecho, sin probar en el juego |
| Shift+clic en Find It añade a la colección activa (multi-selección documentada) | Pendiente |
| Datos de edificios desde el juego (no desde Find It) | Pendiente (hoy el sondeo los toma de Find It) |
| Vista previa y Build con edificios reales | Pendiente, depende del log del sondeo |
| Validar la forma de los métodos de Find It antes de llamarlos | Pendiente |

## Revisión del plan (sesión 3, 2026-11-04)
Revisión del spec y del código hecho en la sesión 2, contrastada con el `Game.dll` y el `Colossal.UI.dll`
descompilados y con el código de Find It y Plop the Growables. La UI **no se cambia**: todo lo de abajo es del
lado C# o del flujo de trabajo.

### Cómo reducir reinicios (comprobado en el DLL)
- **La UI se recarga en caliente** si el juego arranca con la opción de lanzamiento **`--uiDeveloperMode`**
  (Steam → Propiedades → Opciones de lanzamiento). `UIManager(developerMode)` activa `liveReload` y el depurador
  de Coherent. Las carpetas de mods **locales** se registran como `ui-mods` con vigilancia (`isLocal`), y un
  cambio en un `.js`, `.css` o `.html` recarga la página entera de la UI. Los bindings de C# siguen vivos.
  - Ojo: **`.mjs` no cuenta como página** (`IsPageFileExtension` solo acepta `.js`, `.css` y `.html`), y
    webpack no reescribe ficheros que no cambian. Tras `npx webpack` hay que esperar unos 2 s y hacer
    `touch BuildingCollections.css` para forzar la recarga.
  - El depurador (`http://localhost:9444` desde Chrome) enseña el DOM real del juego: el aspecto de
    `Dropdown` y `Button` de `cs2/ui` se puede revisar y retocar en vivo.
  - El DLL **no** se recarga: cada cambio de C# cuesta un arranque. Por eso cada arranque tiene que llevar
    todo el C# posible y registrar en el log todo lo que haga falta para el siguiente paso.
- **Un playset de desarrollo** con solo este mod y Find It (sin Extra Assets Importer ni packs de assets).
  Así el arranque baja de unos 26 min a pocos minutos; los mods locales de `Mods/` se cargan igual. Se prueba
  en una ciudad nueva y pequeña con edificios del juego base. El playset completo se usa solo para la prueba
  final de compatibilidad.
- **Parámetros dudosos en un JSON que se relee en cada acción**
  (`ModsData/BuildingCollections/dev.json`): `CreationFlags`, desplazamiento de altura, fotogramas de espera
  antes de aplicar, tolerancia de la búsqueda de bloques, nivel de detalle del log... Si algo sale torcido, se
  corrige sin recompilar.
- Para cambiar de partida no hace falta reiniciar: se vuelve al menú y se carga otra. Las cachés de C# deben
  reconstruirse al cargar una partida (`OnGameLoadingComplete`).
- Los logs (`Logs/BuildingCollections.log`, `UI.log`) se leen desde WSL sin intervención del usuario.

### Problemas encontrados (del lado C#)
1. **Shift+clic en Find It no funcionaría como se planeó.** La multi-selección de Find It
   (`FindItUISystem.MultiSelect.cs`) solo se activa si **nuestra herramienta es la activa**, y entonces
   Ctrl+clic **alterna** (quita si ya estaba) y Shift+clic selecciona un **rango** desde el último clic,
   deseleccionando lo que quede fuera. Si `GetSelectedPrefabs` devolviera la colección, un Shift+clic
   **vaciaría parte de la colección**. Además, al abrir Find It con "Select prefab on open", este activa la
   herramienta de objetos y la nuestra deja de estar activa.
   **Propuesta:** no implementar ese contrato y escuchar `ToolSystem.EventPrefabChanged`. Find It (y el menú
   de assets del juego) disparan ese evento al hacer clic en un edificio. Si Shift está pulsado
   (`Keyboard.current`, hay que añadir la referencia a `Unity.InputSystem`), el panel está abierto y hay una
   colección elegida, el edificio se añade a ella y se etiqueta. No depende de Find It y el texto de la UI
   sigue siendo cierto. Efecto secundario: el edificio queda en el cursor, como con un clic normal. No se debe
   devolver la herramienta por defecto, porque Find It cierra su panel al detectarlo.
2. **Celdas válidas mal calculadas** (`BlockBuilderToolSystem.IsFree`, `BuildGrid`): el juego solo usa las
   celdas dentro de `ValidArea.m_Area` (x en `[x, y)`, profundidad en `[z, w)`) **y** con `CellFlags.Visible`
   (`ZoneSpawnSystem`). El código actual cuenta como libres las celdas fuera del área válida o invisibles
   (tapadas por otro bloque o por la calle), y el planificador pondría edificios ahí. El ancho útil para las
   esquinas también es el del área válida, no `m_Size.x`.
3. **La fila 0 siempre da a la calle** (`CellBlockJobs` y `CellOverlapJobs` ponen `Roadside` en `y == 0`, y
   `ZoneUtils.GetPosition` / `GetCellPosition` lo asumen). La heurística `frontRow` / `flip` sobra; se deja solo
   como comprobación en el log.
4. **"Bloque" no está definido en el código.** Una manzana rodeada de calles son varios `Block` (uno por tramo
   de calle y lado). El sondeo actual toma los bloques cuyo centro está a menos de 48 m del clic, lo que mezcla
   bloques del otro lado de la calle y deja fuera los de una manzana grande. **Propuesta:** partir del bloque
   que contiene el clic (`ZoneUtils.CalculateCorners` + punto en el cuadrilátero) y extender por relleno a los
   bloques cuyos contornos se tocan o solapan (mismo lado de calle), buscando con `Game.Zones.SearchSystem`
   (regla R11.2) en lugar de recorrer todos los bloques.
5. **Receta de colocación**: la referencia buena no es LineTool sino `ZoneSpawnSystem.Spawn` del propio juego.
   Esta usa la posición `ZoneUtils.GetPosition(block, lotMin, lotMax)`, la rotación `ZoneUtils.GetRotation(block)`
   y la altura `BuildingUtils.CalculateFrontPosition` + `TerrainUtils.SampleHeight` + `m_PlacementOffset`,
   limitada por `Cell.m_Height`. Además crea definiciones aparte para las **sub-áreas y sub-redes** del
   edificio (`OwnerDefinition`). Sin ellas faltarían caminos de acceso y parcelas. Para la vista previa: sin
   `Permanent` (entidades `Temp`); para construir: `applyMode = Apply`. **Sin `CreationFlags.Construction`**,
   ver el punto 6.
6. **Plop the Growables está instalado** en el playset del usuario. Desactiva el `ZoneCheckSystem` del juego y
   protege los edificios "plantados", que son los que no tienen `UnderConstruction` ni `SpawnedBuilding`. Con
   `Construction` nuestros edificios pasarían por obra y PtG los trataría como crecidos (y los demolería). Sin
   ella, PtG ya los protege, pero el mod debe protegerse solo. **Propuesta:** en lugar de una lista de entidades
   persistida y remapeada, poner a nuestros edificios un componente vacío `IEmptySerializable` (como el
   `PloppedBuilding` de PtG, Apache-2.0) y un sistema en `ModificationEnd` que les quite `Condemned` y su icono.
   No se desactiva `ZoneCheckSystem` (chocaría con PtG). La protección propia se prueba en el playset de
   desarrollo **sin PtG**, y la convivencia con PtG en el playset completo.
7. **Herramienta y pestaña desincronizadas**:
   - El clic derecho (`cancelAction`) hoy **sale** de la herramienta, pero la UI dice "Right click: clear" (en
     polígono, "undo last point"). La pestaña Builder sigue abierta con la herramienta apagada, y no se vuelve
     a activar hasta cambiar de pestaña. Se cambia a lo que dice la UI: borrar la selección o el último punto.
   - `setToolActive(false)` solo debe volver a la herramienta por defecto **si la activa es la nuestra**. Si
     el usuario cogió una carretera, cerrar el panel no puede quitársela.
   - Ctrl+B enciende la herramienta sin panel. **Propuesta:** que Ctrl+B abra o cierre el panel (sin elegir
     pestaña).
8. **Rendimiento de los bindings**: `BcUISystem.OnUpdate` serializa `collections` a JSON **en cada
   fotograma**. Con `buildings` (cientos de entradas con miniatura) sería caro. Hay que actualizar solo al
   cambiar algo, con un indicador de "sucio".
9. **Datos de edificios desde el juego**:
   - Índice nombre → (entidad, `BuildingPrefab`) con una consulta `BuildingData` + `PrefabData`.
   - Hay 306 avisos "Duplicate prefab ID": se queda el primero y se registra cuántos se repiten.
   - Extra Assets Importer **termina de cargar assets unos 30 min después** del menú: el índice debe
     reconstruirse si cambia el número de prefabs (o al elegir colección) y tolerar nombres aún no cargados
     (se muestran como "no cargado", no se borran).
   - Miniatura: `Game.UI.ImageSystem.GetThumbnail(prefab)` (estático; devuelve el icono o
     `thumbnailUrl?width=128&height=128`).
   - Nivel: `SpawnableBuildingData.m_Level` (0 si no es de crecimiento).
   - Esquinas: `m_AccessType` pasado a `CornerKind`, con prueba unitaria del mapeo (`All`, `FrontAndBack`,
     `OnRoad`...).
10. **Ajustes no persistidos**: `SelectedCollection`, filtros y densidad se asignan sin `ApplyAndSave()`, así
    que se pierden al reiniciar. Falta el campo del modo (bloque o polígono).
11. Menores:
    - `CollectionStore.Save` borra y mueve el fichero (hay un instante sin él): usar `File.Replace`.
    - `mod.json` no se despliega (el log dice `m_ModuleId: null`): copiarlo junto al `.mjs`.
    - Definir `plan.lots`: número de parcelas de la vista previa, o de celdas frontales libres antes de
      generarla.

### Decisiones del usuario (sesión 3)
- Shift+clic por `EventPrefabChanged`: aceptado que el edificio quede en el cursor.
- Opciones de lanzamiento: `-developerMode --uiDeveloperMode`.
- Playset "Development": Find It, Asset Icon Library, Unified Icon Library, Skyve, Images (local), Move It,
  Better Bulldozer y Building Collections. Para el arranque 1 se desactivan **Plop the Growables** (para probar
  nuestra protección) y **Anarchy** (oculta los errores de la vista previa).
- Código en https://github.com/caay2000/CSII-collections-builder (MIT).

### Hecho en la sesión 3 (compila, 38 tests, desplegado; sin probar en el juego)
- `BcUISystem` con el contrato nuevo, envío solo al cambiar, ajustes guardados con `ApplyAndSave()`.
  `setToolActive(false)` solo quita nuestra herramienta y Ctrl+B abre o cierra el panel.
- `PrefabCatalog`: datos y miniatura (`ImageSystem.GetThumbnail`) desde el juego. Se reconstruye si cambia el
  número de prefabs, y los nombres sin cargar se muestran como "(not loaded)".
- Shift+clic (`EventPrefabChanged` + Shift), `removeBuildings` y caché nombre → id de Find It.
- Herramienta (`BlockBuilderToolSystem : ObjectToolBaseSystem`):
  - Clic: selecciona la manzana por relleno (`Core/Geometry.cs`, `BlockGrouping`), con resaltado.
  - Clic derecho: borra la selección.
  - Rejilla con `ValidArea` + `Visible`.
  - Vista previa con `CreateDefinitions` del juego, posición y rotación de `ZoneSpawnSystem` y altura con
    `CalculateFrontPosition`.
  - Build con `applyMode = Apply`; no aplica si hay errores, salvo que se desactive en `dev.json`.
- `BuiltGuardSystem`: marca `BcBuilt` (`IEmptySerializable`), quita `Condemned` y su icono, y registra el estado
  de los edificios tras la espera.
- `dev.json` (`DevConfig`): `BlockTouchTolerance`, `ClickSnapDistance`, `SwapCornerSides`, `RequireNoErrors`,
  `UsePlacementOffset`, `LogCells` y `ReportAfterFrames`.
- `UI/deploy-ui.sh` compila la UI y toca el `.css` para la recarga en caliente.
- Queda para el arranque 2: el modo polígono (hoy usa la selección de manzana) y quitar las opciones
  temporales de `Settings.cs`.

### Arranque 1 (2026-11-04, playset Development): resultados
- **Arranque**: menú en 11 min (antes 26,7).
  - Hay 4,3 min sin escribir en ningún log antes de registrar Paradox Mods, y 3,5 min tras cargar los prefabs.
    En total el juego lee **137 GB** del disco (`.cache` ocupa 81 GB, con 948 paquetes descargados, aunque solo
    10 estén activos).
  - Hipótesis sin demostrar: el juego recorre los paquetes descargados, no los activos. Para comprobarlo hay que
    arrancar con menos suscripciones.
  - Los mods del playset cargan en menos de 2 s.
- **Funciona en el juego**:
  - Colecciones: crear, borrar, quitar edificios, "Remove shown" y Shift+clic.
  - Builder: selección de manzana con resaltado, vista previa, New combination y Build.
  - Protección contra `Condemned` sin Plop the Growables (21 de 21 en pie). Durante un instante se ve el icono de
    abandono; con PtG no ocurre, porque sustituye `ZoneCheckSystem`.
  - Recarga en caliente de la UI.
- **Corregido después** (C#, pendiente del arranque 2):
  - Selección por celdas visibles (`FloodByCells`, umbral 8 m + `BlockTouchTolerance`) y contorno real.
  - Modo polígono.
  - Celdas `Shared` aprovechadas, con huellas de lo planificado y 5 puntos de prueba por celda.
  - Esquinas con acceso exacto, como `ZoneSpawnSystem`: un edificio "izquierda y derecha" solo cuando hay calle a
    los dos lados y llena el ancho.
  - Etiquetas de Find It en cola (`TagQueue`) y caché de ids una vez por operación: el borrado tardaba 10-20 s.
  - Clonar colección y densidad de 10 en 10.
  - Reactivar la herramienta al volver de otra (por ejemplo, tras construir una carretera).
  - Un clic fuera de una celda de zona no selecciona nada (`ClickSnapDistance = -1.5`).
- **UI** (ya desplegada en caliente):
  - Tarjetas al estilo de Find It: tamaño y nivel en la primera línea y nombre legible debajo.
  - Desplegable propio; el nativo salía blanco.
  - Botones de colección apilados y etiquetas visibles en los campos (Gameface no pinta el `placeholder`).
  - `Scrollable` de `cs2/ui` para que se vea la barra de scroll.
  - Panel arrastrable, de ancho fijo (700) y con la pestaña Collections a como máximo el 70% de alto.
  - Recuerda la última pestaña.
- **`dev.json` del usuario**: ya existía con valores antiguos, que mandan sobre los del código. Se ha puesto a mano
  `BlockTouchTolerance = 4` y `ClickSnapDistance = -1.5`.
- **Pendiente**:
  - Arranque 2, para probar todo lo de C#.
  - Rediseño de la UI con Claude Design (el usuario lo propone para otro día; quizá Builder en dos columnas).
  - Redimensionar el panel: descartado de momento.
  - Arranque 3 con el playset completo (PtG, Extra Assets Importer, MapExt2).

## Plan de implementación (revisado en la sesión 3)
Reglas de código: `docs/implementation_rules.md`. Referencias: `ZoneSpawnSystem` del juego (solo lectura) para
colocar, LineTool-CS2 (Apache) para el ciclo de vista previa y aplicar, y Plop the Growables (Apache) para
`Condemned`.

**Antes del arranque 1, sin juego** (todo con `try/catch` y tests de la lógica pura):
1. Corregir el lado C# de la UI: el contrato nuevo de `BcUISystem` (puntos 7, 8 y 10), los datos de edificios
   desde el juego (9), `removeBuildings` y Shift+clic por `EventPrefabChanged` (1).
2. Selección de bloque por relleno desde el bloque clicado (4), con celdas válidas (2, 3) y resaltado con
   `OverlayRenderSystem`.
3. Vista previa y Build según la receta del juego (5), con el componente propio y la protección contra
   `Condemned` (6), y `dev.json` con los parámetros dudosos.
4. Log de diagnóstico: los bloques recogidos con su `ValidArea`, el mapa de celdas, las esquinas, el plan, las
   entidades creadas y su estado al cabo de unos segundos (`Condemned`, `UnderConstruction`).

**Arranque 1** (playset de desarrollo, `--uiDeveloperMode`, ciudad nueva):
- Comprobar el icono, el panel, el desplegable nativo, crear, llenar y quitar colecciones, y Shift+clic.
- Comprobar el clic en la manzana, el resaltado, la vista previa, New combination y Build.
- Esperar a que la simulación pase el chequeo de zonas y confirmar que nuestros edificios no se demuelen.
- Los fallos de UI se corrigen **en el mismo arranque** con recarga en caliente; los de colocación, en lo
  posible, con `dev.json`.

**Arranque 2:** correcciones de C# que salgan del arranque 1, más el modo polígono (cierre del polígono y
solapes entre rejillas con `CellFlags.Shared` / `ZoneUtils.CanShareCells`). Quitar las opciones temporales de
`Settings.cs`.

**Arranque 3** (playset completo): convivencia con Plop the Growables, colecciones con assets de Extra Assets
Importer cargados tarde, MapExt2 en 57 km con un bloque lejos del centro, y Find It ausente (también se puede
probar en el playset de desarrollo).

Pruebas: lógica pura en `BuildingCollections.Tests`; lo demás, en el juego y por el log.

## Preguntas abiertas / riesgos
- **Reflexión sobre Find It** (`GetFilteredPrefabs`, `AddTag`, `RemoveTag`, `AllPrefabs`): no es API documentada.
  Mitigación: validar firmas, `try/catch`, degradar, y la multi-selección documentada como respaldo.
- **Find It no tiene licencia**: se lee para entender, no se copia su código.
- **Reutilizar código de Shelve It (MIT)**: el usuario permite copiar con revisión crítica y atribución, pero
  sin dependencia ni relación con ese mod. Hay que revisar línea a línea (tiene bugs que el usuario comentará
  si se importa código) y no copiar sin sentido. Candidatos: estructura del botón (`ToolbarButton`), host del
  panel, configuración de webpack y modo de previsualización en navegador. No copiar nada de comunidad ni red.
- **Botón de la barra**: resuelto en el diseño (`moduleRegistry.append("GameTopLeft", ...)`); la herramienta
  se activa desde C# con el disparador `setToolActive` / Ctrl+B. Sin probar en el juego.
- **Colocación real de edificios** (entidades, ocupación de celdas, servicios y red): sin verificar.
- **Orientación de las celdas**: se asume que la fila frontal es la de más celdas `Roadside` y que el índice es
  `y * m_Size.x + x` (comprobado en `ZoneSpawnSystem`). Falta confirmar con el log.
- **Manzanas irregulares**: cada calle genera su rejilla, y en las esquinas se solapan. Resolver solapes.
- **Qué significa "no cabe nada en el bloque"**: aviso y botón Build deshabilitado.
- **Bloques de Platter**: se detectan por el propietario del bloque, comprobando el nombre del tipo.
- **Compatibilidad con MapExt2**: no hacer Harmony sobre `TerrainSystem`, `BaseCellMapSystem`, `WaterSystem` ni
  `AirwaySystem`; no asumir el tamaño del mapa.
- Cada actualización del juego puede cambiar clases internas.

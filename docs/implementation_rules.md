# Reglas de implementación — mods de Cities: Skylines 2

**Fecha:** 2026-11-03
**Fuente:** 61 repos de CS2 (clonados con `--depth 1`) de JadHajjar (Find It, RoadBuilder...), algernon-A,
yenyang, krzychu124, AlphaGaming7780, bruceyboy24804, **lucarager** (Platter, Network Tools y 10 más) y
**Noel-leoN** (MapExt2). Los conteos de las secciones 3 a 6 se calcularon sobre los 47 primeros repos. Los números son "repos que usan el patrón / 47" salvo que se diga otra cosa,
contados con `grep`. Una regla que aparece en pocos repos se marca como **opinión**, no como norma.

> Aviso: no he podido comprobar quién usa IA y quién no. Estas reglas salen de lo que el código hace.

## 1. Licencias: qué se puede reutilizar

| Licencia | Repos | Qué podemos hacer |
|---|---|---|
| MIT | yenyang (11 de 12), JadHajjar (RoadBuilder, ZoneColorChanger, HardMode...) | Reutilizar con aviso de copyright |
| Apache 2.0 | algernon-A (los 7 de CS2 clonados) | Reutilizar con aviso y fichero NOTICE |
| MIT | blinkone: Shelve It (se puede copiar con revisión crítica y atribución) | Reutilizar con aviso de copyright |
| MIT | lucarager: Network Tools, LucaModsCommon, RealismTweaks, AssetMenuTweaks, Stats, SubElementManager, RoadWearToggle, BetterButtons, BetterDevMenu | Reutilizar con aviso de copyright |
| **Sin licencia** | Find It, PrefabUpdateMod, **Platter**, Clearance, DebugUtils, **MapExt2**, bruceyboy24804 (todos), krzychu124 (todos), AlphaGaming7780 (todos), TopoToggle | **Solo leer para entender.** No copiar código |

- **R1.1** Leemos todo, pero copiamos código solo de repos MIT o Apache, con atribución en el fichero.
- **R1.2** De los repos sin licencia solo se toman ideas y formatos de datos, y el código se escribe de cero.

## 2. Proyecto y compilación

- **R2.1** Target `net472` (19 repos) o `net48` (7), `LangVersion` 9. Nuestro csproj (net48, LangVersion 9)
  está dentro de la norma.
- **R2.2** La norma de la comunidad es importar `Mod.props` y `Mod.targets` con la variable de usuario
  `CSII_TOOLPATH` (33 repos). Solo funciona en Windows. Nuestro csproj para WSL es una desviación
  consciente: sirve para desarrollar, y el empaquetado y la publicación se hacen en Windows con la
  toolchain oficial.
- **R2.3** Código y recursos en un solo proyecto con carpetas: `Systems/`, `Domain/`, `Utilities/`,
  `Locale/`, `UI/` (patrón de Find It). Los repos de algernon-A añaden `Config/References.csproj`.

## 3. Punto de entrada (`IMod`)

- **R3.1** Una clase `Mod : IMod` con `OnLoad(UpdateSystem)` y `OnDispose()` (44/47).
- **R3.2** Un `ILog` estático creado con `LogManager.GetLogger(...)` (45/47). Todo se registra en
  `Logs/<NombreMod>.log`, que ya funciona en nuestro mod.
- **R3.3** Ajustes con una clase que hereda de `ModSetting` (41/47), con `SetDefaults()` (39/47), registrada
  en la pantalla de opciones del juego.
- **R3.4** Textos con `AddLocaleSource` o `IDictionarySource` (29/47). Nada de textos fijos en la UI.
- **R3.5** El mod no debe romper el juego: errores capturados y registrados en `OnLoad`, y degradación
  suave si una integración falla. Para otros mods se usa reflexión (`GetMethod`, `Assembly`) y nunca una
  referencia dura (19 repos, entre ellos Find It, RoadBuilder y Traffic).

## 4. Sistemas ECS

- **R4.1** Sistemas que heredan de `GameSystemBase` (40/47), registrados con
  `updateSystem.UpdateAt<T>(SystemUpdatePhase.X)` o `UpdateAfter` / `UpdateBefore` (38/47).
- **R4.2** Lógica pesada en jobs (`IJobChunk` / `IJobEntity`, 12/47), con `[BurstCompile]` (17/47). Los
  cambios de entidades se difieren con `EntityCommandBuffer` (12/47) y una barrera
  (`ToolOutputBarrier` / `ModificationBarrier`, 14/47).
- **R4.3** Los prefabs se resuelven con `PrefabSystem` (34/47), no con búsquedas manuales.
- **R4.4** Harmony (22/47) solo cuando no hay otra forma. LineTool lo usa solo para colgar su botón de la
  barra de herramientas. **Opinión:** preferir ECS y APIs del juego y usar Harmony lo mínimo.

## 5. Herramientas con selección y vista previa

Patrón de `LineTool-CS2` (Apache) y `Traffic`, que usaremos como referencia principal:

- **R5.1** Una herramienta que hereda de `ToolBaseSystem` (17/47), o de `ObjectToolBaseSystem` si coloca
  objetos. Se sobreescriben `toolID`, `InitializeRaycast()`, `GetPrefab()` y `TrySetPrefab()`.
- **R5.2** El raycast se configura con `m_ToolRaycastSystem` (16/47) y su `typeMask`.
- **R5.3** La entrada va con `applyAction` y `cancelAction` del juego, y las acciones propias son
  `InputAction`. Clic aplica, clic derecho cancela.
- **R5.4** Colocar objetos reales = generar `CreationDefinition` + `ObjectDefinition` en un job
  (`CreateDefinitions`), en modo temporal para la **vista previa**, y aplicar de verdad al pulsar Build.
  LineTool incluye `m_RandomizationEnabled` y `m_FixedRandomSeed`, que es justo el patrón que necesitamos
  para "mismos candidatos, distinto resultado".
- **R5.5** Los overlays de selección (celdas, polígono) se dibujan con `OverlayRenderSystem` (13/47).
- **R5.6** Antes de añadir una herramienta propia hay que mirar cómo LineTool y Traffic la hacen
  aparecer en la barra de herramientas. Es una **pregunta abierta** (ver `feature_261103`).

## 6. UI

- **R6.1** La UI se comunica con C# por bindings: `ValueBinding<T>` / `GetterValueBinding` (25/47) y
  `TriggerBinding` (24/47), dentro de un `UISystemBase` (25/47) o `ExtendedUISystemBase` (13/47).
- **R6.2** Solo 9 repos llevan su propia UI en TypeScript/React (`package.json`), entre ellos LineTool y
  Traffic. Los demás usan la UI nativa del juego o ajustes. **Opinión:** para un panel flotante con
  listas, filtros y botones, TypeScript/React es lo razonable, pero añade Node y webpack a la cadena.
- **R6.3** Cada vista tiene un `*UISystem` con la lógica y un panel de solo presentación. Find It divide
  su sistema en ficheros parciales (`.Setup`, `.Bindings`, `.Methods`, `.Tags`).

## 7. Integración con Find It (la regla que más nos afecta)

Find It es de código abierto pero **sin licencia** (R1.2), y trae una integración oficial. Hechos:

- **R7.1** Tiene tres APIs documentadas, sin dependencia dura. Se activan exponiendo métodos públicos
  estáticos en nuestra clase `IMod` o en nuestra herramienta, y Find It los encuentra por reflexión.
- **R7.2** **Multi-selección:** si nuestra herramienta (`ToolBaseSystem`) expone `SelectPrefab`,
  `DeselectPrefab` y `GetSelectedPrefabs`, Find It permite Ctrl+clic y Shift+clic y resalta la selección.
  Es la vía **documentada** para llevar edificios elegidos en Find It a nuestra herramienta.
- **R7.3** **Picker:** `SetFindItPrefabSelector(Func<Type,string,Task<PrefabBase>>)` abre el selector de
  Find It y devuelve un prefab. No sirve para "añadir todo".
- **R7.4** **Lista filtrada y etiquetas:** Find It tiene `FindItUtil.GetFilteredPrefabs()` y
  `FindItUtil.AddTag(int id, string tag)` como métodos públicos estáticos, pero **no son una API
  documentada**. Se pueden llamar por reflexión, con `try/catch` y plan B. Pueden cambiar sin avisar.
- **R7.5** **No escribir `CustomPrefabData.json` desde fuera.** Find It lo mantiene en memoria y lo
  reescribe entero al guardar (`SaveCustomPrefabData`), así que nuestros cambios se perderían o se
  corromperían. Las etiquetas se escriben por `AddTag` / `RemoveTag` de Find It, o no se escriben.
- **R7.6** Las etiquetas se normalizan: se quitan espacios y `#`, y los espacios pasan a guiones
  (`NormalizeTag`). Una etiqueta ya existente se reutiliza (comparación sin distinguir mayúsculas), lo que
  permite **adoptar etiquetas existentes** sin esfuerzo extra.
- **R7.7** Find It ya filtra por ancho y profundidad de parcela, nivel, tipo de esquina (`BuilderCornerOption`)
  y zona. Los datos de tamaño, nivel y esquina de un edificio salen de sus clases `Options` y de
  `PrefabIndex`, así que no hay que inventarlos.

## 8. Pruebas y calidad

- **R8.1** Casi ningún mod tiene pruebas (2 proyectos de AlphaGaming7780), con una excepción clara:
  **Platter** tiene `Platter.Tests` (pruebas unitarias de geometría y utilidades de parcelas) y pruebas de
  integración dentro del juego (`VerifyPlatterLoadTest`, `VerifyParcelBehaviorTest`, un `TestRunner` y saves de
  depuración). Es el modelo a imitar.
- **R8.2** Nosotros separamos la lógica pura (filtros con `=`, `!=`, `>`, `<`, densidad, reparto de
  esquinas, semilla) en un proyecto de tests sin dependencias del juego. Lo que toca ECS se verifica en el
  juego, con el log como evidencia.
- **R8.3** Cada funcionalidad se valida en el juego antes de darla por buena, y el resultado se anota.

## 9. Datos del usuario

- **R9.1** Datos propios en `ModsData/<NombreMod>/` (como ya hacemos). Formato JSON con versión de
  esquema, para poder migrar.
- **R9.2** Escritura atómica: se escribe un fichero temporal y se renombra (Find It lo hace así).
- **R9.3** Nunca se tocan datos de otros mods salvo por su API (R7.5).

## 10. Compatibilidad con actualizaciones del juego

- **R10.1** Sin API estable, cada parche del juego puede romper lo que usa tipos internos. Cualquier uso de
  `Game.*` interno se concentra en pocos ficheros, para limitar el daño.
- **R10.2** Toda integración por reflexión comprueba que el método existe, y si no, desactiva la función y
  registra un aviso.

## 11. Aportaciones de Platter y Network Tools (lucarager)

Platter es el mod más cercano al nuestro: trabaja con bloques y celdas de zona de verdad. Es **sin licencia**,
así que solo se lee. Network Tools y LucaModsCommon son MIT.

- **R11.1** Platter **crea sus propios `Block`** para sus parcelas (`P_GenerateZonesSystem`), con su
  componente `ParcelSubBlock`, y los marca para que el juego los trate como zonas normales. Es decir, el
  juego hace crecer edificios en ellos. Para nosotros significa que, al recoger bloques, hay que decidir si
  se incluyen o se ignoran los de Platter (pregunta abierta en el spec).
- **R11.2** Lee los bloques existentes con `Game.Zones.SearchSystem` (`AddSearchTreeReader`) en lugar de
  recorrer todas las entidades. Es la forma correcta de buscar bloques por área.
- **R11.3** Estructura de un mod grande: `Components/`, `Systems/` por tema (`Roads/`, `Tool/`, `Parcels/`,
  `Blocks/`, `UI/`), `Prefabs/`, `Patches/`, `Utils/`, `Constants/`, `L10n/`, `Settings/`. Los sistemas
  heredan de una base propia (`PlatterGameSystemBase`) y los jobs grandes se parten en ficheros parciales
  (`P_RoadConnectionSystem.*Job.cs`).
- **R11.4** Logging que funciona dentro de jobs Burst (`BurstLogger`) y logger con prefijo por sistema
  (`PrefixedLogger`).
- **R11.5** Acceso a campos privados del juego con clases *accessor* en `Patches/Accessors/`, no con
  reflexión dispersa. Los parches Harmony (`ToolSystemPatch`, `MarkerPatches`) están aislados en `Patches/`.
- **R11.6** Interfaz de React/TypeScript con la parte C# del sistema de UI (`P_UISystem`), igual que
  Network Tools. Ambos están descritos por su autor como "C# (Unity ECS, Burst) con UI React/TypeScript".
- **R11.7** Tiene en común con otros autores (agradece a yenyang y krzychu124 en sus créditos): son una
  comunidad pequeña que comparte patrones, así que los nombres y estructuras se repiten entre proyectos.

## 12. Compatibilidad con MapExt2 (Noel-leoN) y otros mods grandes

MapExt2 amplía el mapa hasta 57 km y reescribe sistemas del juego. Es **sin licencia** (solo lectura).

- **R12.1** MapExt aplica Harmony sobre `BaseCellMapSystem` (196 parches), `TerrainSystem` (15),
  `WaterSystem` (6), `TerrainPanelSystem`, `AirwaySystem`, `MenuUISystem`, `SnowSystem`,
  `ServiceCoverageSystem` y los sistemas de agua. Sustituye además sistemas de economía y de demanda
  (`ReBurstSystem`). **No toca zonas, edificios ni herramientas de objetos.**
  **Regla:** no aplicamos Harmony sobre ninguna de esas clases.
- **R12.2** No se asume el tamaño del mapa. Nada de constantes como 14336 m: los límites y las alturas del
  terreno se piden siempre al juego (`TerrainSystem`), porque con MapExt cambian.
- **R12.3** Con MapExt, los extremos del mapa pierden precisión en coma flotante. No hace falta tratarlo en
  v1, pero no debemos fallar si un bloque está lejos del centro.
- **R12.4** MapExt tiene un detector de conflictos en tiempo de carga (`ModConflictDetector`) y avisa en
  pantalla. Nuestro mod no debe entrar en su lista: evitamos tocar `EconomyParameterData`, la demanda, las
  rentas y `BuildingPropertyData`.
- **R12.5** Prueba de compatibilidad obligatoria antes de dar una versión por buena: arrancar el juego con
  MapExt activo en modo 57 km, cargar un save, usar la herramienta en un bloque lejos del centro y mirar el
  log de ambos mods.

## 13. Integración con Find It: lecciones de Shelve It y decisión final

- **R13.1** Find It guarda **un único** gancho de búsqueda (`Filters.GetCustomSearchFunction = ...`): si dos mods
  exponen `GetFindItSearchMethod`, el último que se registra pisa al otro. **Decisión:** no usamos ese gancho.
  Las colecciones se exponen como etiquetas `#nombre` de Find It (`AddTag` / `RemoveTag` por reflexión), que
  Find It gestiona por su cuenta y no chocan con nadie.
- **R13.2** Antes de llamar por reflexión a un método de otro mod, **se valida su forma** (nombre, número y tipo
  de parámetros, tipo de retorno) y se avisa una vez en el log si no coincide. Shelve It lo hace con
  `SearchUtil.SearchCheck`, y es un patrón correcto. Si no coincide, la función se desactiva.
- **R13.3** Los datos de los edificios (tamaño, nivel, esquina) se leen del juego, no de Find It.
- **R13.4** No hay dependencia, importación ni relación con Shelve It. Si se copia código suyo (MIT), se revisa
  línea a línea, se pone atribución en el fichero y se evita todo lo de red y comunidad.

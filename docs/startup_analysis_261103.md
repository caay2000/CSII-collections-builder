# Análisis del arranque lento de Cities: Skylines II

**Fecha:** 2026-11-03
**Estado:** Diagnóstico inicial, a retomar. No se ha cambiado nada en el juego ni en los mods.
**Muestra:** una sola sesión (arranque a las 08:23, partida cargada a las 09:10). Un único arranque no
permite generalizar: hay que repetir la medición antes de tomar decisiones drásticas.

## Cómo se midió
El juego ya escribe marcas de tiempo en `Logs/` (carpeta de datos de usuario del juego, en WSL
`/mnt/c/Users/caay2/AppData/LocalLow/Colossal Order/Cities Skylines II/Logs`). El script
`tools/startup_report.py` genera esta cronología a partir de esos logs, sin tocar nada:

    python3 tools/startup_report.py            # usa la carpeta de logs por defecto
    python3 tools/startup_report.py <carpeta> --gap 30 --top 10

Los logs se sobrescriben en cada arranque, así que **guarda una copia** de `Logs/` (o la salida del script)
antes de cada medición si quieres comparar. `Player-prev.log` (sesión anterior) también sirve de segunda muestra.

## Cronología de la sesión analizada

| Hora | Desde el inicio | Hito |
|---|---|---|
| 08:23:08 | 0 | Primera marca del juego |
| 08:24:13 | 1,1 min | Biblioteca de assets cargada |
| 08:28:30 | 5,4 min | Base de datos de Paradox Mods registrada; empieza la precarga de mods |
| 08:31:24 | 8,3 min | Playset activado (`OnActivePlaysetChanged` tardó 173,6 s); se crean los sistemas ECS |
| 08:35:16 | 12,1 min | Arranca `ExtraAssetsImporter` (EAI) |
| 08:49:50 | 26,7 min | Arranque completado, **menú principal disponible** |
| 08:56:09 | 33,0 min | EAI termina de cargar sus assets |
| 09:09:55 | 46,8 min | Se desactivan dos mods (Map Texture Replacer, Tampa Bay 57 KM) |
| 09:10:17 | 47,1 min | Empieza a cargar la partida |
| 09:10:52 | 47,7 min | **Partida cargada** (35 s) |

El log queda en silencio entre las 08:56 y las 09:09, unos 13 minutos. No se puede distinguir entre "esperando
en el menú" y "juego bloqueado". Si vuelve a pasar, anota la hora de lo que veas en pantalla.

## Datos que apuntan a los culpables
- **`ExtraAssetsImporter`**: su carpeta de datos (`ModsData/ExtraAssetsImporter`) ocupa **8,4 GB en 20.905
  ficheros**. **Corrección:** su propia carga es corta. Su `OnLoad` dura menos de 1 s (08:35:16). Después su log
  calla 14 minutos (08:35 a 08:50), hasta "Failed to relocate database" y "Registered 'EAI' database"
  (08:50:06, en `FileSystem.log`). Su trabajo visible ("Start loading custom stuff") va de 08:51:06 a 08:56:09,
  es decir **5 minutos**. Los 14 minutos de silencio coinciden con la carga general de prefabs del juego: no
  está demostrado cuánto de ese tiempo se debe a EAI (puede ser el juego indexando sus 20.905 ficheros).
  Su log tiene 2.225 líneas informativas y 39 avisos.
- **Dónde se va el tiempo visible de EAI** (estimado repartiendo el tiempo entre líneas `RenderPrefab`, solo
  esos 5 minutos): Wall Decor Comics-Movie-Saga 9,3 %, Grates and Covers 8,2 %, Farming Surfaces & Decals
  7,6 %, Edges and Borders 7,4 %, RealVision Decals 7,2 %, Doors and Windows 6,0 %, Creator Graffiti 5,0 %.
- **Carga de prefabs de los mods**: entre 08:31 y 08:49 (18 minutos) el juego crea sistemas y carga prefabs.
  En esa fase hay **3.354 avisos "missing reference"** (`FileSystem.log`) y **306 "Duplicate prefab ID"**
  (`SceneFlow.log`), lo que apunta a packs de assets rotos, repetidos o que se pisan entre sí.
- **Antes de los mods (5,4 min) y activación del playset (173 s)**: coherente con una caché de mods grande.
  La carpeta `.cache` ocupa **80 GB**.
- Otras carpetas grandes: `AssetIconLibrary` 690 MB, `Skyve` 386 MB, `CustomZones` 97 MB, `PO2` 71 MB.
- Mods con más actividad de log en la primera hora: `I18NEverywhere` (1.204 líneas, 35 min activo),
  `Better Transit Selector` (976 líneas, 36 min), `UI.log` (416 líneas, 47 min).
- **La partida no es el cuello de botella**: 35 s.
- **`BuildingCollections` no influye**: su `OnLoad` y la carga de colecciones tardan menos de 0,2 s.

## Veredicto
El arranque está dominado por la **carga de mods de assets**, no por la partida ni por mods pequeños. De los
33 minutos que hay hasta tener todo cargado, `ExtraAssetsImporter` tiene **5 minutos de trabajo demostrado** y
hay 18 de carga de prefabs de todos los mods, de los que no se puede aislar su parte. Tampoco es descartable el
coste del playset y de los packs con referencias rotas. Es una conclusión **provisional**: sale de un solo arranque y de correlaciones en los logs, no de una prueba
controlada. La forma de confirmarla es medir desactivando cosas, una a la vez.

## Siguientes pasos propuestos (nada hecho todavía)
1. **Segunda muestra**: ejecutar el script sobre `Player-prev.log` y los logs del siguiente arranque, para ver
   si la cronología se repite (y si los ">20 min" que mencionas coinciden).
2. **Prueba de EAI**: arrancar con `ExtraAssetsImporter` desactivado (o con menos packs) y comparar el tiempo
   hasta el menú. Es el sospechoso principal, pero solo esta prueba dirá cuánto pesa de verdad.
3. **Limpiar avisos**: listar qué packs generan los "missing reference" y los "Duplicate prefab ID"
   (aparecen con el nombre del pack en los logs) y quitar los duplicados o rotos.
4. **Caché**: listar qué ocupa los 80 GB de `.cache` y qué mods ya no están en el playset, para decidir qué
   limpiar. No borrar nada sin comprobar.
5. **Segundo script, opcional**: un resumen por mod con su tiempo de carga y avisos, si hace falta más detalle.
6. Comparar antes y después con `tools/startup_report.py` y guardar los informes con fecha.

## Lo que NO se sabe
- Qué hizo el juego entre las 08:56 y las 09:09.
- Si el tiempo de EAI es proporcional a los 8,4 GB o por ficheros (20.905).
- Si la lentitud es siempre igual o varía con el contenido del playset (comentas que a veces pasa de 20 min).
- Si el disco es SSD o HDD (la partición tiene 0,9 TB; no he mirado el tipo).

# Spike: zonas, celdas y colocación de edificios

**Fecha:** 2026-11-03
**Fuente:** descompilación de `Game.dll` (solo para entender la API del juego) con `ilspycmd` 8.2, más los repos
de referencia de `docs/implementation_rules.md`. Estado: **parcial**. El modelo de datos está claro, pero la
colocación real y la barra de herramientas no se han probado en el juego.

## Confirmado en el DLL

- **Bloque** (`Game.Zones.Block`): `m_Position` (float3), `m_Direction` (float2, hacia dónde "mira" el bloque,
  o sea la calle) y `m_Size` (int2, celdas de ancho y fondo). Cada bloque tiene un `DynamicBuffer<Cell>`.
- **Celda** (`Game.Zones.Cell`): `m_State` (`CellFlags`), `m_Zone` y `m_Height`.
- **`CellFlags`** útiles: `Blocked`, `Occupied`, `Roadside`, `Overridden`, y **`RoadLeft`, `RoadRight`,
  `RoadBack`**, que indican de qué lados toca una celda a una calle.
- **`ValidArea`**: `int4` con el rectángulo de celdas válido de un bloque.
- **`ZoneUtils`**: `CELL_SIZE = 8` (metros), `MAX_ZONE_WIDTH = 10`, `MAX_ZONE_DEPTH = 6`, y utilidades listas:
  `GetPosition(block, min, max)`, `GetRotation(block)`, `GetCellIndex`, `GetCellPosition`,
  `CalculateCorners`, `IsNeighbor`, `CanShareCells`.
- **Tamaño del edificio**: `BuildingData.m_LotSize` (int2, ancho x fondo en celdas), también
  `BuildingPrefab.m_LotWidth` / `m_LotDepth`.
- **Nivel**: `SpawnableBuildingData.m_Level` (byte), y su zona en `m_ZonePrefab`.
- **Esquinero**: `BuildingPrefab.m_AccessType` (`BuildingAccessType`): `Front`, `LeftCorner`, `RightCorner`,
  `LeftAndRightCorner`, `LeftAndBackCorner`, `RightAndBackCorner`, `FrontAndBack`, `All`, `OnRoad`,
  `OnRoadArea`. Find It deriva de aquí su filtro de esquina (Izquierda / Frente / Derecha).
- **Colocación**: las herramientas generan entidades temporales con `CreationDefinition` (prefab, owner,
  flags, `m_RandomSeed`) y `ObjectDefinition` (posición, rotación, escala), como hace LineTool.

## Consecuencias para el diseño

1. **No hace falta geometría propia.** Selección de bloque = recoger los `Block` cuyo contorno cae en la
   zona elegida (`CalculateCorners` / `CalculateBounds`) y trabajar con su `Cell` buffer.
2. **Detección de esquina barata**: una celda es de esquina si tiene `RoadLeft` o `RoadRight` además del lado
   frontal. Un edificio solo encaja en una esquina si su `m_AccessType` lo permite. Esto confirma la regla
   "esquineros en las esquinas".
3. **Los filtros de tamaño y nivel salen de datos del prefab** (`m_LotSize`, `m_Level`), no hay que inventar
   nada, y el filtro `=`, `!=`, `>`, `<` es lógica pura, comprobable en tests.
4. **Tamaño "> 2" en edificios no cuadrados**: queda decidido que se compara por separado el ancho
   (`m_LotSize.x`) y el fondo (`m_LotSize.y`), igual que Find It con "Lot Width" y "Lot Depth".
5. **Densidad = fracción de celdas `Free` cubiertas.** Una celda `Occupied` o `Blocked` no cuenta.

## Sin resolver (hay que probarlo en el juego)

- Cómo se coloca exactamente un edificio **zonificable (growable)** sin pintar zona. El mod Plop the
  Growables demuestra que se puede, pero el camino concreto (`ObjectToolSystem` con `CreationDefinition`)
  hay que verificarlo con un prototipo.
- Cómo aparece nuestra herramienta en la barra del juego (LineTool usa Harmony).
- Si hay que marcar las celdas como `Occupied` a mano o lo hace el juego al crear el edificio.
- Cómo se enlaza la lista de Find It con `GetFilteredPrefabs` por reflexión (la firma exacta sale de su
  código, pero no se ha probado contra el DLL instalado del usuario).

## Siguiente paso propuesto
Prototipo mínimo en el juego: una herramienta vacía que se activa, lee los `Block` bajo el cursor, resalta sus
celdas y coloca **un** edificio fijo en una celda. Con eso se cierran las cuatro dudas de arriba antes de
construir el resto.

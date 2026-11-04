using System;
using System.IO;
using Colossal.PSI.Environment;
using Newtonsoft.Json;

namespace BuildingCollections
{
    /// <summary>
    /// Parámetros dudosos que se pueden corregir sin recompilar ni reiniciar el juego: se releen de
    /// <c>ModsData/BuildingCollections/dev.json</c> en cada acción. Si el fichero no existe se escribe con los valores
    /// por defecto para que sea fácil de editar.
    /// </summary>
    public sealed class DevConfig
    {
        /// <summary>
        /// Holgura (m) sobre los 8 m entre centros de celdas contiguas para que dos bloques sean de la misma manzana.
        /// Debe ser menor que el ancho de la calle más estrecha (una calle en medio los separa 16 m o más).
        /// </summary>
        public double BlockTouchTolerance = 4.0;

        /// <summary>Margen (m) fuera de la celda en el que se acepta el clic. 0 = tiene que caer dentro de una celda.</summary>
        public double ClickSnapDistance = 0;

        /// <summary>Radio (m) alrededor del clic en el que se buscan bloques. Una manzana más grande queda cortada.</summary>
        public double SearchRadius = 400.0;

        /// <summary>Modo bloque: seleccionar todo lo que queda dentro de las calles que rodean el clic (como Street Builder).</summary>
        public bool UseRoadFace = true;

        /// <summary>
        /// Modo bloque: parcelas propias a lo largo de la acera (como Street Builder), sin depender de las celdas de
        /// zona; llena también diagonales y esquinas. false = solo celdas de zona del juego.
        /// </summary>
        public bool UseVirtualLots = true;

        /// <summary>Radio (m) alrededor del clic en el que se leen las calles para cerrar la manzana.</summary>
        public double RoadSearchRadius = 1500.0;

        /// <summary>
        /// Con Anarchy encendido: cuánto (m) pueden pisarse dos edificios de calles distintas. Es un tope: el solape
        /// real es el que dé la geometría, entre 0 y este valor.
        /// </summary>
        public double AnarchyOverlap = 0.5;

        /// <summary>Intercambia izquierda y derecha al leer m_AccessType, por si el criterio del juego es el contrario.</summary>
        public bool SwapCornerSides = false;

        /// <summary>No construir si alguna entidad de la vista previa tiene error (como hace el juego).</summary>
        public bool RequireNoErrors = true;

        /// <summary>Suma a la altura el m_PlacementOffset.y del prefab (como ZoneSpawnSystem).</summary>
        public bool UsePlacementOffset = true;

        /// <summary>Mapa de celdas de cada bloque en el log al seleccionar.</summary>
        public bool LogCells = true;

        /// <summary>Fotogramas tras construir al cabo de los cuales se registra el estado de los edificios.</summary>
        public int ReportAfterFrames = 1800;

        static string FilePath => Path.Combine(EnvPath.kUserDataPath, "ModsData", "BuildingCollections", "dev.json");

        public static DevConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    return JsonConvert.DeserializeObject<DevConfig>(File.ReadAllText(FilePath)) ?? new DevConfig();
                }

                var d = new DevConfig();
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(d, Formatting.Indented));
                return d;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("dev.json no válido, se usan valores por defecto: " + e.Message);
                return new DevConfig();
            }
        }
    }
}

using System;
using System.Linq;
using System.Reflection;
using Unity.Entities;

namespace BuildingCollections
{
    /// <summary>
    /// Integración opcional con Anarchy (sin dependencia: todo por reflexión). Anarchy desactiva los errores de las
    /// herramientas de su lista cuando está encendido; <c>AnarchyUISystem.TryAddTool(toolID)</c> permite añadir la
    /// nuestra, y <c>AnarchyEnabled</c> dice si está encendido. Si Anarchy no está, todo devuelve "apagado".
    /// </summary>
    public static class AnarchyBridge
    {
        const string SystemType = "Anarchy.Systems.Common.AnarchyUISystem";

        static object system;
        static PropertyInfo enabledProperty;
        static int attempts;
        static bool registered;

        /// <summary>Anarchy instalado y nuestra herramienta añadida a su lista.</summary>
        public static bool Available => registered;

        /// <summary>Anarchy instalado, con nuestra herramienta registrada, y encendido.</summary>
        public static bool Enabled
        {
            get
            {
                if (!registered || enabledProperty == null)
                {
                    return false;
                }

                try { return (bool)enabledProperty.GetValue(system); }
                catch { return false; }
            }
        }

        /// <summary>
        /// Intenta registrar la herramienta. Anarchy puede crear su sistema después que nosotros, así que se llama
        /// periódicamente hasta que funcione (o se rinde tras unos intentos si Anarchy no está).
        /// </summary>
        public static void TryRegister(World world, string toolID)
        {
            if (registered || attempts >= 20)
            {
                return;
            }

            attempts++;
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(SystemType, false))
                    .FirstOrDefault(t => t != null);
                if (type == null)
                {
                    return;
                }

                system = world.GetExistingSystemManaged(type);
                if (system == null)
                {
                    return;
                }

                var add = type.GetMethod("TryAddTool", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                enabledProperty = type.GetProperty("AnarchyEnabled", BindingFlags.Public | BindingFlags.Instance);
                if (add == null || enabledProperty == null)
                {
                    Mod.Log.Warn("Anarchy encontrado, pero sin TryAddTool / AnarchyEnabled: integración desactivada.");
                    attempts = int.MaxValue;
                    return;
                }

                add.Invoke(system, new object[] { toolID });
                registered = true;
                Mod.Log.Info($"Anarchy: herramienta '{toolID}' registrada.");
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Anarchy: no se pudo registrar la herramienta: " + e.Message);
            }
        }
    }
}

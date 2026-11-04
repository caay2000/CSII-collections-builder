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
        static World systemWorld;
        static PropertyInfo enabledProperty;
        static int typeLookups;
        static bool registered;
        static bool unsupported;

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
            // Partida nueva (otro World): hay que volver a registrar en el sistema de ese mundo.
            if (registered && systemWorld != world)
            {
                registered = false;
                system = null;
            }

            if (registered || unsupported || world == null)
            {
                return;
            }

            try
            {
                // El tipo está o no está desde el principio (los ensamblados se cargan antes que el mundo): pocos
                // intentos. El sistema, en cambio, puede crearse tarde: se sigue probando sin tope.
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => { try { return a.GetType(SystemType, false); } catch { return null; } })
                    .FirstOrDefault(t => t != null);
                if (type == null)
                {
                    unsupported = ++typeLookups >= 3;
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
                    unsupported = true;
                    return;
                }

                // TryAddTool devuelve false si la herramienta ya estaba en la lista: también vale.
                var result = add.Invoke(system, new object[] { toolID });
                registered = true;
                systemWorld = world;
                Mod.Log.Info($"Anarchy: herramienta '{toolID}' registrada ({(result is bool b && !b ? "ya estaba" : "añadida")}).");
            }
            catch (Exception e)
            {
                var inner = (e as TargetInvocationException)?.InnerException ?? e;
                Mod.Log.Warn("Anarchy: no se pudo registrar la herramienta: " + inner.Message);
                unsupported = true;
            }
        }
    }
}

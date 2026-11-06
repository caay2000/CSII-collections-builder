using System;
using System.Linq;
using System.Reflection;
using Unity.Entities;

namespace BuildingCollections
{
    public static class AnarchyBridge
    {
        const string SystemTypeName = "Anarchy.Systems.Common.AnarchyUISystem";
        const int MaxTypeLookups = 3;

        static Type systemType;
        static object system;
        static World systemWorld;
        static PropertyInfo enabledProperty;
        static MethodInfo toggleMethod;
        static int typeLookups;
        static bool registered;
        static bool unsupported;

        public static bool Available => registered;

        public static bool CanSwitch => registered && toggleMethod != null;

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

        public static bool SetEnabled(bool on)
        {
            if (!CanSwitch)
            {
                return false;
            }

            if (Enabled == on)
            {
                return true;
            }

            try
            {
                toggleMethod.Invoke(system, null);
            }
            catch (Exception e)
            {
                var inner = (e as TargetInvocationException)?.InnerException ?? e;
                Mod.Log.Warn("Anarchy: could not be switched: " + inner.Message);
                return false;
            }

            return Enabled == on;
        }

        public static void TryRegister(World world, string toolID)
        {
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
                if (systemType == null)
                {
                    systemType = FindType();
                    if (systemType == null)
                    {
                        unsupported = ++typeLookups >= MaxTypeLookups;
                        return;
                    }
                }

                system = world.GetExistingSystemManaged(systemType);
                if (system == null)
                {
                    return;
                }

                var add = systemType.GetMethod("TryAddTool", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                enabledProperty = systemType.GetProperty("AnarchyEnabled", BindingFlags.Public | BindingFlags.Instance);
                toggleMethod = systemType.GetMethod("AnarchyToggled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (add == null || enabledProperty == null)
                {
                    Mod.Log.Warn("Anarchy found, but without TryAddTool / AnarchyEnabled: integration disabled.");
                    unsupported = true;
                    return;
                }

                if (toggleMethod == null)
                {
                    Mod.Log.Warn("Anarchy found, but without AnarchyToggled: it cannot be turned on automatically.");
                }

                var result = add.Invoke(system, new object[] { toolID });
                registered = true;
                systemWorld = world;
                Mod.Log.Info($"Anarchy: tool '{toolID}' registered ({(result is bool b && !b ? "already there" : "added")}).");
            }
            catch (Exception e)
            {
                var inner = (e as TargetInvocationException)?.InnerException ?? e;
                Mod.Log.Warn("Anarchy: could not register the tool: " + inner.Message);
                unsupported = true;
            }
        }

        static Type FindType() => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => { try { return a.GetType(SystemTypeName, false); } catch { return null; } })
            .FirstOrDefault(t => t != null);
    }
}

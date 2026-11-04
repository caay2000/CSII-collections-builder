using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;

namespace BuildingCollections
{
    /// <summary>Acciones sobre colecciones que combinan nuestro almacén con Find It.</summary>
    public static class CollectionService
    {
        public static string Create(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return "El nombre está vacío.";
            }

            if (CollectionStore.Find(name) != null)
            {
                return $"Ya existe la colección '{name}'.";
            }

            CollectionStore.Collections.Add(new BuildingCollection { Name = name, Tag = CollectionLogic.NormalizeTag(name) });
            CollectionStore.Save();
            Mod.Log.Info($"Colección '{name}' creada (etiqueta #{CollectionLogic.NormalizeTag(name)}).");
            return null;
        }

        /// <summary>Suma a la colección todos los edificios de la lista filtrada de Find It, y los etiqueta.</summary>
        public static string AddAllFromFindIt(string collectionName)
        {
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No hay colección seleccionada.";
            }

            if (!FindItBridge.IsAvailable)
            {
                return "Find It no está instalado o activo.";
            }

            var filtered = FindItBridge.GetFilteredBuildings();
            if (filtered.Count == 0)
            {
                return "Find It no devuelve edificios con el filtro actual.";
            }

            int added = CollectionLogic.AddRange(c.Prefabs, filtered.Select(f => f.Info.PrefabName));
            FindItBridge.RefreshIds();
            foreach (var f in filtered)
            {
                TagQueue.Add(f.Info.PrefabName, c.Tag);
            }

            CollectionStore.Save();
            Mod.Log.Info($"'{c.Name}': {filtered.Count} en Find It, {added} nuevos; {filtered.Count} etiquetas en cola. Total {c.Prefabs.Count}.");
            return null;
        }

        /// <summary>Añade un edificio suelto (Shift+clic) y lo etiqueta en Find It si está. Devuelve null si todo fue bien.</summary>
        public static string AddPrefab(string collectionName, string prefabName)
        {
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No hay colección seleccionada.";
            }

            if (CollectionLogic.AddRange(c.Prefabs, new[] { prefabName }) == 0)
            {
                return $"'{prefabName}' ya estaba en '{c.Name}'.";
            }

            if (!FindItBridge.TryGetId(prefabName, out _))
            {
                FindItBridge.RefreshIds();
            }

            TagQueue.Add(prefabName, c.Tag);
            CollectionStore.Save();
            Mod.Log.Info($"'{c.Name}': añadido '{prefabName}'. Total {c.Prefabs.Count}.");
            return null;
        }

        /// <summary>Quita edificios sueltos de la colección y su etiqueta de Find It.</summary>
        public static string RemoveBuildings(string collectionName, IEnumerable<string> prefabNames)
        {
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No hay colección seleccionada.";
            }

            int removed = 0;
            FindItBridge.RefreshIds();
            foreach (var name in prefabNames.Where(n => !string.IsNullOrEmpty(n)).ToList())
            {
                if (CollectionLogic.Remove(c.Prefabs, name))
                {
                    removed++;
                    TagQueue.Remove(name, c.Tag);
                }
            }

            CollectionStore.Save();
            Mod.Log.Info($"'{c.Name}': quitados {removed}. Total {c.Prefabs.Count}.");
            return null;
        }

        /// <summary>Quita todos los edificios de la colección y su etiqueta de Find It.</summary>
        public static string Clear(string collectionName)
        {
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No hay colección seleccionada.";
            }

            RemoveBuildings(collectionName, c.Prefabs.ToList());
            Mod.Log.Info($"Colección '{c.Name}' vaciada.");
            return null;
        }

        /// <summary>Cambia el nombre de la colección y su etiqueta de Find It (quita la vieja y pone la nueva).</summary>
        public static string Rename(string collectionName, string newName, out string finalName)
        {
            finalName = null;
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No hay colección seleccionada.";
            }

            newName = (newName ?? string.Empty).Trim();
            if (newName.Length == 0)
            {
                return "El nombre está vacío.";
            }

            var other = CollectionStore.Find(newName);
            if (other != null && other != c)
            {
                return $"Ya existe la colección '{newName}'.";
            }

            string oldName = c.Name, oldTag = c.Tag, newTag = CollectionLogic.NormalizeTag(newName);
            c.Name = newName;
            c.Tag = newTag;
            if (!string.Equals(oldTag, newTag, System.StringComparison.OrdinalIgnoreCase))
            {
                FindItBridge.RefreshIds();
                foreach (var p in c.Prefabs)
                {
                    TagQueue.Remove(p, oldTag);
                    TagQueue.Add(p, newTag);
                }
            }

            CollectionStore.Save();
            finalName = newName;
            Mod.Log.Info($"Colección '{oldName}' renombrada a '{newName}' (etiqueta #{oldTag} -> #{newTag}).");
            return null;
        }

        /// <summary>Copia la colección con un nombre nuevo ("X copy", "X copy 2"...) y su propia etiqueta.</summary>
        public static string Clone(string collectionName, out string newName)
        {
            newName = null;
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No hay colección seleccionada.";
            }

            newName = c.Name + " copy";
            for (int n = 2; CollectionStore.Find(newName) != null; n++)
            {
                newName = $"{c.Name} copy {n}";
            }

            var copy = new BuildingCollection { Name = newName, Tag = CollectionLogic.NormalizeTag(newName) };
            CollectionLogic.AddRange(copy.Prefabs, c.Prefabs);
            CollectionStore.Collections.Add(copy);
            FindItBridge.RefreshIds();
            foreach (var p in copy.Prefabs)
            {
                TagQueue.Add(p, copy.Tag);
            }

            CollectionStore.Save();
            Mod.Log.Info($"Colección '{c.Name}' clonada como '{newName}' ({copy.Prefabs.Count} edificios).");
            return null;
        }

        public static string Delete(string collectionName)
        {
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No hay colección seleccionada.";
            }

            Clear(collectionName);
            CollectionStore.Collections.Remove(c);
            CollectionStore.Save();
            Mod.Log.Info($"Colección '{c.Name}' borrada.");
            return null;
        }
    }
}

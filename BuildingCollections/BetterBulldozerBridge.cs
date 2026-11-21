using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Prefabs;
using Unity.Entities;

namespace BuildingCollections
{
    public static class BetterBulldozerBridge
    {
        const string BufferTypeName = "Better_Bulldozer.Components.PermanentlyRemovedSubElementPrefab";
        const string RecordTypeName = "Better_Bulldozer.Components.OwnerRecord";
        const string RecordField = "m_RecordEntity";

        static bool resolved;
        static MethodInfo remember;
        static ConstructorInfo bufferConstructor;
        static ConstructorInfo recordConstructor;
        static FieldInfo recordField;

        public static bool Available
        {
            get
            {
                Resolve();
                return remember != null;
            }
        }

        public static void Remember(EntityManager entities, Entity owner, IReadOnlyCollection<Entity> prefabs)
        {
            if (Available && prefabs.Count > 0)
            {
                remember.Invoke(null, new object[] { entities, owner, prefabs });
            }
        }

        static void Add<TBuffer, TRecord>(EntityManager entities, Entity owner, IReadOnlyCollection<Entity> prefabs)
            where TBuffer : unmanaged, IBufferElementData
            where TRecord : unmanaged, IComponentData
        {
            if (!entities.HasBuffer<TBuffer>(owner))
            {
                entities.AddBuffer<TBuffer>(owner);
            }

            var known = new HashSet<Entity>();
            var buffer = entities.GetBuffer<TBuffer>(owner, true);
            for (int i = 0; i < buffer.Length; i++)
            {
                var record = (Entity)recordField.GetValue(buffer[i]);
                if (entities.Exists(record) && entities.HasComponent<PrefabRef>(record))
                {
                    known.Add(entities.GetComponentData<PrefabRef>(record).m_Prefab);
                }
            }

            foreach (var prefab in prefabs.Where(p => !known.Contains(p)))
            {
                var record = entities.CreateEntity();
                entities.AddComponentData(record, (TRecord)recordConstructor.Invoke(new object[] { owner }));
                entities.AddComponentData(record, new PrefabRef { m_Prefab = prefab });
                entities.GetBuffer<TBuffer>(owner).Add((TBuffer)bufferConstructor.Invoke(new object[] { record }));
            }
        }

        static void Resolve()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            try
            {
                var bufferType = FindType(BufferTypeName);
                var recordType = FindType(RecordTypeName);
                var field = bufferType?.GetField(RecordField, BindingFlags.Public | BindingFlags.Instance);
                var bufferCtor = bufferType?.GetConstructor(new[] { typeof(Entity) });
                var recordCtor = recordType?.GetConstructor(new[] { typeof(Entity) });
                if (field?.FieldType != typeof(Entity) || bufferCtor == null || recordCtor == null
                    || !typeof(IBufferElementData).IsAssignableFrom(bufferType) || !typeof(IComponentData).IsAssignableFrom(recordType))
                {
                    Mod.Log.Info("Better Bulldozer not found or not compatible: parcel limits are unavailable.");
                    return;
                }

                recordField = field;
                bufferConstructor = bufferCtor;
                recordConstructor = recordCtor;
                remember = typeof(BetterBulldozerBridge).GetMethod(nameof(Add), BindingFlags.NonPublic | BindingFlags.Static)
                    .MakeGenericMethod(bufferType, recordType);
                Mod.Log.Info("Better Bulldozer found: parcel limits are available.");
            }
            catch (Exception e)
            {
                remember = null;
                Mod.Log.Warn("Better Bulldozer could not be linked: " + e.Message);
            }
        }

        static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => { try { return a.GetType(name, false); } catch { return null; } })
            .FirstOrDefault(t => t != null);
    }
}

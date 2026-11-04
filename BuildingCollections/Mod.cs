using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using BuildingCollections.Systems;

namespace BuildingCollections
{
    public class Mod : IMod
    {
        public static ILog Log = LogManager.GetLogger(nameof(BuildingCollections)).SetShowsErrorsInUI(false);

        public static BuildingCollectionsSettings Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info(nameof(OnLoad));
            try
            {
                CollectionStore.Load();
                RegisterUIAssets();

                Settings = new BuildingCollectionsSettings(this);
                Settings.RegisterInOptionsUI();
                Settings.RegisterKeyBindings();
                GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
                AssetDatabase.global.LoadSettings(nameof(BuildingCollections), Settings, new BuildingCollectionsSettings(this));

                updateSystem.UpdateAt<BlockBuilderToolSystem>(SystemUpdatePhase.ToolUpdate);
                updateSystem.UpdateAt<BuiltGuardSystem>(SystemUpdatePhase.ModificationEnd);
                updateSystem.UpdateAt<ToolActivatorSystem>(SystemUpdatePhase.UIUpdate);
                updateSystem.UpdateAt<BcUISystem>(SystemUpdatePhase.UIUpdate);
            }
            catch (System.Exception e)
            {
                Log.Error(e);
            }
        }

        /// <summary>Publica la carpeta del mod como coui://buildingcollections/ para servir el icono del botón.</summary>
        void RegisterUIAssets()
        {
            try
            {
                if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                {
                    var dir = System.IO.Path.GetDirectoryName(asset.path);
                    Colossal.UI.UIManager.defaultUISystem?.AddHostLocation("buildingcollections", dir, false);
                    Log.Info("Recursos de UI en coui://buildingcollections/ -> " + dir);
                }
                else
                {
                    Log.Warn("No se encontró la carpeta del mod; el icono usará el valor por defecto.");
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("No se pudo registrar la carpeta de UI: " + e.Message);
            }
        }

        public void OnDispose()
        {
            Log.Info(nameof(OnDispose));
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }

            CollectionStore.Save();
        }
    }
}

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

        public static string Version { get; } = typeof(Mod).Assembly.GetName().Version is System.Version v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"{nameof(OnLoad)} v{Version}");
            try
            {
                CollectionStore.Load();
                RegisterUIAssets();

                Settings = new BuildingCollectionsSettings(this);
                Settings.RegisterInOptionsUI();
                Settings.RegisterKeyBindings();
                GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
                AssetDatabase.global.LoadSettings(nameof(BuildingCollections), Settings, new BuildingCollectionsSettings(this));
                Settings.ChooseFindItTagsOnce();
                Settings.Loaded = true;
                Log.Info($"Find It tags: {(Settings.FindItTags ? "enabled" : "disabled")}.");

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

        void RegisterUIAssets()
        {
            try
            {
                if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                {
                    var dir = System.IO.Path.GetDirectoryName(asset.path);
                    Colossal.UI.UIManager.defaultUISystem?.AddHostLocation("buildingcollections", dir, false);
                    Log.Info("UI resources at coui://buildingcollections/ -> " + dir);
                }
                else
                {
                    Log.Warn("Mod folder not found; the icon will use the default.");
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("Could not register the UI folder: " + e.Message);
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

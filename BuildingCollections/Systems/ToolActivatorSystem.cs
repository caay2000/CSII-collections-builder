using System;
using Game;
using Game.Input;

namespace BuildingCollections.Systems
{
    public partial class ToolActivatorSystem : GameSystemBase
    {
        BcUISystem ui;
        (ProxyAction action, Action run)[] shortcuts;

        protected override void OnCreate()
        {
            base.OnCreate();
            ui = World.GetOrCreateSystemManaged<BcUISystem>();
            shortcuts = new (ProxyAction, Action)[]
            {
                (Enable(BuildingCollectionsSettings.ActivateAction), ui.TogglePanel),
                (Enable(BuildingCollectionsSettings.ReshuffleAction), ui.ReshufflePreview),
                (Enable(BuildingCollectionsSettings.BuildAction), ui.BuildPreview),
            };
        }

        static ProxyAction Enable(string name)
        {
            var action = Mod.Settings?.GetAction(name);
            if (action != null)
            {
                action.shouldBeEnabled = true;
            }

            return action;
        }

        protected override void OnUpdate()
        {
            foreach (var (action, run) in shortcuts)
            {
                if (action != null && action.WasPressedThisFrame())
                {
                    run();
                    return;
                }
            }
        }
    }
}

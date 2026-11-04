using Game;
using Game.Input;

namespace BuildingCollections.Systems
{
    /// <summary>
    /// Atajo (Ctrl+B por defecto): abre o cierra el panel. La herramienta la activa la pestaña Builder al abrirse, así
    /// que el atajo nunca deja la herramienta encendida sin panel.
    /// </summary>
    public partial class ToolActivatorSystem : GameSystemBase
    {
        BcUISystem ui;
        ProxyAction action;

        protected override void OnCreate()
        {
            base.OnCreate();
            ui = World.GetOrCreateSystemManaged<BcUISystem>();
            action = Mod.Settings?.GetAction(BuildingCollectionsSettings.ActivateAction);
            if (action != null)
            {
                action.shouldBeEnabled = true;
            }
        }

        protected override void OnUpdate()
        {
            if (action != null && action.WasPressedThisFrame())
            {
                ui.TogglePanel();
            }
        }
    }
}

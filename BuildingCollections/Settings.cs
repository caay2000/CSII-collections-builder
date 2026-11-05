using System.Linq;
using BuildingCollections.Core;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;

namespace BuildingCollections
{
    /// <summary>
    /// Página de ajustes del mod. En el prototipo hace de panel: gestión de colecciones y filtros del constructor.
    /// El panel flotante propio (React/TypeScript) llega después de validar la mecánica.
    /// </summary>
    [FileLocation(nameof(BuildingCollections))]
    [SettingsUITabOrder(Section, Keys)]
    [SettingsUIGroupOrder(Collections, Builder, Shortcuts)]
    [SettingsUIShowGroupName(Collections, Builder, Shortcuts)]
    public class BuildingCollectionsSettings : ModSetting
    {
        public const string Section = "Main";
        public const string Collections = "Collections";
        public const string Builder = "Builder";
        public const string Keys = "KeyBindings";
        public const string Shortcuts = "Shortcuts";
        public const string ActivateAction = "BuildingCollectionsActivate";

        public BuildingCollectionsSettings(IMod mod) : base(mod) { }

        string lastMessage = string.Empty;

        // ---- Colecciones ----

        [SettingsUIDropdown(typeof(BuildingCollectionsSettings), nameof(GetCollectionItems))]
        [SettingsUIHidden]
        [SettingsUISection(Section, Collections)]
        public string SelectedCollection { get; set; } = string.Empty;

        public DropdownItem<string>[] GetCollectionItems()
        {
            var items = CollectionStore.Collections
                .Select(c => new DropdownItem<string> { value = c.Name, displayName = $"{c.Name} ({c.Prefabs.Count})" })
                .ToList();
            if (items.Count == 0)
            {
                items.Add(new DropdownItem<string> { value = string.Empty, displayName = "—" });
            }

            return items.ToArray();
        }

        [SettingsUITextInput]
        [SettingsUIHidden]
        [SettingsUISection(Section, Collections)]
        public string NewCollectionName { get; set; } = string.Empty;

        [SettingsUIButton]
        [SettingsUIHidden]
        [SettingsUISection(Section, Collections)]
        public bool CreateCollection
        {
            set
            {
                Report(CollectionService.Create(NewCollectionName));
                if (CollectionStore.Find(NewCollectionName) != null)
                {
                    SelectedCollection = CollectionStore.Find(NewCollectionName).Name;
                    NewCollectionName = string.Empty;
                }
            }
        }

        [SettingsUIButton]
        [SettingsUIHidden]
        [SettingsUISection(Section, Collections)]
        public bool AddAllFromFindIt { set => Report(CollectionService.AddAllFromFindIt(SelectedCollection)); }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUIHidden]
        [SettingsUISection(Section, Collections)]
        public bool ClearCollection { set => Report(CollectionService.Clear(SelectedCollection)); }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUIHidden]
        [SettingsUISection(Section, Collections)]
        public bool DeleteCollection
        {
            set
            {
                Report(CollectionService.Delete(SelectedCollection));
                SelectedCollection = CollectionStore.Collections.FirstOrDefault()?.Name ?? string.Empty;
            }
        }

        [SettingsUIHidden]
        [SettingsUISection(Section, Collections)]
        public string Status => lastMessage;

        bool findItTags;

        /// <summary>Los ajustes ya se han leído del fichero: a partir de aquí un cambio de FindItTags es del usuario.</summary>
        internal bool Loaded;

        /// <summary>
        /// Etiquetar en Find It los edificios de cada colección (#nombre). Apagada por defecto. Al encenderla se ponen
        /// las de todas las colecciones (en bloque, un solo guardado de Find It; con muchas puede congelar el juego
        /// un momento, por eso el aviso). Al apagarla no se quita nada: las que hay se quedan.
        /// </summary>
        [SettingsUISection(Section, Collections)]
        public bool FindItTags
        {
            get => findItTags;
            set
            {
                bool turnedOn = Loaded && value && !findItTags;
                findItTags = value;
                if (turnedOn)
                {
                    CollectionService.SyncFindItTags();
                }
            }
        }

        /// <summary>Texto bajo la casilla: encenderla etiqueta todas las colecciones y puede parar el juego un momento.</summary>
        [SettingsUIMultilineText]
        [SettingsUISection(Section, Collections)]
        public string FindItTagsNote => string.Empty;

        /// <summary>Ya se decidió el valor inicial de FindItTags (migración de la versión en que siempre etiquetaba).</summary>
        [SettingsUIHidden]
        [SettingsUISection(Section, Collections)]
        public bool FindItTagsChosen { get; set; }

        /// <summary>
        /// Primera carga con esta opción: quien ya tenía colecciones las tenía etiquetadas (antes siempre se hacía), así
        /// que se deja encendida; una instalación nueva empieza apagada. Sin etiquetar nada: solo fija el valor.
        /// </summary>
        internal void ChooseFindItTagsOnce()
        {
            if (FindItTagsChosen)
            {
                return;
            }

            findItTags = CollectionStore.Collections.Count > 0;
            FindItTagsChosen = true;
            ApplyAndSave();
            Mod.Log.Info($"Etiquetas de Find It: primera vez con la opción, {(findItTags ? "encendida (ya había colecciones etiquetadas)" : "apagada")}.");
        }

        [SettingsUIKeyboardBinding(BindingKeyboard.B, ActivateAction, ctrl: true)]
        [SettingsUISection(Keys, Shortcuts)]
        public ProxyBinding ActivateBinding { get; set; }

        // ---- Constructor de manzanas ----

        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public CompareOp WidthOp { get; set; } = CompareOp.Equal;

        [SettingsUIDropdown(typeof(BuildingCollectionsSettings), nameof(GetFilterValues))]
        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public int WidthValue { get; set; }

        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public CompareOp DepthOp { get; set; } = CompareOp.Equal;

        [SettingsUIDropdown(typeof(BuildingCollectionsSettings), nameof(GetFilterValues))]
        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public int DepthValue { get; set; }

        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public CompareOp LevelOp { get; set; } = CompareOp.Equal;

        [SettingsUIDropdown(typeof(BuildingCollectionsSettings), nameof(GetFilterValues))]
        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public int LevelValue { get; set; }

        [SettingsUISlider(min = 1, max = 100, step = 1, unit = Unit.kPercentage)]
        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public int Density { get; set; } = 100;

        /// <summary>
        /// Con Anarchy encendido, casilla "Overlap buildings" del panel: mayor que 0 = marcada. La cantidad por junta
        /// está en <see cref="OverlapMeters"/>; lo ya construido no se pisa nunca.
        /// </summary>
        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public float AnarchyOverlap { get; set; } = 0.5f;

        /// <summary>Solape máximo por junta (m) cuando "Overlap buildings" está marcado: 0,5, 1, 1,5 o 2.</summary>
        [SettingsUISlider(min = 0.5f, max = 2f, step = 0.5f, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(Section, Builder)]
        public float OverlapMeters { get; set; } = 0.5f;

        /// <summary>Árboles del juego base en el espacio sobrante (patios, huecos).</summary>
        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public bool Trees { get; set; }

        /// <summary>Set de Tree Controller para la decoración ("" = todos los árboles del juego base).</summary>
        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public string TreeSet { get; set; } = string.Empty;

        /// <summary>Edades de los árboles (bits de Game.Tools.AgeMask: 1 joven, 2 adolescente, 4 adulto, 8 viejo).</summary>
        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public int TreeAges { get; set; } = 4;

        /// <summary>Segunda colección de la mezcla del constructor ("" = sin mezcla).</summary>
        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public string MixCollection { get; set; } = string.Empty;

        /// <summary>Porcentaje de edificios de la primera colección en la mezcla (5 a 95).</summary>
        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public int MixShare { get; set; } = 50;

        /// <summary>Densidad de la decoración (10 a 100).</summary>
        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public int DecorDensity { get; set; } = 50;

        /// <summary>
        /// Área máxima (hectáreas) de una manzana en modo Block. Una más grande no se selecciona (se pide Polygon): con
        /// curvas, cada manzana grande genera muchas tiras y la vista previa tarda más. 1 ha = 10.000 m².
        /// </summary>
        [SettingsUISlider(min = 1, max = 50, step = 1, unit = Unit.kInteger)]
        [SettingsUISection(Section, Builder)]
        public int MaxBlockHectares { get; set; } = 5;

        /// <summary>Modo de selección del constructor: "block" o "polygon".</summary>
        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public string Mode { get; set; } = "block";

        public DropdownItem<int>[] GetFilterValues() =>
            Enumerable.Range(0, 6).Select(i => new DropdownItem<int> { value = i, displayName = i == 0 ? "All" : i.ToString() }).ToArray();

        public BuildingFilter ToFilter() => new BuildingFilter
        {
            Width = ToNumber(WidthOp, WidthValue),
            Depth = ToNumber(DepthOp, DepthValue),
            Level = ToNumber(LevelOp, LevelValue),
        };

        static NumberFilter ToNumber(CompareOp op, int value) => value <= 0 ? NumberFilter.All : NumberFilter.Of(op, value);

        void Report(string error)
        {
            lastMessage = error ?? "OK";
            if (error != null)
            {
                Mod.Log.Warn(error);
            }
        }

        public override void SetDefaults()
        {
            SelectedCollection = string.Empty;
            NewCollectionName = string.Empty;
            findItTags = false; // restablecer la apaga sin quitar nada (como desmarcarla)
            WidthOp = DepthOp = LevelOp = CompareOp.Equal;
            WidthValue = DepthValue = LevelValue = 0;
            Density = 100;
            Trees = false;
            AnarchyOverlap = 0.5f;
            DecorDensity = 50;
            TreeSet = string.Empty;
            TreeAges = 4;
            MixCollection = string.Empty;
            MixShare = 50;
            Mode = "block";
            MaxBlockHectares = 5;
            OverlapMeters = 0.5f;
        }
    }
}

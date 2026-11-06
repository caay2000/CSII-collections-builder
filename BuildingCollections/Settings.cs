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
        public const string ReshuffleAction = "BuildingCollectionsReshuffle";
        public const string BuildAction = "BuildingCollectionsBuild";

        public BuildingCollectionsSettings(IMod mod) : base(mod) { }

        string lastMessage = string.Empty;

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

        internal bool Loaded;

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

        [SettingsUIMultilineText]
        [SettingsUISection(Section, Collections)]
        public string FindItTagsNote => string.Empty;

        [SettingsUIHidden]
        [SettingsUISection(Section, Collections)]
        public bool FindItTagsChosen { get; set; }

        internal void ChooseFindItTagsOnce()
        {
            if (FindItTagsChosen)
            {
                return;
            }

            findItTags = CollectionStore.Collections.Count > 0;
            FindItTagsChosen = true;
            ApplyAndSave();
            Mod.Log.Info($"Find It tags: first time with the option, {(findItTags ? "on (there were tagged collections already)" : "off")}.");
        }

        [SettingsUIKeyboardBinding(BindingKeyboard.Y, ActivateAction, ctrl: true)]
        [SettingsUISection(Keys, Shortcuts)]
        public ProxyBinding ActivateBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.T, ReshuffleAction)]
        [SettingsUISection(Keys, Shortcuts)]
        public ProxyBinding ReshuffleBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.Y, BuildAction)]
        [SettingsUISection(Keys, Shortcuts)]
        public ProxyBinding BuildBinding { get; set; }

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

        [SettingsUISlider(min = 0f, max = 2f, step = 0.5f, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(Section, Builder)]
        public float OverlapMeters { get; set; } = 0.5f;

        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public bool Trees { get; set; }

        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public string TreeSet { get; set; } = string.Empty;

        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public int TreeAges { get; set; } = 4;

        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public string MixCollection { get; set; } = string.Empty;

        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public int MixShare { get; set; } = 50;

        [SettingsUIHidden]
        [SettingsUISection(Section, Builder)]
        public int DecorDensity { get; set; } = 50;

        [SettingsUISlider(min = 1, max = 50, step = 1, unit = Unit.kInteger)]
        [SettingsUISection(Section, Builder)]
        public int MaxBlockHectares { get; set; } = 5;

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
            findItTags = false;
            WidthOp = DepthOp = LevelOp = CompareOp.Equal;
            WidthValue = DepthValue = LevelValue = 0;
            Density = 100;
            Trees = false;
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

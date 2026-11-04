using System.Collections.Generic;
using BuildingCollections.Core;
using Colossal;

namespace BuildingCollections
{
    public class LocaleEN : IDictionarySource
    {
        readonly BuildingCollectionsSettings s;

        public LocaleEN(BuildingCollectionsSettings setting) { s = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { s.GetSettingsLocaleID(), "Building Collections" },
                { s.GetOptionTabLocaleID(BuildingCollectionsSettings.Section), "Main" },
                { s.GetOptionGroupLocaleID(BuildingCollectionsSettings.Collections), "Collections" },
                { s.GetOptionGroupLocaleID(BuildingCollectionsSettings.Builder), "Block builder" },
                { s.GetOptionTabLocaleID(BuildingCollectionsSettings.Keys), "Key bindings" },
                { s.GetOptionGroupLocaleID(BuildingCollectionsSettings.Anarchy), "Anarchy" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.AnarchyOverlap)), "Max overlap with Anarchy (m)" },
                { s.GetOptionDescLocaleID(nameof(BuildingCollectionsSettings.AnarchyOverlap)), "When the Anarchy mod is on, buildings from different streets may overlap up to this distance, so fewer lots are lost at corners and curves. The actual overlap is whatever the geometry needs, between 0 and this value. Without Anarchy, buildings never overlap." },
                { s.GetOptionGroupLocaleID(BuildingCollectionsSettings.Shortcuts), "Shortcuts" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.ActivateBinding)), "Toggle block builder tool" },
                { s.GetBindingKeyLocaleID(BuildingCollectionsSettings.ActivateAction), "Toggle block builder tool" },

                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.SelectedCollection)), "Collection" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.NewCollectionName)), "New collection name" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.CreateCollection)), "Create collection" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.AddAllFromFindIt)), "Add all Find It items to this collection" },
                { s.GetOptionDescLocaleID(nameof(BuildingCollectionsSettings.AddAllFromFindIt)), "Adds every building in Find It's current filtered list to the selected collection and tags them in Find It." },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.ClearCollection)), "Empty collection" },
                { s.GetOptionWarningLocaleID(nameof(BuildingCollectionsSettings.ClearCollection)), "Remove every building from this collection and its Find It tag?" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.DeleteCollection)), "Delete collection" },
                { s.GetOptionWarningLocaleID(nameof(BuildingCollectionsSettings.DeleteCollection)), "Delete this collection?" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.Status)), "Status" },

                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.WidthOp)), "Lot width: operator" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.WidthValue)), "Lot width: value" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.DepthOp)), "Lot depth: operator" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.DepthValue)), "Lot depth: value" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.LevelOp)), "Building level: operator" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.LevelValue)), "Building level: value" },
                { s.GetOptionLabelLocaleID(nameof(BuildingCollectionsSettings.Density)), "Density" },
                { s.GetOptionDescLocaleID(nameof(BuildingCollectionsSettings.Density)), "Percentage of free cells to cover with buildings." },

                { s.GetEnumValueLocaleID(CompareOp.Equal), "=" },
                { s.GetEnumValueLocaleID(CompareOp.NotEqual), "!=" },
                { s.GetEnumValueLocaleID(CompareOp.Greater), ">" },
                { s.GetEnumValueLocaleID(CompareOp.Less), "<" },
            };
        }

        public void Unload() { }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Features.DetailItemFactory.ViewModels;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;

namespace Synthetic.Features.DetailItemFactory.Services
{
    /// <summary>
    /// Host composition and Revit database helper for Detail Item Factory settings
    /// and shared <c>OST_DetailComponents</c> subcategory harvesting.
    /// </summary>
    public static class DetailItemFactorySettingsService
    {
        /// <summary>
        /// Harvests deduplicated <see cref="BuiltInCategory.OST_DetailComponents"/> subcategory names
        /// from <paramref name="doc"/>, ensuring <c>"Detail Items"</c> is always included.
        /// </summary>
        public static IReadOnlyList<string> HarvestDetailComponentSubcategories(Document doc)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            var subcategories = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Category? detailComponentsCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
            if (detailComponentsCategory != null)
            {
                foreach (Category subCat in detailComponentsCategory.SubCategories)
                {
                    if (!string.IsNullOrWhiteSpace(subCat.Name) && seen.Add(subCat.Name))
                    {
                        subcategories.Add(subCat.Name);
                    }
                }
            }

            if (seen.Add(DetailItemFactorySettings.DefaultTargetSubcategory))
            {
                subcategories.Add(DetailItemFactorySettings.DefaultTargetSubcategory);
            }

            return subcategories.AsReadOnly();
        }

        /// <summary>
        /// Creates a <see cref="DetailItemFactorySettingsViewModel"/> wired to Revit document Extensible Storage,
        /// shared subcategory harvesting, firmwide defaults, and Windows folder browsing.
        /// </summary>
        public static DetailItemFactorySettingsViewModel CreateSettingsViewModel(Document doc, IntPtr mainWindowHandle)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            return new DetailItemFactorySettingsViewModel(
                () => SettingsManager.Get<DetailItemFactorySettings>(doc),
                settings => SettingsManager.Save(doc, settings),
                () => SettingsManager.Delete(doc, DetailItemFactorySettings.Name),
                () => HasDocumentOverride(doc),
                () => HarvestDetailComponentSubcategories(doc),
                new WindowsFolderDialogService(mainWindowHandle),
                LoadFirmwideDefaults);
        }

        private static bool HasDocumentOverride(Document doc)
        {
            var dataStorages = new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();

            string storageName = "Synthetic_" + DetailItemFactorySettings.Name;
            return dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
        }

        private static DetailItemFactorySettings LoadFirmwideDefaults()
        {
            if (SettingsRuntimeOptions.SuppressFirmwideDiskFallback)
            {
                return new DetailItemFactorySettings().Defaults();
            }

            var displaySettings = new DetailItemFactorySettings().Defaults();
            try
            {
                string defaultPath = Config.defaultSettingsPath;
                if (File.Exists(defaultPath))
                {
                    Config? defaultConfig = Config.ReadFromFile(defaultPath);
                    if (defaultConfig != null && defaultConfig.Contains(DetailItemFactorySettings.Name))
                    {
                        displaySettings = defaultConfig.GetSettings<DetailItemFactorySettings>(DetailItemFactorySettings.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DetailItemFactorySettingsService: failed to read firmwide defaults: {ex.Message}");
            }

            return (displaySettings ?? new DetailItemFactorySettings().Defaults()).DeepClone();
        }
    }
}

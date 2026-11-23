using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace Synthetic.Platform.Host
{
    /// <summary>
    /// Static class responsible for dynamically loading the JSON configuration
    /// and building the Revit ribbon controls.
    /// </summary>
    public static class RibbonManager
    {
        private interface IRibbonItemBuilder
        {
            void Build(RibbonPanel panel, RibbonItemConfig item, string assemblyPath, string assetsDir, bool isDebug, int activeVersion);
        }

        private class PushButtonBuilder : IRibbonItemBuilder
        {
            public void Build(RibbonPanel panel, RibbonItemConfig item, string assemblyPath, string assetsDir, bool isDebug, int activeVersion)
            {
                PushButtonData data = CreatePushButtonData(item, assemblyPath, assetsDir);
                var ribbonItem = panel.AddItem(data);
                ValidateCommandClass(ribbonItem as PushButton, item.Class);
            }
        }

        private class StackedGroupBuilder : IRibbonItemBuilder
        {
            public void Build(RibbonPanel panel, RibbonItemConfig item, string assemblyPath, string assetsDir, bool isDebug, int activeVersion)
            {
                if (item.SubItems == null || item.SubItems.Count == 0)
                {
                    return;
                }

                var filteredSubItems = new List<RibbonItemConfig>();
                foreach (var subItem in item.SubItems)
                {
                    if (!RibbonItemFilter.ShouldIncludeItem(subItem, isDebug, activeVersion))
                    {
                        continue;
                    }
                    filteredSubItems.Add(subItem);
                }

                if (filteredSubItems.Count == 0)
                {
                    return;
                }

                var buttonDatas = new List<RibbonItemData>();
                foreach (var subItem in filteredSubItems)
                {
                    buttonDatas.Add(CreatePushButtonData(subItem, assemblyPath, assetsDir));
                }

                IList<RibbonItem> addedItems;
                if (buttonDatas.Count == 3)
                {
                    addedItems = panel.AddStackedItems(buttonDatas[0], buttonDatas[1], buttonDatas[2]);
                }
                else if (buttonDatas.Count == 2)
                {
                    addedItems = panel.AddStackedItems(buttonDatas[0], buttonDatas[1]);
                }
                else // count == 1
                {
                    addedItems = new List<RibbonItem> { panel.AddItem(buttonDatas[0]) };
                }

                for (int i = 0; i < addedItems.Count; i++)
                {
                    ValidateCommandClass(addedItems[i] as PushButton, filteredSubItems[i].Class);
                }
            }
        }

        private class SplitButtonBuilder : IRibbonItemBuilder
        {
            public void Build(RibbonPanel panel, RibbonItemConfig item, string assemblyPath, string assetsDir, bool isDebug, int activeVersion)
            {
                SplitButtonData splitButtonData = new SplitButtonData(item.Name, item.Text);
                var splitButton = panel.AddItem(splitButtonData) as SplitButton;

                if (splitButton != null && item.SubItems != null)
                {
                    foreach (var subItem in item.SubItems)
                    {
                        if (!RibbonItemFilter.ShouldIncludeItem(subItem, isDebug, activeVersion))
                        {
                            continue;
                        }

                        PushButtonData subButtonData = CreatePushButtonData(subItem, assemblyPath, assetsDir);
                        var subButton = splitButton.AddPushButton(subButtonData);
                        ValidateCommandClass(subButton, subItem.Class);
                    }
                }
            }
        }

        private static readonly Dictionary<string, IRibbonItemBuilder> Builders =
            new Dictionary<string, IRibbonItemBuilder>(StringComparer.OrdinalIgnoreCase)
            {
                { "PushButton", new PushButtonBuilder() },
                { "StackedGroup", new StackedGroupBuilder() },
                { "SplitButton", new SplitButtonBuilder() }
            };

        public static void Create(UIControlledApplication appControlled, string assemblyPath)
        {
            string assetsDir = Path.Combine(Path.GetDirectoryName(assemblyPath) ?? string.Empty, "Assets");
            string configPath = Path.Combine(assetsDir, "ribbon_config.json");

            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("Ribbon configuration file not found.", configPath);
            }

            string json = File.ReadAllText(configPath);
            var config = JsonConvert.DeserializeObject<RibbonConfig>(json);

            if (config == null)
            {
                return;
            }

            // Create Ribbon Tab
            appControlled.CreateRibbonTab(config.TabName);

#if DEBUG
            bool isDebug = true;
#else
            bool isDebug = false;
#endif

            int activeVersion = 2026;
            if (appControlled?.ControlledApplication != null)
            {
                if (int.TryParse(appControlled.ControlledApplication.VersionNumber, out int parsedVersion))
                {
                    activeVersion = parsedVersion;
                }
            }

            if (config.Panels != null)
            {
                foreach (var panelConfig in config.Panels)
                {
                    RibbonPanel panel = appControlled.CreateRibbonPanel(config.TabName, panelConfig.Name);

                    // Add items
                    if (panelConfig.Items != null)
                    {
                        AddItemsToPanel(panel, panelConfig.Items, assemblyPath, assetsDir, isDebug, activeVersion);
                    }

                    // Add slideout items if present
                    if (panelConfig.SlideoutItems != null && panelConfig.SlideoutItems.Count > 0)
                    {
                        // Filter slideout items first, so we only call AddSlideOut if there's actually something to add
                        var filteredSlideout = new List<RibbonItemConfig>();
                        foreach (var item in panelConfig.SlideoutItems)
                        {
                            if (!RibbonItemFilter.ShouldIncludeItem(item, isDebug, activeVersion))
                            {
                                continue;
                            }
                            filteredSlideout.Add(item);
                        }

                        if (filteredSlideout.Count > 0)
                        {
                            panel.AddSlideOut();
                            AddItemsToPanel(panel, filteredSlideout, assemblyPath, assetsDir, isDebug, activeVersion);
                        }
                    }
                }
            }
        }

        private static void AddItemsToPanel(RibbonPanel panel, List<RibbonItemConfig> items, string assemblyPath, string assetsDir, bool isDebug, int activeVersion)
        {
            foreach (var item in items)
            {
                if (!RibbonItemFilter.ShouldIncludeItem(item, isDebug, activeVersion))
                {
                    continue;
                }

                if (Builders.TryGetValue(item.Type, out var builder))
                {
                    builder.Build(panel, item, assemblyPath, assetsDir, isDebug, activeVersion);
                }
            }
        }

        private static PushButtonData CreatePushButtonData(RibbonItemConfig item, string assemblyPath, string assetsDir)
        {
            PushButtonData data = new PushButtonData(
                item.Name,
                item.Text,
                assemblyPath,
                item.Class
            );

            data.ToolTip = item.Tooltip ?? string.Empty;

            if (!string.IsNullOrEmpty(item.AvailabilityClass))
            {
                data.AvailabilityClassName = item.AvailabilityClass;
            }

            // Fallback for large image
            string largeImagePath = ResolveImagePath(assetsDir, item.LargeImage, "placeholder_32.png");
            data.LargeImage = LoadBitmapImage(largeImagePath);

            // Fallback for small image
            string imagePath = ResolveImagePath(assetsDir, item.Image, "placeholder_16.png");
            data.Image = LoadBitmapImage(imagePath);

            return data;
        }

        private static string ResolveImagePath(string assetsDir, string? requestedName, string fallbackName)
        {
            string imageName = string.IsNullOrEmpty(requestedName) ? fallbackName : requestedName!;
            string imagePath = Path.Combine(assetsDir, imageName);
            if (!File.Exists(imagePath))
            {
                imagePath = Path.Combine(assetsDir, fallbackName);
            }
            return imagePath;
        }

        internal static BitmapImage LoadBitmapImage(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }

        private static void ValidateCommandClass(PushButton? button, string classPath)
        {
            if (button == null || string.IsNullOrEmpty(classPath))
            {
                return;
            }

            var type = Assembly.GetExecutingAssembly().GetType(classPath);
            if (type == null)
            {
                button.Enabled = false;
                button.ToolTip = $"[WARNING] Command class not found: {classPath}";
            }
        }
    }
}

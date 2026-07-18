using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace Synthetic.Core
{
    /// <summary>
    /// Static class responsible for dynamically loading the JSON configuration
    /// and building the Revit ribbon controls.
    /// </summary>
    public static class RibbonManager
    {
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
                            if (item.DebugOnly && !isDebug)
                            {
                                continue;
                            }
                            if (!IsVersionMatch(item, activeVersion))
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

        public static bool IsVersionMatch(RibbonItemConfig item, int currentVersion)
        {
            if (item.MinVersion.HasValue && currentVersion < item.MinVersion.Value)
            {
                return false;
            }
            if (item.MaxVersion.HasValue && currentVersion > item.MaxVersion.Value)
            {
                return false;
            }
            return true;
        }

        private static void AddItemsToPanel(RibbonPanel panel, List<RibbonItemConfig> items, string assemblyPath, string assetsDir, bool isDebug, int activeVersion)
        {
            foreach (var item in items)
            {
                if (item.DebugOnly && !isDebug)
                {
                    continue;
                }
                if (!IsVersionMatch(item, activeVersion))
                {
                    continue;
                }

                if (string.Equals(item.Type, "PushButton", StringComparison.OrdinalIgnoreCase))
                {
                    PushButtonData data = CreatePushButtonData(item, assemblyPath, assetsDir);
                    var ribbonItem = panel.AddItem(data);
                    ValidateCommandClass(ribbonItem as PushButton, item.Class);
                }
                else if (string.Equals(item.Type, "StackedGroup", StringComparison.OrdinalIgnoreCase))
                {
                    if (item.SubItems == null || item.SubItems.Count == 0)
                    {
                        continue;
                    }

                    var filteredSubItems = new List<RibbonItemConfig>();
                    foreach (var subItem in item.SubItems)
                    {
                        if (subItem.DebugOnly && !isDebug)
                        {
                            continue;
                        }
                        if (!IsVersionMatch(subItem, activeVersion))
                        {
                            continue;
                        }
                        filteredSubItems.Add(subItem);
                    }

                    if (filteredSubItems.Count == 0)
                    {
                        continue;
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
                else if (string.Equals(item.Type, "SplitButton", StringComparison.OrdinalIgnoreCase))
                {
                    SplitButtonData splitButtonData = new SplitButtonData(item.Name, item.Text);
                    var splitButton = panel.AddItem(splitButtonData) as SplitButton;

                    if (splitButton != null && item.SubItems != null)
                    {
                        foreach (var subItem in item.SubItems)
                        {
                            if (subItem.DebugOnly && !isDebug)
                            {
                                continue;
                            }
                            if (!IsVersionMatch(subItem, activeVersion))
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
            string largeImageName = string.IsNullOrEmpty(item.LargeImage) ? "placeholder_32.png" : item.LargeImage;
            string largeImagePath = Path.Combine(assetsDir, largeImageName);
            if (!File.Exists(largeImagePath))
            {
                largeImagePath = Path.Combine(assetsDir, "placeholder_32.png");
            }
            data.LargeImage = new BitmapImage(new Uri(largeImagePath));

            // Fallback for small image
            string imageName = string.IsNullOrEmpty(item.Image) ? "placeholder_16.png" : item.Image;
            string imagePath = Path.Combine(assetsDir, imageName);
            if (!File.Exists(imagePath))
            {
                imagePath = Path.Combine(assetsDir, "placeholder_16.png");
            }
            data.Image = new BitmapImage(new Uri(imagePath));

            return data;
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

    public class RibbonConfig
    {
        [JsonProperty("tab_name")]
        public string TabName { get; set; }

        [JsonProperty("panels")]
        public List<PanelConfig> Panels { get; set; }
    }

    public class PanelConfig
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("items")]
        public List<RibbonItemConfig> Items { get; set; }

        [JsonProperty("slideout_items")]
        public List<RibbonItemConfig> SlideoutItems { get; set; }
    }

    public class RibbonItemConfig
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("class")]
        public string Class { get; set; }

        [JsonProperty("tooltip")]
        public string Tooltip { get; set; }

        [JsonProperty("large_image")]
        public string LargeImage { get; set; }

        [JsonProperty("image")]
        public string Image { get; set; }

        [JsonProperty("availability_class")]
        public string AvailabilityClass { get; set; }

        [JsonProperty("debugOnly")]
        public bool DebugOnly { get; set; }

        [JsonProperty("minVersion")]
        public int? MinVersion { get; set; }

        [JsonProperty("maxVersion")]
        public int? MaxVersion { get; set; }

        [JsonProperty("sub_items")]
        public List<RibbonItemConfig> SubItems { get; set; }
    }
}

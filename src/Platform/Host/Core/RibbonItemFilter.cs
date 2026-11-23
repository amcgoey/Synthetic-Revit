using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Synthetic.Platform.Host
{
    /// <summary>
    /// Root ribbon tab and panel configuration deserialized from <c>ribbon_config.json</c>.
    /// </summary>
    public class RibbonConfig
    {
        /// <summary>
        /// Gets or sets the display name of the Revit ribbon tab.
        /// </summary>
        [JsonProperty("tab_name")]
        public string TabName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ordered list of ribbon panels in the tab.
        /// </summary>
        [JsonProperty("panels")]
        public List<PanelConfig>? Panels { get; set; }
    }

    /// <summary>
    /// Ribbon panel definition containing primary and slideout ribbon items.
    /// </summary>
    public class PanelConfig
    {
        /// <summary>
        /// Gets or sets the panel display name.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the primary items displayed directly on the panel.
        /// </summary>
        [JsonProperty("items")]
        public List<RibbonItemConfig>? Items { get; set; }

        /// <summary>
        /// Gets or sets optional items placed in the panel slideout drawer.
        /// </summary>
        [JsonProperty("slideout_items")]
        public List<RibbonItemConfig>? SlideoutItems { get; set; }
    }

    /// <summary>
    /// Configuration schema for a ribbon button, stacked group, or split button item.
    /// </summary>
    public class RibbonItemConfig
    {
        /// <summary>
        /// Gets or sets the item builder type (<c>PushButton</c>, <c>StackedGroup</c>, or <c>SplitButton</c>).
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the internal Revit ribbon item name.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the visible button label text.
        /// </summary>
        [JsonProperty("text")]
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the fully qualified <c>IExternalCommand</c> type name.
        /// </summary>
        [JsonProperty("class")]
        public string Class { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the tooltip text displayed on hover.
        /// </summary>
        [JsonProperty("tooltip")]
        public string? Tooltip { get; set; }

        /// <summary>
        /// Gets or sets the 32x32 icon filename under <c>Assets/</c>.
        /// </summary>
        [JsonProperty("large_image")]
        public string? LargeImage { get; set; }

        /// <summary>
        /// Gets or sets the 16x16 icon filename under <c>Assets/</c>.
        /// </summary>
        [JsonProperty("image")]
        public string? Image { get; set; }

        /// <summary>
        /// Gets or sets an optional <c>IExternalCommandAvailability</c> type name.
        /// </summary>
        [JsonProperty("availability_class")]
        public string? AvailabilityClass { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the item appears only in Debug builds.
        /// </summary>
        [JsonProperty("debugOnly")]
        public bool DebugOnly { get; set; }

        /// <summary>
        /// Gets or sets the inclusive minimum supported Revit version year.
        /// </summary>
        [JsonProperty("minVersion")]
        public int? MinVersion { get; set; }

        /// <summary>
        /// Gets or sets the inclusive maximum supported Revit version year.
        /// </summary>
        [JsonProperty("maxVersion")]
        public int? MaxVersion { get; set; }

        /// <summary>
        /// Gets or sets nested items for <c>StackedGroup</c> and <c>SplitButton</c> entries.
        /// </summary>
        [JsonProperty("sub_items")]
        public List<RibbonItemConfig>? SubItems { get; set; }
    }

    /// <summary>
    /// Pure filtering logic for evaluating ribbon item visibility across build configurations and Revit versions.
    /// </summary>
    public static class RibbonItemFilter
    {
        /// <summary>
        /// Determines whether a ribbon item should be included for the specified build mode and Revit version.
        /// </summary>
        public static bool ShouldIncludeItem(RibbonItemConfig item, bool isDebug, int activeVersion)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (item.DebugOnly && !isDebug)
            {
                return false;
            }

            return IsVersionMatch(item, activeVersion);
        }

        /// <summary>
        /// Determines whether the item's optional <c>MinVersion</c>/<c>MaxVersion</c> bounds include <paramref name="currentVersion"/>.
        /// </summary>
        public static bool IsVersionMatch(RibbonItemConfig item, int currentVersion)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

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
    }
}

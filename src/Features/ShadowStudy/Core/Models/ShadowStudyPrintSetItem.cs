using System;
using System.Collections.Generic;

namespace Synthetic.Features.ShadowStudy.Models
{
    /// <summary>
    /// Represents a selectable Revit document Print Set (or the <c>&lt;In-Session&gt;</c> sentinel)
    /// partitioned into direct shadow-capable graphical views and non-shadow members.
    /// </summary>
    public class ShadowStudyPrintSetItem
    {
        /// <summary>
        /// Display name used for the unsaved in-session selection entry.
        /// </summary>
        public const string InSessionName = "<In-Session>";

        private int? _ignoredNonShadowMemberCount;

        /// <summary>
        /// Gets or sets the Revit <c>ViewSheetSet.UniqueId</c>, or <see cref="string.Empty"/> for <c>&lt;In-Session&gt;</c>.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Print Set display name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ordered list of direct eligible shadow-capable graphical view <c>UniqueId</c>s in this Print Set.
        /// </summary>
        public IReadOnlyList<string> ShadowViewIds { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the non-shadow member <c>UniqueId</c>s (for example <c>ViewSheet</c>, schedules, legends, drafting views,
        /// or walkthroughs) stored in this Print Set.
        /// </summary>
        public IReadOnlyList<string> NonShadowMemberIds { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the number of non-shadow members ignored during Shadow Study export and preserved on Save / Save As.
        /// Defaults to <see cref="NonShadowMemberIds"/> count unless explicitly overridden.
        /// </summary>
        public int IgnoredNonShadowMemberCount
        {
            get => _ignoredNonShadowMemberCount ?? (NonShadowMemberIds != null ? NonShadowMemberIds.Count : 0);
            set => _ignoredNonShadowMemberCount = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether this item represents the <c>&lt;In-Session&gt;</c> selection entry.
        /// </summary>
        public bool IsInSession { get; set; }

        /// <summary>
        /// Creates the <c>&lt;In-Session&gt;</c> sentinel item.
        /// </summary>
        public static ShadowStudyPrintSetItem CreateInSession()
        {
            return new ShadowStudyPrintSetItem
            {
                Id = string.Empty,
                Name = InSessionName,
                ShadowViewIds = Array.Empty<string>(),
                NonShadowMemberIds = Array.Empty<string>(),
                IgnoredNonShadowMemberCount = 0,
                IsInSession = true
            };
        }
    }
}

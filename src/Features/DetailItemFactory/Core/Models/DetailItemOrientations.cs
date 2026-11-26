using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Synthetic.Features.DetailItemFactory.Models
{
    /// <summary>
    /// Canonical Detail Item orientation labels used for family file naming and UI selection.
    /// </summary>
    public static class DetailItemOrientations
    {
        public const string Plan = "Plan";
        public const string ElevFront = "Elev Front";
        public const string ElevBack = "Elev Back";
        public const string ElevSide = "Elev Side";
        public const string ElevTop = "Elev Top";
        public const string ElevBottom = "Elev Bottom";
        public const string ElevSection = "Elev Section";

        /// <summary>
        /// Gets the ordered list of all supported orientation labels.
        /// </summary>
        public static IReadOnlyList<string> All { get; } = new ReadOnlyCollection<string>(new[]
        {
            Plan,
            ElevFront,
            ElevBack,
            ElevSide,
            ElevTop,
            ElevBottom,
            ElevSection
        });
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Synthetic.Features.DetailItemFactory.Settings;

namespace Synthetic.Features.DetailItemFactory.Models
{
    /// <summary>
    /// Immutable Core snapshot harvested from the active Revit session prior to UI configuration.
    /// </summary>
    public sealed class DetailItemSessionSnapshot
    {
        /// <summary>
        /// Gets the active view <c>UniqueId</c>.
        /// </summary>
        public string ActiveViewUniqueId { get; }

        /// <summary>
        /// Gets the active view name.
        /// </summary>
        public string ActiveViewName { get; }

        /// <summary>
        /// Gets the classified 2D source view kind.
        /// </summary>
        public DetailItemSourceViewKind ActiveViewKind { get; }

        /// <summary>
        /// Gets a value indicating whether the active view is a supported non-template 2D view.
        /// </summary>
        public bool IsActiveViewSupported { get; }

        /// <summary>
        /// Gets the guarded default output folder path (falling back to My Documents for cloud, detached, or unsaved models).
        /// </summary>
        public string DefaultOutputFolder { get; }

        /// <summary>
        /// Gets an isolated deep clone of the project's <see cref="DetailItemFactorySettings"/>.
        /// </summary>
        public DetailItemFactorySettings Settings { get; }

        /// <summary>
        /// Gets the deduplicated <c>OST_DetailComponents</c> subcategory names (always including <c>"Detail Items"</c>).
        /// </summary>
        public IReadOnlyList<string> AvailableSubcategories { get; }

        /// <summary>
        /// Gets the ordered list of eligible elements harvested from the active selection.
        /// </summary>
        public IReadOnlyList<DetailItemSessionElement> Elements { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemSessionSnapshot"/> class.
        /// </summary>
        public DetailItemSessionSnapshot(
            string activeViewUniqueId,
            string activeViewName,
            DetailItemSourceViewKind activeViewKind,
            bool isActiveViewSupported,
            string? defaultOutputFolder,
            DetailItemFactorySettings? settings,
            IEnumerable<string>? availableSubcategories = null,
            IEnumerable<DetailItemSessionElement>? elements = null)
        {
            ActiveViewUniqueId = activeViewUniqueId ?? string.Empty;
            ActiveViewName = activeViewName ?? string.Empty;
            ActiveViewKind = activeViewKind;
            IsActiveViewSupported = isActiveViewSupported;
            DefaultOutputFolder = defaultOutputFolder ?? string.Empty;
            Settings = (settings ?? new DetailItemFactorySettings().Defaults()).DeepClone();

            var subcatList = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (availableSubcategories != null)
            {
                foreach (string name in availableSubcategories)
                {
                    if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    {
                        subcatList.Add(name);
                    }
                }
            }

            if (seen.Add(DetailItemFactorySettings.DefaultTargetSubcategory))
            {
                subcatList.Add(DetailItemFactorySettings.DefaultTargetSubcategory);
            }

            AvailableSubcategories = new ReadOnlyCollection<string>(subcatList);

            var elementList = elements != null
                ? elements.Where(e => e != null).ToList()
                : new List<DetailItemSessionElement>();
            Elements = new ReadOnlyCollection<DetailItemSessionElement>(elementList);
        }
    }
}

using System;
using System.Collections.Generic;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.ShadowStudy.ViewModels
{
    /// <summary>
    /// Selectable checklist row ViewModel wrapping an eligible <see cref="ShadowStudyViewItem"/>.
    /// </summary>
    public class ShadowStudySelectableViewItemViewModel : ViewModelBase
    {
        private readonly Action? _onSelectionChanged;
        private bool _isSelected;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShadowStudySelectableViewItemViewModel"/> class.
        /// </summary>
        public ShadowStudySelectableViewItemViewModel(
            ShadowStudyViewItem viewItem,
            Action? onSelectionChanged = null,
            bool isSelected = false)
        {
            ViewItem = viewItem ?? throw new ArgumentNullException(nameof(viewItem));
            _onSelectionChanged = onSelectionChanged;
            _isSelected = isSelected;
        }

        /// <summary>
        /// Gets the underlying eligible view DTO.
        /// </summary>
        public ShadowStudyViewItem ViewItem { get; }

        /// <summary>
        /// Gets the serialized Revit element UniqueId.
        /// </summary>
        public string ViewId => ViewItem.Id;

        /// <summary>
        /// Gets the raw Revit view name.
        /// </summary>
        public string Name => ViewItem.Name;

        /// <summary>
        /// Gets the Revit ViewType string.
        /// </summary>
        public string ViewType => ViewItem.ViewType;

        /// <summary>
        /// Gets the formatted display string combining view name and type.
        /// </summary>
        public string DisplayName => $"{ViewItem.Name} ({ViewItem.ViewType})";

        /// <summary>
        /// Gets a value indicating whether CropBoxActive is enabled on the view.
        /// </summary>
        public bool IsCropped => ViewItem.IsCropped;

        /// <summary>
        /// Gets the non-blocking per-view warning badges (for example, Uncropped or Wireframe/Rendering).
        /// </summary>
        public IReadOnlyList<ShadowStudyViewWarningBadge> Warnings =>
            ViewItem.Warnings ?? (IReadOnlyList<ShadowStudyViewWarningBadge>)Array.Empty<ShadowStudyViewWarningBadge>();

        /// <summary>
        /// Gets a value indicating whether this view carries one or more warning badges.
        /// </summary>
        public bool HasWarnings => ViewItem.HasWarnings;

        /// <summary>
        /// Gets or sets a value indicating whether this view is checked for batch export.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    _onSelectionChanged?.Invoke();
                }
            }
        }
    }
}

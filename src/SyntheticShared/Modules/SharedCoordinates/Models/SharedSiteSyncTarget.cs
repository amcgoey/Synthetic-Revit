using Synthetic.Shared.UI;

namespace Synthetic.Modules.SharedCoordinates.Models
{
    /// <summary>
    /// Represents a candidate Level-Based Shared Site to create or update during Shared Site Sync.
    /// </summary>
    public class SharedSiteSyncTarget : ViewModelBase
    {
        private bool _isChecked;
        private string _levelName = string.Empty;
        private double _sharedElevation;
        private bool _siteExists;

        /// <summary>
        /// Gets or sets the target level name matching the Level-Based Shared Site name.
        /// </summary>
        public string LevelName
        {
            get => _levelName;
            set => SetProperty(ref _levelName, value);
        }

        /// <summary>
        /// Gets or sets the target level's shared coordinate elevation in feet.
        /// </summary>
        public double SharedElevation
        {
            get => _sharedElevation;
            set => SetProperty(ref _sharedElevation, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this target site is selected for synchronization.
        /// </summary>
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether a Level-Based Shared Site with this exact name already exists.
        /// </summary>
        public bool SiteExists
        {
            get => _siteExists;
            set
            {
                if (SetProperty(ref _siteExists, value))
                {
                    OnPropertyChanged(nameof(ActionText));
                }
            }
        }

        /// <summary>
        /// Gets the display action text indicating whether synchronization will Update an existing site or Create a new one.
        /// </summary>
        public string ActionText => SiteExists ? "Update" : "Create";

        /// <summary>
        /// Gets a formatted string representation of the shared elevation in feet.
        /// </summary>
        public string DisplayElevation => $"{SharedElevation:F3}'";
    }
}

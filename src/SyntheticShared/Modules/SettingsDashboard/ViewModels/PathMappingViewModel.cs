using System;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel representing a single path mapping entry (Original -> LocalMapped).
    /// </summary>
    public class PathMappingViewModel : ViewModelBase
    {
        private string _originalServerPath;
        private string _localMappedPath;

        /// <summary>
        /// Gets or sets the original server network path.
        /// </summary>
        public string OriginalServerPath
        {
            get => _originalServerPath;
            set => SetProperty(ref _originalServerPath, value);
        }

        /// <summary>
        /// Gets or sets the local mapped network path.
        /// </summary>
        public string LocalMappedPath
        {
            get => _localMappedPath;
            set => SetProperty(ref _localMappedPath, value);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PathMappingViewModel"/> class.
        /// </summary>
        public PathMappingViewModel()
        {
            _originalServerPath = string.Empty;
            _localMappedPath = string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PathMappingViewModel"/> class with initial paths.
        /// </summary>
        public PathMappingViewModel(string originalServerPath, string localMappedPath)
        {
            _originalServerPath = originalServerPath ?? string.Empty;
            _localMappedPath = localMappedPath ?? string.Empty;
        }
    }
}

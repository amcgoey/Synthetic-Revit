using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.AutoTagger.ViewModels
{
    /// <summary>
    /// Represents an item with a naming conflict and its resolution options.
    /// </summary>
    public class ConflictItem : ViewModelBase
    {
        /// <summary>
        /// Gets or sets the display name of the conflicted item.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the unique key of the conflicted item.
        /// </summary>
        public string UniqueKey { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the list of candidates to resolve the conflict.
        /// </summary>
        public List<TagTemplate> Candidates { get; set; } = new List<TagTemplate>();

        private TagTemplate? _selectedCandidate;
        /// <summary>
        /// Gets or sets the selected candidate chosen to resolve the conflict.
        /// </summary>
        public TagTemplate? SelectedCandidate
        {
            get => _selectedCandidate;
            set
            {
                SetProperty(ref _selectedCandidate, value);
            }
        }
    }

    /// <summary>
    /// ViewModel for resolving template conflicts.
    /// </summary>
    public class ResolveConflictsViewModel : ViewModelBase
    {
        /// <summary>
        /// Gets the collection of conflict items.
        /// </summary>
        public ObservableCollection<ConflictItem> Conflicts { get; }

        /// <summary>
        /// Gets a value indicating whether the user proceeded with resolving the conflicts.
        /// </summary>
        public bool Proceeded { get; private set; } = false;

        /// <summary>
        /// Gets the proceed command.
        /// </summary>
        public ICommand ProceedCommand { get; }

        /// <summary>
        /// Gets the cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Gets or sets the close action for the window.
        /// </summary>
        public Action? CloseAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResolveConflictsViewModel"/> class.
        /// </summary>
        /// <param name="conflicts">The list of naming conflicts to resolve.</param>
        public ResolveConflictsViewModel(List<ConflictItem> conflicts)
        {
            Conflicts = new ObservableCollection<ConflictItem>(conflicts);
            
            // Default select the first candidate for each conflict item
            foreach (var item in Conflicts)
            {
                item.SelectedCandidate = item.Candidates.FirstOrDefault();
            }

            ProceedCommand = new RelayCommand(ExecuteProceed, CanExecuteProceed);
            CancelCommand = new RelayCommand(ExecuteCancel);
        }

        private bool CanExecuteProceed(object obj)
        {
            return Conflicts.All(c => c.SelectedCandidate != null);
        }

        private void ExecuteProceed(object obj)
        {
            Proceeded = true;
            CloseAction?.Invoke();
        }

        private void ExecuteCancel(object obj)
        {
            Proceeded = false;
            CloseAction?.Invoke();
        }
    }
}

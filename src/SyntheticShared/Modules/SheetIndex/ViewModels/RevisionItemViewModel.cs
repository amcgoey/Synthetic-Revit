using System.ComponentModel;
using System.Runtime.CompilerServices;
using Synthetic.Modules.SheetIndex.Models;

namespace Synthetic.Modules.SheetIndex.ViewModels
{
    public class RevisionItemViewModel : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name => Model?.Name ?? string.Empty;
        public string Date => Model?.Date ?? string.Empty;
        public int Sequence => Model?.Sequence ?? 0;
        public SheetIndexRevisionModel Model { get; }

        public RevisionItemViewModel(SheetIndexRevisionModel model, bool isSelected = true)
        {
            Model = model;
            _isSelected = isSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Synthetic.Modules.SheetIndex.Models;

namespace Synthetic.Modules.SheetIndex.ViewModels
{
    public class SheetItemViewModel : INotifyPropertyChanged
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

        public string SheetNumber => Model?.SheetNumber ?? string.Empty;
        public string SheetName => Model?.SheetName ?? string.Empty;
        public SheetIndexSheetModel Model { get; }

        public SheetItemViewModel(SheetIndexSheetModel model, bool isSelected = true)
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

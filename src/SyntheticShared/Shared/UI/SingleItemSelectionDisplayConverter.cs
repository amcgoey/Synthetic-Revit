using System;
using System.Globalization;
using System.Windows.Data;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// MultiValueConverter to dynamically resolve names of items for single-item selection.
    /// Expects two values: the item itself, and the parent DataContext implementing <see cref="ISingleItemSelectionViewModel"/>.
    /// </summary>
    public class SingleItemSelectionDisplayConverter : IMultiValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 2 && values[1] is ISingleItemSelectionViewModel vm)
            {
                var item = values[0];
                return vm.GetItemDisplayName(item);
            }

            return values?[0]?.ToString() ?? string.Empty;
        }

        /// <inheritdoc/>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

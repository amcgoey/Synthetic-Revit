using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Shared.UI{
    /// <summary>
    /// Converts an enum value to a boolean for binding RadioButtons to Enum properties.
    /// </summary>
    public class EnumToBooleanConverter : IValueConverter
    {
        /// <summary>
        /// Converts an enum value to a boolean. Returns true if the value equals the parameter.
        /// </summary>
        /// <param name="value">The enum value produced by the binding source.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">The converter parameter to compare against.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>True if the value equals the parameter; otherwise, false.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return DependencyProperty.UnsetValue;

            return value.Equals(parameter);
        }

        /// <summary>
        /// Converts a boolean back to the enum value. Returns the parameter if value is true.
        /// </summary>
        /// <param name="value">The boolean value produced by the binding target.</param>
        /// <param name="targetType">The type to convert to.</param>
        /// <param name="parameter">The converter parameter specifying the target enum value.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>The parameter value if true; otherwise DependencyProperty.UnsetValue.</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return DependencyProperty.UnsetValue;

            if (value is bool isChecked && isChecked)
            {
                return parameter;
            }

            return DependencyProperty.UnsetValue;
        }
    }
}

using System;
using System.Windows;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Interaction logic for SingleItemSelectionWindow.xaml.
    /// </summary>
    public partial class SingleItemSelectionWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SingleItemSelectionWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public SingleItemSelectionWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is ISingleItemSelectionViewModel vm)
                {
                    vm.CloseAction = (dialogResult) =>
                    {
                        try
                        {
                            this.DialogResult = dialogResult;
                        }
                        catch (InvalidOperationException)
                        {
                            // Window is closing/closed
                        }
                        this.Close();
                    };
                }
            };
        }
    }
}

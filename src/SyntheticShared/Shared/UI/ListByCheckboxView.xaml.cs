using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Interaction logic for ListByCheckboxView.xaml.
    /// </summary>
    public partial class ListByCheckboxView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ListByCheckboxView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public ListByCheckboxView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is ListByCheckboxViewModel vm)
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

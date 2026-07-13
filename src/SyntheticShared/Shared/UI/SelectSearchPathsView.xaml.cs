using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Interaction logic for SelectSearchPathsView.xaml.
    /// </summary>
    public partial class SelectSearchPathsView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SelectSearchPathsView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public SelectSearchPathsView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is SelectSearchPathsViewModel vm)
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

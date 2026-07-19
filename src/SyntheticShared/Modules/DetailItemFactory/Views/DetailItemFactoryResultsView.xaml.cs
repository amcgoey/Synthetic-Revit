using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.DetailItemFactory.Views
{
    /// <summary>
    /// Interaction logic for DetailItemFactoryResultsView.xaml
    /// </summary>
    public partial class DetailItemFactoryResultsView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactoryResultsView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public DetailItemFactoryResultsView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is DetailItemFactoryResultsViewModel vm)
                {
                    vm.CloseAction = () => this.Close();
                }
            };
        }
    }
}

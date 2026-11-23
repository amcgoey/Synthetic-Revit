using Synthetic.Features.DetailItemFactory.Commands;
using Synthetic.Features.DetailItemFactory.Handlers;
using Synthetic.Features.DetailItemFactory.ViewModels;
using Synthetic.Features.DetailItemFactory.Views;
using Synthetic.Features.DetailItemFactory.Settings;

using System;
using System.Windows;

using Synthetic.Platform.UI;

namespace Synthetic.Features.DetailItemFactory.Views
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

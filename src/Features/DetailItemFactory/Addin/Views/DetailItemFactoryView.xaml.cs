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
    /// Interaction logic for DetailItemFactoryView.xaml
    /// </summary>
    public partial class DetailItemFactoryView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactoryView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public DetailItemFactoryView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is DetailItemFactoryViewModel vm)
                {
                    vm.CloseAction = (dialogResult) =>
                    {
                        try
                        {
                            this.DialogResult = dialogResult;
                        }
                        catch (InvalidOperationException)
                        {
                            // Window is closing or closed
                        }
                        this.Close();
                    };
                }
            };
        }
    }
}

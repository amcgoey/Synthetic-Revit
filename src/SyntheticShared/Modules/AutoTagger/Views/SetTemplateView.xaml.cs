using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.AutoTagger.Views
{
    /// <summary>
    /// Interaction logic for SetTemplateView.xaml
    /// </summary>
    public partial class SetTemplateView : Window
    {
        /// <summary>
        /// Initializes a new instance of the SetTemplateView class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent window handle.</param>
        public SetTemplateView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}

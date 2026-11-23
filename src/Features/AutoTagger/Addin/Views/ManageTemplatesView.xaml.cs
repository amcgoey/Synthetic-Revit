using Synthetic.Features.AutoTagger.Commands;
using Synthetic.Features.AutoTagger.Models;
using Synthetic.Features.AutoTagger.ViewModels;
using Synthetic.Features.AutoTagger.Views;
using Synthetic.Features.AutoTagger.Repositories;

using System;
using System.Windows;

using Synthetic.Platform.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Features.AutoTagger.Views
{
    /// <summary>
    /// Interaction logic for ManageTemplatesView.xaml.
    /// </summary>
    public partial class ManageTemplatesView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ManageTemplatesView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public ManageTemplatesView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}

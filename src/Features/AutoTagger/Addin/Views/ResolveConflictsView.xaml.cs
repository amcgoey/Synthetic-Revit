using Synthetic.Features.AutoTagger.Commands;
using Synthetic.Features.AutoTagger.Models;
using Synthetic.Features.AutoTagger.ViewModels;
using Synthetic.Features.AutoTagger.Views;
using Synthetic.Features.AutoTagger.Repositories;

using System;
using System.Windows;

using Synthetic.Platform.UI;

namespace Synthetic.Features.AutoTagger.Views
{
    /// <summary>
    /// Interaction logic for ResolveConflictsView.xaml
    /// </summary>
    public partial class ResolveConflictsView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResolveConflictsView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public ResolveConflictsView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}

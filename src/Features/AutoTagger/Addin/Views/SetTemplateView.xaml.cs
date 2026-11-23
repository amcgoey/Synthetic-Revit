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

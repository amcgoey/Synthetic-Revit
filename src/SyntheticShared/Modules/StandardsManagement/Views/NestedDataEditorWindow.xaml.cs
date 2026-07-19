using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.StandardsManagement.Views
{
    /// <summary>
    /// Interaction logic for NestedDataEditorWindow.xaml
    /// </summary>
    public partial class NestedDataEditorWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NestedDataEditorWindow"/> class.
        /// </summary>
        /// <param name="ownerHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        /// <param name="vm">The view model associated with this editor window.</param>
        public NestedDataEditorWindow(IntPtr ownerHandle, NestedDataEditorViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;

            if (ownerHandle != IntPtr.Zero)
            {
                RevitWindowHelper.SetOwner(this, ownerHandle);
            }

#if !REVIT2026
            try
            {
                PriorityColumn.Visibility = Visibility.Collapsed;
            }
            catch {}
#endif

            vm.CloseAction = () =>
            {
                try
                {
                    this.DialogResult = vm.DialogResult;
                }
                catch {}
                this.Close();
            };
        }
    }
}

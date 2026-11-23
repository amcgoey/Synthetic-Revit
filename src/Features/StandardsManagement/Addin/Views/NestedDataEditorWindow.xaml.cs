using System;
using System.Windows;

using Synthetic.Platform.UI;
using Synthetic.Features.StandardsManagement.ViewModels;

namespace Synthetic.Features.StandardsManagement.Views
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

#if REVIT2023 || REVIT2024 || REVIT2025
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

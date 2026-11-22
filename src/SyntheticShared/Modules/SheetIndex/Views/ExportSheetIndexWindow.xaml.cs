using System;
using System.Windows;
using Synthetic.Modules.SheetIndex.ViewModels;

namespace Synthetic.Modules.SheetIndex.Views
{
    public partial class ExportSheetIndexWindow : Window
    {
        public ExportSheetIndexWindow()
        {
            InitializeComponent();
        }

        public ExportSheetIndexWindow(IntPtr parentHandle) : this()
        {
            if (parentHandle != IntPtr.Zero)
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = parentHandle;
            }
        }

        public ExportSheetIndexWindow(ExportSheetIndexViewModel viewModel, IntPtr parentHandle = default)
            : this(parentHandle)
        {
            DataContext = viewModel;
            viewModel.RequestClose += OnRequestClose;
        }

        private void OnRequestClose()
        {
            if (DataContext is ExportSheetIndexViewModel vm)
            {
                DialogResult = vm.DialogResult;
            }
            Close();
        }
    }
}

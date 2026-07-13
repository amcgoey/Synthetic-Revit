using System;
using System.Windows;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.StandardsManagement.Views
{
    public partial class SelectRevitDocumentWindow : Window
    {
        public SelectRevitDocumentWindow(SelectRevitDocumentViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

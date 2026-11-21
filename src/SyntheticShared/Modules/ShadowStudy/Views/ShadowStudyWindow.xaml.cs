using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Synthetic.Modules.ShadowStudy.ViewModels;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.ShadowStudy.Views
{
    /// <summary>
    /// Interaction logic for ShadowStudyWindow.xaml.
    /// </summary>
    public partial class ShadowStudyWindow : Window
    {
        private ShadowStudyViewModel? _boundViewModel;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShadowStudyWindow"/> class.
        /// </summary>
        public ShadowStudyWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
            Loaded += OnLoaded;
            Closed += OnWindowClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            double maxWorkAreaHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height * 0.90);
            if (Height > maxWorkAreaHeight)
            {
                Height = maxWorkAreaHeight;
            }

            if (DataContext is ShadowStudyViewModel vm && !ReferenceEquals(_boundViewModel, vm))
            {
                _boundViewModel = vm;
                _boundViewModel.RequestClose += OnRequestClose;
            }
        }

        private void OnInnerScrollPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || MainScrollViewer == null)
            {
                return;
            }

            ScrollViewer? innerScroller = sender as ScrollViewer ?? FindChildScrollViewer(sender as DependencyObject);
            bool canScrollInner = false;
            if (innerScroller != null && innerScroller.ScrollableHeight > 0.5)
            {
                if (e.Delta > 0 && innerScroller.VerticalOffset > 0.5)
                {
                    canScrollInner = true;
                }
                else if (e.Delta < 0 && innerScroller.VerticalOffset < innerScroller.ScrollableHeight - 0.5)
                {
                    canScrollInner = true;
                }
            }

            if (!canScrollInner)
            {
                e.Handled = true;
                MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - (e.Delta / 3.0));
            }
        }

        private static ScrollViewer? FindChildScrollViewer(DependencyObject? root)
        {
            if (root == null)
            {
                return null;
            }

            if (root is ScrollViewer sv)
            {
                return sv;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                ScrollViewer? found = FindChildScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void OnRequestClose(bool dialogResult)
        {
            DialogResult = dialogResult;
            Close();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (_boundViewModel != null)
            {
                _boundViewModel.RequestClose -= OnRequestClose;
                _boundViewModel = null;
            }

            Loaded -= OnLoaded;
            Closed -= OnWindowClosed;
        }
    }
}

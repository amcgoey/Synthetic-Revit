using System;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.SettingsDashboard.Handlers;

namespace Synthetic.Modules.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for SyncToastNotification.xaml.
    /// </summary>
    public partial class SyncToastNotification : Window
    {
        private readonly IntPtr _mainWindowHandle;
        private readonly Document _doc;
        private readonly string _filePath;
        private readonly Synthetic.Modules.SettingsDashboard.Handlers.SyncExternalEventHandler _handler;
        private readonly ExternalEvent _externalEvent;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncToastNotification"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        /// <param name="doc">The active Revit Document.</param>
        /// <param name="filePath">The path to the linked settings configuration JSON file.</param>
        /// <param name="handler">The external event handler for settings sync.</param>
        /// <param name="externalEvent">The external event associated with the handler.</param>
        public SyncToastNotification(IntPtr mainWindowHandle, Document doc, string filePath, Synthetic.Modules.SettingsDashboard.Handlers.SyncExternalEventHandler handler, ExternalEvent externalEvent)
        {
            InitializeComponent();
            _mainWindowHandle = mainWindowHandle;
            _doc = doc;
            _filePath = filePath;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _externalEvent = externalEvent ?? throw new ArgumentNullException(nameof(externalEvent));

            // Set parent owner handle if available
            RevitWindowHelper.SetOwner(this, _mainWindowHandle);
        }

        /// <inheritdoc/>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                // Position the toast in the bottom-right corner of the working area
                var workArea = SystemParameters.WorkArea;
                this.Left = workArea.Right - this.Width - 10;
                this.Top = workArea.Bottom - this.Height - 10;
            }
            catch
            {
                // Safe positioning fallback
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void OnResolveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var resolutionWindow = new SyncResolutionWindow(_mainWindowHandle, _doc, _filePath, _handler, _externalEvent);
                resolutionWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Resolution Error", $"Failed to open resolution window: {ex.Message}");
            }
            finally
            {
                this.Close();
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

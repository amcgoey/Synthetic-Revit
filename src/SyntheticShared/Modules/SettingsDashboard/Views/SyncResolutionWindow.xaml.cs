using System;
using System.IO;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.SettingsDashboard.Handlers;

namespace Synthetic.Modules.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for SyncResolutionWindow.xaml.
    /// </summary>
    public partial class SyncResolutionWindow : Window
    {
        private readonly Document _doc;
        private readonly string _filePath;
        private readonly Synthetic.Modules.SettingsDashboard.Handlers.SyncExternalEventHandler _handler;
        private readonly ExternalEvent _externalEvent;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncResolutionWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        /// <param name="doc">The active Revit Document.</param>
        /// <param name="filePath">The path to the linked settings configuration JSON file.</param>
        /// <param name="handler">The external event handler for settings sync.</param>
        /// <param name="externalEvent">The external event associated with the handler.</param>
        public SyncResolutionWindow(IntPtr mainWindowHandle, Document doc, string filePath, Synthetic.Modules.SettingsDashboard.Handlers.SyncExternalEventHandler handler, ExternalEvent externalEvent)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _filePath = filePath;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _externalEvent = externalEvent ?? throw new ArgumentNullException(nameof(externalEvent));

            txtDetail.Text = $"The active project configuration settings differ from the linked external settings file:\n{_filePath}\n\nChoose how to resolve this conflict:";

            // Set parent owner handle if available
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }

        private void OnPullClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _handler.QueueRequest(Handlers.SyncRequestType.Pull, _doc, _filePath);
                _externalEvent.Raise();



                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Pull Request Error", $"Failed to raise Pull request: {ex.Message}");
            }
        }

        private void OnPushClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _handler.QueueRequest(Handlers.SyncRequestType.Push, _doc, _filePath);
                _externalEvent.Raise();



                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Push Request Error", $"Failed to raise Push request: {ex.Message}");
            }
        }

        private void OnIgnoreClick(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}

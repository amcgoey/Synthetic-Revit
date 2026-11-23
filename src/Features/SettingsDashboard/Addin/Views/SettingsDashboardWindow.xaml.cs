using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Synthetic.Platform.UI;

namespace Synthetic.Features.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for SettingsDashboardWindow.xaml.
    /// </summary>
    public partial class SettingsDashboardWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsDashboardWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public SettingsDashboardWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RegisterTabDataTemplates();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }

        private void RegisterTabDataTemplates()
        {
            var genericTemplate = Resources["GenericModuleTemplate"] as DataTemplate;

            foreach (SettingsTabDescriptor tab in SettingsTabRegistry.Tabs)
            {
                DataTemplate template;
                if (tab.CustomViewType != null)
                {
                    template = new DataTemplate(tab.ViewModelType)
                    {
                        VisualTree = new FrameworkElementFactory(tab.CustomViewType)
                    };
                }
                else
                {
                    var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
                    presenterFactory.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding());
                    if (genericTemplate != null)
                    {
                        presenterFactory.SetValue(ContentPresenter.ContentTemplateProperty, genericTemplate);
                    }

                    template = new DataTemplate(tab.ViewModelType)
                    {
                        VisualTree = presenterFactory
                    };
                }

                DetailContentControl.Resources[new DataTemplateKey(tab.ViewModelType)] = template;
            }
        }
    }
}

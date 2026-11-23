using Synthetic.Features.MaterialManagement.Settings;
using Synthetic.Features.SettingsDashboard.ViewModels;
using Synthetic.Features.SettingsDashboard.Views;
using Synthetic.Features.StandardsManagement.Settings;
using Synthetic.Features.ViewManagement.Settings;
using Synthetic.Features.Worksets.Settings;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Features.DetailItemFactory.Views;
using Synthetic.Features.ShadowStudy.Adapters;
using Synthetic.Features.ShadowStudy.Settings;
using Synthetic.Features.ShadowStudy.ViewModels;
using Synthetic.Features.ShadowStudy.Views;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;

namespace Synthetic.Platform.Host
{
    /// <summary>
    /// Host composition root wiring all synchronized settings modules and Settings Dashboard tabs
    /// into <see cref="SettingsModuleRegistry"/> and <see cref="SettingsTabRegistry"/>.
    /// </summary>
    public static class HostSettingsRegistrar
    {
        private static readonly object _registrationLock = new object();
        private static bool _registered;

        /// <summary>
        /// Idempotently registers all 8 synchronized settings modules and all 9 ordered Settings Dashboard tabs.
        /// Safe to call repeatedly from <see cref="App.OnStartup"/> and test fixture setups.
        /// </summary>
        public static void EnsureRegistered()
        {
            lock (_registrationLock)
            {
                if (_registered)
                {
                    return;
                }

                // 8 synced SettingsModuleDescriptors (excluding SyncSettings)
                SettingsModuleRegistry.Register(WorksetSettings.Name, () => new WorksetSettings().Defaults());
                SettingsModuleRegistry.Register(ViewAutoNumSettings.Name, () => new ViewAutoNumSettings().Defaults());
                SettingsModuleRegistry.Register(MaterialLibrarySettings.Name, () => new MaterialLibrarySettings().Defaults());
                SettingsModuleRegistry.Register(ProjectMaterialSettings.Name, () => new ProjectMaterialSettings().Defaults());
                SettingsModuleRegistry.Register(FileUtilitySettings.Name, () => new FileUtilitySettings().Defaults());
                SettingsModuleRegistry.Register(StandardsSettings.Name, () => new StandardsSettings().Defaults());
                SettingsModuleRegistry.Register(DetailItemFactorySettings.Name, () => new DetailItemFactorySettings().Defaults());
                SettingsModuleRegistry.Register(ShadowStudySettings.Name, () => new ShadowStudySettings().Defaults());

                // 9 ordered SettingsTabDescriptors
                SettingsTabRegistry.Register((doc, hwnd) => new WorksetSettingsViewModel(doc, hwnd));
                SettingsTabRegistry.Register((doc, hwnd) => new ViewAutoNumSettingsViewModel(doc, hwnd));
                SettingsTabRegistry.Register((doc, hwnd) => new MaterialLibraryViewModel(doc, hwnd));
                SettingsTabRegistry.Register((doc, hwnd) => new ProjectMaterialsViewModel(doc, hwnd));
                SettingsTabRegistry.Register((doc, hwnd) => new FileUtilitySettingsViewModel(doc, hwnd));
                SettingsTabRegistry.Register((doc, hwnd) => new StandardsSettingsViewModel(doc, hwnd), typeof(StandardsSettingsView));
                SettingsTabRegistry.Register((doc, hwnd) => new DetailItemFactorySettingsViewModel(doc, hwnd), typeof(DetailItemFactorySettingsView));
                SettingsTabRegistry.Register((doc, hwnd) => RevitShadowStudyAdapter.CreateSettingsViewModel(doc, hwnd), typeof(ShadowStudySettingsView));
                SettingsTabRegistry.Register((doc, hwnd) => new SyncSettingsViewModel(doc, hwnd), typeof(SyncSettingsView));

                _registered = true;
            }
        }
    }
}

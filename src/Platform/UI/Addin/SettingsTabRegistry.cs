using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace Synthetic.Platform.UI
{
    /// <summary>
    /// Describes a Settings Dashboard tab ViewModel factory and optional custom view mapping.
    /// </summary>
    public sealed class SettingsTabDescriptor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsTabDescriptor"/> class.
        /// </summary>
        /// <param name="createViewModel">Factory creating the tab ViewModel for a Revit document and owner window handle.</param>
        /// <param name="viewModelType">Concrete ViewModel type implementing <see cref="ISettingModuleViewModel"/>.</param>
        /// <param name="customViewType">Optional custom WPF view type; <c>null</c> uses the shared generic module template.</param>
        public SettingsTabDescriptor(
            Func<Document, IntPtr, ISettingModuleViewModel> createViewModel,
            Type viewModelType,
            Type? customViewType = null)
        {
            CreateViewModel = createViewModel ?? throw new ArgumentNullException(nameof(createViewModel));
            ViewModelType = viewModelType ?? throw new ArgumentNullException(nameof(viewModelType));

            if (!typeof(ISettingModuleViewModel).IsAssignableFrom(viewModelType))
            {
                throw new ArgumentException(
                    $"ViewModel type '{viewModelType.FullName}' must implement {nameof(ISettingModuleViewModel)}.",
                    nameof(viewModelType));
            }

            if (customViewType != null && !typeof(FrameworkElement).IsAssignableFrom(customViewType))
            {
                throw new ArgumentException(
                    $"Custom view type '{customViewType.FullName}' must inherit from {nameof(FrameworkElement)}.",
                    nameof(customViewType));
            }

            CustomViewType = customViewType;
        }

        /// <summary>
        /// Gets the factory creating the tab ViewModel.
        /// </summary>
        public Func<Document, IntPtr, ISettingModuleViewModel> CreateViewModel { get; }

        /// <summary>
        /// Gets the ViewModel type used as the WPF implicit <see cref="DataTemplate"/> data type key.
        /// </summary>
        public Type ViewModelType { get; }

        /// <summary>
        /// Gets the optional custom WPF view type, or <c>null</c> to render using the generic module template.
        /// </summary>
        public Type? CustomViewType { get; }
    }

    /// <summary>
    /// Host-populated registry of ordered Settings Dashboard tab descriptors.
    /// </summary>
    public static class SettingsTabRegistry
    {
        private static readonly object _syncLock = new object();
        private static readonly List<SettingsTabDescriptor> _tabs = new List<SettingsTabDescriptor>();
        private static readonly HashSet<Type> _registeredViewModelTypes = new HashSet<Type>();

        /// <summary>
        /// Gets the ordered list of registered Settings Dashboard tab descriptors.
        /// </summary>
        public static IReadOnlyList<SettingsTabDescriptor> Tabs
        {
            get
            {
                lock (_syncLock)
                {
                    return new ReadOnlyCollection<SettingsTabDescriptor>(_tabs.ToList());
                }
            }
        }

        /// <summary>
        /// Idempotently registers a Settings Dashboard tab descriptor by <see cref="SettingsTabDescriptor.ViewModelType"/>.
        /// </summary>
        public static void Register(SettingsTabDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            lock (_syncLock)
            {
                if (_registeredViewModelTypes.Add(descriptor.ViewModelType))
                {
                    _tabs.Add(descriptor);
                }
            }
        }

        /// <summary>
        /// Idempotently registers a Settings Dashboard tab descriptor for <typeparamref name="TViewModel"/>.
        /// </summary>
        public static void Register<TViewModel>(
            Func<Document, IntPtr, TViewModel> createViewModel,
            Type? customViewType = null)
            where TViewModel : ISettingModuleViewModel
        {
            if (createViewModel == null) throw new ArgumentNullException(nameof(createViewModel));

            Register(new SettingsTabDescriptor(
                (doc, hwnd) => createViewModel(doc, hwnd),
                typeof(TViewModel),
                customViewType));
        }
    }
}

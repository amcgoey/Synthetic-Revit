using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Shell;
using NUnit.Framework;
using Synthetic.Platform.UI;
using SyntheticTests.Helpers;

namespace SyntheticTests.Platform.UI
{
    /// <summary>
    /// Headless unit tests for <see cref="WindowChromeBehavior"/>, UI value converters,
    /// and the <c>SyntheticTheme.xaml</c> resource dictionary.
    /// These tests run on a dedicated STA thread (required by WPF) without creating
    /// a process-global <see cref="Application"/> instance or requiring a live Revit host.
    /// </summary>
    [TestFixture]
    [UnitTest]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class ThemeTests
    {
        private enum SampleMode
        {
            Alpha,
            Beta
        }

        private sealed class FakeSelectionViewModel : ISingleItemSelectionViewModel
        {
            public Action<bool>? CloseAction { get; set; }

            public string GetItemDisplayName(object item)
            {
                return item is int id ? $"Item #{id}" : item?.ToString() ?? string.Empty;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // WindowChromeBehavior tests
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void WindowChromeBehavior_AttachCommands_AddsCommandBindings()
        {
            var window = new Window();

            WindowChromeBehavior.SetEnableWindowCommands(window, true);

            bool hasClose = false;
            bool hasMinimize = false;
            bool hasMaximize = false;
            bool hasRestore = false;

            foreach (CommandBinding cb in window.CommandBindings)
            {
                if (cb.Command == SystemCommands.CloseWindowCommand) hasClose = true;
                if (cb.Command == SystemCommands.MinimizeWindowCommand) hasMinimize = true;
                if (cb.Command == SystemCommands.MaximizeWindowCommand) hasMaximize = true;
                if (cb.Command == SystemCommands.RestoreWindowCommand) hasRestore = true;
            }

            Assert.IsTrue(hasClose, "CloseWindowCommand binding must be registered.");
            Assert.IsTrue(hasMinimize, "MinimizeWindowCommand binding must be registered.");
            Assert.IsTrue(hasMaximize, "MaximizeWindowCommand binding must be registered.");
            Assert.IsTrue(hasRestore, "RestoreWindowCommand binding must be registered.");

            var chrome = WindowChrome.GetWindowChrome(window);
            Assert.IsNotNull(chrome, "WindowChrome should be applied by the behavior.");
            Assert.AreEqual(45, chrome!.CaptionHeight, "CaptionHeight should be 45.");
        }

        [Test]
        public void WindowChromeBehavior_DetachCommands_RemovesCommandBindings()
        {
            var window = new Window();
            WindowChromeBehavior.SetEnableWindowCommands(window, true);

            WindowChromeBehavior.SetEnableWindowCommands(window, false);

            foreach (CommandBinding cb in window.CommandBindings)
            {
                Assert.IsFalse(
                    cb.Command == SystemCommands.CloseWindowCommand ||
                    cb.Command == SystemCommands.MinimizeWindowCommand ||
                    cb.Command == SystemCommands.MaximizeWindowCommand ||
                    cb.Command == SystemCommands.RestoreWindowCommand,
                    "All SystemCommands bindings should have been removed.");
            }

            Assert.IsNull(WindowChrome.GetWindowChrome(window),
                "WindowChrome should be null after detach.");
        }

        [Test]
        public void WindowChromeBehavior_GetDefault_ReturnsFalse()
        {
            var window = new Window();

            bool value = WindowChromeBehavior.GetEnableWindowCommands(window);

            Assert.IsFalse(value, "EnableWindowCommands should default to false.");
        }

        // ─────────────────────────────────────────────────────────────────
        // Core Converter tests
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void EnumToBooleanConverter_ConvertsAndConvertsBackMatchingValues()
        {
            var converter = new EnumToBooleanConverter();

            Assert.AreEqual(true, converter.Convert(SampleMode.Beta, typeof(bool), SampleMode.Beta, CultureInfo.InvariantCulture));
            Assert.AreEqual(false, converter.Convert(SampleMode.Alpha, typeof(bool), SampleMode.Beta, CultureInfo.InvariantCulture));
            Assert.AreEqual(DependencyProperty.UnsetValue, converter.Convert(null!, typeof(bool), SampleMode.Beta, CultureInfo.InvariantCulture));

            Assert.AreEqual(SampleMode.Beta, converter.ConvertBack(true, typeof(SampleMode), SampleMode.Beta, CultureInfo.InvariantCulture));
            Assert.AreEqual(DependencyProperty.UnsetValue, converter.ConvertBack(false, typeof(SampleMode), SampleMode.Beta, CultureInfo.InvariantCulture));
        }

        [Test]
        public void SingleItemSelectionDisplayConverter_UsesViewModelDelegateAndFallsBackToString()
        {
            var converter = new SingleItemSelectionDisplayConverter();
            var vm = new FakeSelectionViewModel();

            object display = converter.Convert(new object[] { 42, vm }, typeof(string), null!, CultureInfo.InvariantCulture);
            object fallback = converter.Convert(new object[] { "PlainText" }, typeof(string), null!, CultureInfo.InvariantCulture);

            Assert.AreEqual("Item #42", display);
            Assert.AreEqual("PlainText", fallback);
        }

        // ─────────────────────────────────────────────────────────────────
        // SyntheticTheme.xaml resource-loading tests
        // ─────────────────────────────────────────────────────────────────

        private static string GetThemeXamlPath()
        {
            string? baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                string localTarget = Path.Combine(baseDir, "SyntheticTheme.xaml");
                if (File.Exists(localTarget))
                {
                    return Path.GetFullPath(localTarget);
                }
            }

            string projectRoot = TestPathHelper.GetProjectRoot();
            string sourceTarget = Path.Combine(projectRoot, "src", "Platform", "UI", "Addin", "SyntheticTheme.xaml");
            if (File.Exists(sourceTarget))
            {
                return Path.GetFullPath(sourceTarget);
            }

            throw new FileNotFoundException("Could not locate SyntheticTheme.xaml.", sourceTarget);
        }

        private static ResourceDictionary LoadThemeDictionary()
        {
            using var stream = File.OpenRead(GetThemeXamlPath());
            return (ResourceDictionary)XamlReader.Load(stream);
        }

        [Test]
        public void SyntheticTheme_LoadDictionary_DoesNotThrow()
        {
            ResourceDictionary? dict = null;
            Assert.DoesNotThrow(
                () => { dict = LoadThemeDictionary(); },
                "SyntheticTheme.xaml should load via XamlReader without exceptions.");

            Assert.IsNotNull(dict, "Loaded ResourceDictionary should not be null.");
        }

        [Test]
        public void SyntheticTheme_ContainsCoreColorKeys_AllPresent()
        {
            var dict = LoadThemeDictionary();

            string[] requiredColorKeys =
            {
                "Synthetic.Colors.BackgroundBase",
                "Synthetic.Colors.ControlSurface",
                "Synthetic.Colors.ControlSurfaceLighter",
                "Synthetic.Colors.BorderNormal",
                "Synthetic.Colors.AccentActive",
                "Synthetic.Colors.TextPrimary",
                "Synthetic.Colors.TextSecondary",
                "Synthetic.Colors.TextDark",
                "Synthetic.Colors.Success",
                "Synthetic.Colors.Warning",
                "Synthetic.Colors.Error",
            };

            foreach (string key in requiredColorKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define resource key '{key}'.");
            }
        }

        [Test]
        public void SyntheticTheme_ContainsCoreBrushKeys_AllPresent()
        {
            var dict = LoadThemeDictionary();

            string[] requiredBrushKeys =
            {
                "Synthetic.Brushes.BackgroundBase",
                "Synthetic.Brushes.ControlSurface",
                "Synthetic.Brushes.ControlSurfaceLighter",
                "Synthetic.Brushes.BorderNormal",
                "Synthetic.Brushes.AccentActive",
                "Synthetic.Brushes.TextPrimary",
                "Synthetic.Brushes.TextSecondary",
                "Synthetic.Brushes.TextDark",
                "Synthetic.Brushes.Success",
                "Synthetic.Brushes.Warning",
                "Synthetic.Brushes.Error",
                "Synthetic.Brushes.Transparent",
            };

            foreach (string key in requiredBrushKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define resource key '{key}'.");
            }
        }

        [Test]
        public void SyntheticTheme_ContainsWindowStyle_Present()
        {
            var dict = LoadThemeDictionary();

            Assert.IsTrue(dict.Contains("SyntheticWindowStyle"),
                "SyntheticTheme.xaml must define the 'SyntheticWindowStyle' window style.");
        }

        [Test]
        public void SyntheticTheme_ContainsKeyedControlStyles_AllPresent()
        {
            var dict = LoadThemeDictionary();

            string[] requiredStyleKeys =
            {
                "Synthetic.Styles.PrimaryButton",
                "Synthetic.Styles.NeutralButton",
                "Synthetic.Internal.ComboBoxToggleButton",
                "Synthetic.Styles.PrimaryButton.Right",
                "Synthetic.Styles.NeutralButton.Right",
                "Synthetic.Styles.SecondaryButton.Right",
                "Synthetic.Margins.RightAction",
                "Synthetic.Styles.ExpanderHeaderToggle"
            };

            foreach (string key in requiredStyleKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define keyed style '{key}'.");
            }
        }

        [Test]
        public void SyntheticTheme_ImplicitControlStyles_CanBeRetrieved()
        {
            var dict = LoadThemeDictionary();

            var types = new[]
            {
                typeof(System.Windows.Controls.Button),
                typeof(System.Windows.Controls.TextBox),
                typeof(System.Windows.Controls.CheckBox),
                typeof(System.Windows.Controls.ComboBox),
                typeof(System.Windows.Controls.ComboBoxItem),
                typeof(System.Windows.Controls.TabControl),
                typeof(System.Windows.Controls.TabItem),
                typeof(System.Windows.Controls.Primitives.ScrollBar),
                typeof(System.Windows.Controls.ToolTip),
                typeof(System.Windows.Controls.Expander),
                typeof(System.Windows.Controls.RadioButton),
                typeof(System.Windows.Controls.DatePicker),
                typeof(System.Windows.Controls.Primitives.DatePickerTextBox),
                typeof(System.Windows.Controls.Calendar),
                typeof(System.Windows.Controls.Primitives.CalendarItem),
                typeof(System.Windows.Controls.Primitives.CalendarButton),
                typeof(System.Windows.Controls.Primitives.CalendarDayButton),
            };

            foreach (var type in types)
            {
                object? style = dict[type];
                Assert.IsNotNull(style,
                    $"SyntheticTheme.xaml must define an implicit style for {type.Name}.");
            }
        }

        [Test]
        public void SyntheticTheme_ContainsDataGridAndTreeViewKeyedStyles_AllPresent()
        {
            var dict = LoadThemeDictionary();

            string[] requiredKeys =
            {
                "Synthetic.Internal.TreeViewItemToggle",
            };

            foreach (string key in requiredKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define keyed style '{key}'.");
            }
        }

        [Test]
        public void SyntheticTheme_ImplicitDataGridAndTreeViewStyles_CanBeRetrieved()
        {
            var dict = LoadThemeDictionary();

            var types = new[]
            {
                typeof(System.Windows.Controls.DataGrid),
                typeof(System.Windows.Controls.DataGridRow),
                typeof(System.Windows.Controls.DataGridCell),
                typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
                typeof(System.Windows.Controls.TreeView),
                typeof(System.Windows.Controls.TreeViewItem),
            };

            foreach (var type in types)
            {
                object? style = dict[type];
                Assert.IsNotNull(style,
                    $"SyntheticTheme.xaml must define an implicit style for {type.Name}.");
            }
        }

        [Test]
        public void SyntheticTheme_ContainsVectorGeometryResources_AllPresent()
        {
            var dict = LoadThemeDictionary();

            string[] geometryKeys =
            {
                "Synthetic.Geometries.Folder",
                "Synthetic.Geometries.File",
                "Synthetic.Geometries.Warning",
                "Synthetic.Geometries.Blocked",
                "Synthetic.Geometries.Success",
                "Synthetic.Geometries.Info",
                "Synthetic.Geometries.Gear",
            };

            foreach (string key in geometryKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define geometry key '{key}'.");

                object resource = dict[key];
                Assert.IsInstanceOf<System.Windows.Media.Geometry>(resource,
                    $"Resource '{key}' must be of type System.Windows.Media.Geometry.");
            }
        }
    }
}

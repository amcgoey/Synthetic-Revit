using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Shell;
using NUnit.Framework;
using Synthetic.Shared.UI;

namespace SyntheticTests.Infrastructure.UI
{
    /// <summary>
    /// Tier 1 (Logic) tests for the <see cref="WindowChromeBehavior"/> attached property
    /// and the <c>SyntheticTheme.xaml</c> resource dictionary.
    ///
    /// These tests run on a dedicated STA thread (required by WPF) and do not
    /// require a live Revit host.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ThemeTests
    {
        /// <summary>
        /// Bootstraps the WPF Application infrastructure required to resolve
        /// <c>pack://</c> URIs in a headless NUnit process.  Without an
        /// <see cref="Application"/> instance the pack URI scheme is not
        /// registered, causing <see cref="ResourceDictionary"/> loads to fail.
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (Application.Current == null)
            {
                // Creating an Application instance registers the pack:// scheme.
                // We deliberately do NOT call Run() – that would block the thread.
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // WindowChromeBehavior tests
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void WindowChromeBehavior_AttachCommands_AddsCommandBindings()
        {
            // Arrange
            var window = new Window();

            // Act
            WindowChromeBehavior.SetEnableWindowCommands(window, true);

            // Assert – all four SystemCommands bindings must be present
            bool hasClose    = false;
            bool hasMinimize = false;
            bool hasMaximize = false;
            bool hasRestore  = false;

            foreach (CommandBinding cb in window.CommandBindings)
            {
                if (cb.Command == SystemCommands.CloseWindowCommand)    hasClose    = true;
                if (cb.Command == SystemCommands.MinimizeWindowCommand)  hasMinimize = true;
                if (cb.Command == SystemCommands.MaximizeWindowCommand)  hasMaximize = true;
                if (cb.Command == SystemCommands.RestoreWindowCommand)   hasRestore  = true;
            }

            Assert.IsTrue(hasClose,    "CloseWindowCommand binding must be registered.");
            Assert.IsTrue(hasMinimize, "MinimizeWindowCommand binding must be registered.");
            Assert.IsTrue(hasMaximize, "MaximizeWindowCommand binding must be registered.");
            Assert.IsTrue(hasRestore,  "RestoreWindowCommand binding must be registered.");

            // WindowChrome must be applied
            var chrome = WindowChrome.GetWindowChrome(window);
            Assert.IsNotNull(chrome, "WindowChrome should be applied by the behavior.");
            Assert.AreEqual(45, chrome!.CaptionHeight, "CaptionHeight should be 45.");
        }

        [Test]
        public void WindowChromeBehavior_DetachCommands_RemovesCommandBindings()
        {
            // Arrange
            var window = new Window();
            WindowChromeBehavior.SetEnableWindowCommands(window, true);

            // Act
            WindowChromeBehavior.SetEnableWindowCommands(window, false);

            // Assert – none of the four SystemCommands bindings should remain
            foreach (CommandBinding cb in window.CommandBindings)
            {
                Assert.IsFalse(
                    cb.Command == SystemCommands.CloseWindowCommand    ||
                    cb.Command == SystemCommands.MinimizeWindowCommand  ||
                    cb.Command == SystemCommands.MaximizeWindowCommand  ||
                    cb.Command == SystemCommands.RestoreWindowCommand,
                    "All SystemCommands bindings should have been removed.");
            }

            // WindowChrome must be removed
            Assert.IsNull(WindowChrome.GetWindowChrome(window),
                "WindowChrome should be null after detach.");
        }

        [Test]
        public void WindowChromeBehavior_GetDefault_ReturnsFalse()
        {
            // Arrange
            var window = new Window();

            // Act
            bool value = WindowChromeBehavior.GetEnableWindowCommands(window);

            // Assert – property should default to false (not attached)
            Assert.IsFalse(value, "EnableWindowCommands should default to false.");
        }

        // ─────────────────────────────────────────────────────────────────
        // SyntheticTheme.xaml resource-loading tests
        //
        // NOTE: SyntheticShared is a shared .projitems — there is no standalone
        // SyntheticShared.dll, so pack:// URIs cannot resolve in the test
        // process.  Instead we load the XAML source file directly via
        // XamlReader.Load(), which gives identical coverage of the resource keys.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the absolute path to <c>SyntheticTheme.xaml</c> by walking
        /// from the test assembly output directory back to the solution source tree.
        /// </summary>
        private static string GetThemeXamlPath()
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.GetDirectoryName(typeof(ThemeTests).Assembly.Location);
            }

            while (dir is not null)
            {
                string target1 = Path.Combine(dir, "src", "SyntheticShared", "Shared", "UI", "SyntheticTheme.xaml");
                if (File.Exists(target1)) return Path.GetFullPath(target1);

                string target2 = Path.Combine(dir, "SyntheticShared", "Shared", "UI", "SyntheticTheme.xaml");
                if (File.Exists(target2)) return Path.GetFullPath(target2);

                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException("Could not locate SyntheticTheme.xaml by walking up from test directory.");
        }

        private static ResourceDictionary LoadThemeDictionary()
        {
            using var stream = File.OpenRead(GetThemeXamlPath());
            return (ResourceDictionary)XamlReader.Load(stream);
        }

        [Test]
        public void SyntheticTheme_LoadDictionary_DoesNotThrow()
        {
            // Act & Assert – loading the dictionary must not throw any exception
            ResourceDictionary? dict = null;
            Assert.DoesNotThrow(
                () => { dict = LoadThemeDictionary(); },
                "SyntheticTheme.xaml should load via XamlReader without exceptions.");

            Assert.IsNotNull(dict, "Loaded ResourceDictionary should not be null.");
        }

        [Test]
        public void SyntheticTheme_ContainsCoreColorKeys_AllPresent()
        {
            // Arrange
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

            // Act & Assert
            foreach (string key in requiredColorKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define resource key '{key}'.");
            }
        }

        [Test]
        public void SyntheticTheme_ContainsCoreBrushKeys_AllPresent()
        {
            // Arrange
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

            // Act & Assert
            foreach (string key in requiredBrushKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define resource key '{key}'.");
            }
        }

        [Test]
        public void SyntheticTheme_ContainsWindowStyle_Present()
        {
            // Arrange
            var dict = LoadThemeDictionary();

            // Act & Assert
            Assert.IsTrue(dict.Contains("SyntheticWindowStyle"),
                "SyntheticTheme.xaml must define the 'SyntheticWindowStyle' window style.");
        }

        // ─────────────────────────────────────────────────────────────────
        // DB 006-02 — Control template / style key tests
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void SyntheticTheme_ContainsKeyedControlStyles_AllPresent()
        {
            // Arrange
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

            // Act & Assert
            foreach (string key in requiredStyleKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define keyed style '{key}'.");
            }
        }

        [Test]
        public void SyntheticTheme_ImplicitControlStyles_CanBeRetrieved()
        {
            // Arrange
            var dict = LoadThemeDictionary();

            // WPF implicit style keys are Type objects, not strings.
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
            };

            foreach (var type in types)
            {
                object? style = dict[type];
                Assert.IsNotNull(style,
                    $"SyntheticTheme.xaml must define an implicit style for {type.Name}.");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // DB 006-03 — DataGrid / TreeView template key tests
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void SyntheticTheme_ContainsDataGridAndTreeViewKeyedStyles_AllPresent()
        {
            // Arrange
            var dict = LoadThemeDictionary();

            string[] requiredKeys =
            {
                "Synthetic.Internal.TreeViewItemToggle",
            };

            // Act & Assert
            foreach (string key in requiredKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define keyed style '{key}'.");
            }
        }

        [Test]
        public void SyntheticTheme_ImplicitDataGridAndTreeViewStyles_CanBeRetrieved()
        {
            // Arrange
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

            // Act & Assert
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
            // Arrange
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

            // Act & Assert
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
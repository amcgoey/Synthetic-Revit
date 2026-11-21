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

            if (!string.IsNullOrEmpty(dir))
            {
                string localTarget = Path.Combine(dir, "SyntheticTheme.xaml");
                if (File.Exists(localTarget)) return Path.GetFullPath(localTarget);
            }

            string? envRoot = Environment.GetEnvironmentVariable("SYNTHETIC_PROJECT_ROOT");
            if (!string.IsNullOrEmpty(envRoot))
            {
                string envTarget = Path.Combine(envRoot, "src", "SyntheticShared", "Shared", "UI", "SyntheticTheme.xaml");
                if (File.Exists(envTarget)) return Path.GetFullPath(envTarget);
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

        [Test]
        public void SharedProgressWindow_UserClose_TriggersCancellation()
        {
            using var cts = new System.Threading.CancellationTokenSource();
            var vm = new SharedProgressViewModel(cts);
            var window = new SharedProgressWindow(IntPtr.Zero, vm);
            window.Show();

            window.Close();

            Assert.IsTrue(vm.IsCancellationRequested, "User closing SharedProgressWindow must trigger cancellation.");
            Assert.IsTrue(cts.IsCancellationRequested, "CancellationTokenSource must be cancelled on user close.");
        }

        [Test]
        public void SharedProgressWindow_ProgrammaticClose_DoesNotTriggerCancellation()
        {
            using var cts = new System.Threading.CancellationTokenSource();
            var vm = new SharedProgressViewModel(cts);
            var window = new SharedProgressWindow(IntPtr.Zero, vm)
            {
                AllowProgrammaticClose = true
            };
            window.Show();

            window.Close();

            Assert.IsFalse(vm.IsCancellationRequested, "Programmatic close must not trigger cancellation.");
            Assert.IsFalse(cts.IsCancellationRequested, "CancellationTokenSource must remain active on programmatic close.");
        }

        [Test]
        public void ShadowStudyWindow_ConstructsAndBindsInStaWithoutXamlErrors()
        {
            var adapter = new SyntheticTests.Modules.ShadowStudy.FakeShadowStudyAdapter();
            var folderDialog = new SyntheticTests.Modules.ShadowStudy.FakeFolderDialogService();
            var vm = new Synthetic.Modules.ShadowStudy.ViewModels.ShadowStudyViewModel(adapter, folderDialog, new DateTime(2026, 6, 21));
            vm.PreserveVisualStyle = true;

            Synthetic.Modules.ShadowStudy.Views.ShadowStudyWindow? window = null;
            Assert.DoesNotThrow(() =>
            {
                window = new Synthetic.Modules.ShadowStudy.Views.ShadowStudyWindow(IntPtr.Zero) { DataContext = vm };
                window.ApplyTemplate();
            });

            Assert.IsNotNull(window);
            window!.Close();
        }

        [Test]
        public void ShadowStudyWindow_WithDefaultSeasonalStudyPreset_ConstructsAndBindsInStaWithoutXamlErrors()
        {
            var adapter = new SyntheticTests.Modules.ShadowStudy.FakeShadowStudyAdapter();
            var folderDialog = new SyntheticTests.Modules.ShadowStudy.FakeFolderDialogService();
            var vm = new Synthetic.Modules.ShadowStudy.ViewModels.ShadowStudyViewModel(adapter, folderDialog);

            Synthetic.Modules.ShadowStudy.Views.ShadowStudyWindow? window = null;
            Assert.DoesNotThrow(() =>
            {
                window = new Synthetic.Modules.ShadowStudy.Views.ShadowStudyWindow(IntPtr.Zero) { DataContext = vm };
                window.ApplyTemplate();
                window.Measure(new Size(620, 900));
                window.Arrange(new Rect(0, 0, 620, 900));
                window.UpdateLayout();
            });

            Assert.IsNotNull(window);
            window!.Close();
        }

        [Test]
        public void ShadowStudyWindow_SelectedViewsChecklist_BindsAndSwitchesModesWithoutXamlErrors()
        {
            var adapter = new SyntheticTests.Modules.ShadowStudy.FakeShadowStudyAdapter
            {
                ActiveView = null
            };
            adapter.EligibleViews.Add(new Synthetic.Modules.ShadowStudy.Models.ShadowStudyViewItem
            {
                Id = "v1",
                Name = "Level 1",
                ViewType = "FloorPlan",
                IsCropped = false,
                Warnings =
                {
                    new Synthetic.Modules.ShadowStudy.Models.ShadowStudyViewWarningBadge
                    {
                        Code = "Uncropped",
                        Label = "Uncropped View",
                        Description = "Crop View is off."
                    },
                    new Synthetic.Modules.ShadowStudy.Models.ShadowStudyViewWarningBadge
                    {
                        Code = "VisualStyle",
                        Label = "Hidden Line Export",
                        Description = "Wireframe will use HLR."
                    }
                }
            });

            var folderDialog = new SyntheticTests.Modules.ShadowStudy.FakeFolderDialogService();
            var vm = new Synthetic.Modules.ShadowStudy.ViewModels.ShadowStudyViewModel(adapter, folderDialog, new DateTime(2026, 6, 21));
            vm.PreserveVisualStyle = true;

            Synthetic.Modules.ShadowStudy.Views.ShadowStudyWindow? window = null;
            Assert.DoesNotThrow(() =>
            {
                window = new Synthetic.Modules.ShadowStudy.Views.ShadowStudyWindow(IntPtr.Zero) { DataContext = vm };
                window.ApplyTemplate();
                window.Measure(new Size(620, 900));
                window.Arrange(new Rect(0, 0, 620, 900));
                window.UpdateLayout();
            });

            Assert.IsNotNull(window);
            window!.Close();
        }

        [Test]
        public void ShadowStudySettingsView_ConstructsAndBindsInStaWithoutXamlErrors()
        {
            var stored = new Synthetic.Modules.ShadowStudy.Settings.ShadowStudySettings
            {
                UserPresets = new System.Collections.Generic.List<Synthetic.Modules.ShadowStudy.Settings.ShadowStudyPresetModel>
                {
                    new Synthetic.Modules.ShadowStudy.Settings.ShadowStudyPresetModel
                    {
                        Name = "STA Smoke Preset",
                        Dates = new System.Collections.Generic.List<Synthetic.Modules.ShadowStudy.Models.ShadowStudyPresetDateSpec>
                        {
                            new Synthetic.Modules.ShadowStudy.Models.ShadowStudyPresetDateSpec
                            {
                                AstronomicalEvent = Synthetic.Modules.ShadowStudy.Models.AstronomicalEventKind.SummerSolstice
                            }
                        }
                    }
                }
            };

            var vm = new Synthetic.Modules.ShadowStudy.ViewModels.ShadowStudySettingsViewModel(
                () => stored,
                s => stored = s.DeepClone(),
                () => { },
                () => true);

            Synthetic.Modules.ShadowStudy.Views.ShadowStudySettingsView? view = null;
            Assert.DoesNotThrow(() =>
            {
                view = new Synthetic.Modules.ShadowStudy.Views.ShadowStudySettingsView { DataContext = vm };
                view.ApplyTemplate();
                view.Measure(new Size(500, 600));
                view.Arrange(new Rect(0, 0, 500, 600));
                view.UpdateLayout();
            });

            Assert.IsNotNull(view);
        }

        [Test]
        public void ShadowStudyWindow_CompactHeight_KeepsExportAndCancelFooterWithinWindowBounds()
        {
            var adapter = new SyntheticTests.Modules.ShadowStudy.FakeShadowStudyAdapter
            {
                ActiveView = null
            };
            adapter.EligibleViews.Add(new Synthetic.Modules.ShadowStudy.Models.ShadowStudyViewItem
            {
                Id = "v1",
                Name = "Level 1",
                ViewType = "FloorPlan",
                IsCropped = true
            });

            var folderDialog = new SyntheticTests.Modules.ShadowStudy.FakeFolderDialogService();
            var vm = new Synthetic.Modules.ShadowStudy.ViewModels.ShadowStudyViewModel(adapter, folderDialog);
            vm.OutputFolder = @"C:\Temp\ShadowOut";
            vm.FilteredViews[0].IsSelected = true;

            var window = new Synthetic.Modules.ShadowStudy.Views.ShadowStudyWindow(IntPtr.Zero) { DataContext = vm };
            try
            {
                Assert.AreEqual(ResizeMode.CanResizeWithGrip, window.ResizeMode);

                var rootGrid = window.Content as System.Windows.Controls.Grid;
                Assert.IsNotNull(rootGrid);

                rootGrid!.Measure(new Size(640, 520));
                rootGrid.Arrange(new Rect(0, 0, 640, 520));
                rootGrid.UpdateLayout();

                System.Windows.Controls.Button? exportButton = FindButtonByContent(rootGrid, "Export");
                System.Windows.Controls.Button? cancelButton = FindButtonByContent(rootGrid, "Cancel");
                Assert.IsNotNull(exportButton, "Export button must exist in ShadowStudyWindow.");
                Assert.IsNotNull(cancelButton, "Cancel button must exist in ShadowStudyWindow.");

                Point exportBottomRight = exportButton!.TranslatePoint(
                    new Point(exportButton.RenderSize.Width, exportButton.RenderSize.Height),
                    rootGrid);
                Assert.LessOrEqual(exportBottomRight.Y, 520.5,
                    "Pinned Export footer button must remain visible inside compact 520px content bounds without clipping.");

                var mainScroller = window.FindName("MainScrollViewer") as System.Windows.Controls.ScrollViewer;
                Assert.IsNotNull(mainScroller, "MainScrollViewer must exist in ShadowStudyWindow.");
                Assert.Greater(mainScroller!.ScrollableHeight, 0.0,
                    "MainScrollViewer must provide vertical scrolling when sections exceed compact window height.");

                System.Windows.Controls.ListBox? viewsListBox = FindFirstOfType<System.Windows.Controls.ListBox>(rootGrid);
                Assert.IsNotNull(viewsListBox);
                var wheelArgs = new System.Windows.Input.MouseWheelEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice,
                    Environment.TickCount,
                    -120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    Source = viewsListBox
                };
                viewsListBox!.RaiseEvent(wheelArgs);
                rootGrid.UpdateLayout();

                Assert.IsTrue(wheelArgs.Handled, "Boundary mouse-wheel event on inner ListBox must be forwarded to MainScrollViewer.");
                Assert.Greater(mainScroller.VerticalOffset, 0.0, "MainScrollViewer must scroll downward when wheel rolls over non-overflowing inner ListBox.");
            }
            finally
            {
                window.Close();
            }
        }

        private static T? FindFirstOfType<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                {
                    return typed;
                }

                T? nested = FindFirstOfType<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static System.Windows.Controls.Button? FindButtonByContent(DependencyObject parent, string expectedContent)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is System.Windows.Controls.Button btn && string.Equals(btn.Content as string, expectedContent, StringComparison.Ordinal))
                {
                    return btn;
                }

                System.Windows.Controls.Button? nested = FindButtonByContent(child, expectedContent);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
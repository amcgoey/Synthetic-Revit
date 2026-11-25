using System;
using System.Threading;
using System.Windows;
using NUnit.Framework;
using Synthetic.Platform.UI;

namespace SyntheticTests.Infrastructure.UI
{
    /// <summary>
    /// Headless STA tests verifying <c>Synthetic2027</c> WPF views that merge
    /// <see cref="SyntheticThemeDictionary"/> construct, apply templates, and bind cleanly
    /// inside an external test host process.
    /// </summary>
    [TestFixture]
    [QuarantineTest]
    [Apartment(ApartmentState.STA)]
    public class AddinThemeViewTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (Application.Current == null)
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }
        }

        [Test]
        public void SharedProgressWindow_UserClose_TriggersCancellation()
        {
            using var cts = new CancellationTokenSource();
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
            using var cts = new CancellationTokenSource();
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
        public void ExportCompletionWindow_ConstructsWithThemeAndBindsViewModel()
        {
            var batchVm = ExportCompletionViewModel.ForBatch(
                outputFolder: @"C:\Exports\ShadowStudy",
                generatedCount: 3,
                primaryFilePath: @"C:\Exports\ShadowStudy\Composite.png",
                warningNotices: new[] { "1 date skipped." },
                windowTitle: "Shadow Study Export Complete",
                headerTitle: "Shadow Study Complete");

            var window = new ExportCompletionWindow(batchVm);
            Assert.AreSame(batchVm, window.DataContext);
            Assert.AreSame(batchVm, window.ViewModel);
            Assert.AreEqual("Shadow Study Export Complete", window.Title);
            window.Close();

            var legacyWindow = new ExportCompletionWindow(@"C:\Exports\Dot Sheet Index.xlsx");
            Assert.IsNotNull(legacyWindow.ViewModel);
            Assert.AreEqual("Open File", legacyWindow.ViewModel.PrimaryActionLabel);
            Assert.AreEqual(@"C:\Exports\Dot Sheet Index.xlsx", legacyWindow.ViewModel.DestinationPath);
            legacyWindow.Close();
        }

        [Test]
        public void ShadowStudyWindow_ConstructsAndBindsInStaWithoutXamlErrors()
        {
            var adapter = new SyntheticTests.Fixtures.FakeShadowStudyAdapter();
            var folderDialog = new SyntheticTests.Fixtures.FakeFolderDialogService();
            var vm = new Synthetic.Features.ShadowStudy.ViewModels.ShadowStudyViewModel(adapter, folderDialog, new DateTime(2026, 6, 21));
            vm.PreserveVisualStyle = true;

            Synthetic.Features.ShadowStudy.Views.ShadowStudyWindow? window = null;
            Assert.DoesNotThrow(() =>
            {
                window = new Synthetic.Features.ShadowStudy.Views.ShadowStudyWindow(IntPtr.Zero) { DataContext = vm };
                window.ApplyTemplate();
            });

            Assert.IsNotNull(window);
            window!.Close();
        }

        [Test]
        public void ShadowStudyWindow_WithDefaultSeasonalStudyPreset_ConstructsAndBindsInStaWithoutXamlErrors()
        {
            var adapter = new SyntheticTests.Fixtures.FakeShadowStudyAdapter();
            var folderDialog = new SyntheticTests.Fixtures.FakeFolderDialogService();
            var vm = new Synthetic.Features.ShadowStudy.ViewModels.ShadowStudyViewModel(adapter, folderDialog);

            Synthetic.Features.ShadowStudy.Views.ShadowStudyWindow? window = null;
            Assert.DoesNotThrow(() =>
            {
                window = new Synthetic.Features.ShadowStudy.Views.ShadowStudyWindow(IntPtr.Zero) { DataContext = vm };
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
            var adapter = new SyntheticTests.Fixtures.FakeShadowStudyAdapter
            {
                ActiveView = null
            };
            adapter.EligibleViews.Add(new Synthetic.Features.ShadowStudy.Models.ShadowStudyViewItem
            {
                Id = "v1",
                Name = "Level 1",
                ViewType = "FloorPlan",
                IsCropped = false,
                Warnings =
                {
                    new Synthetic.Features.ShadowStudy.Models.ShadowStudyViewWarningBadge
                    {
                        Code = "Uncropped",
                        Label = "Uncropped View",
                        Description = "Crop View is off."
                    },
                    new Synthetic.Features.ShadowStudy.Models.ShadowStudyViewWarningBadge
                    {
                        Code = "VisualStyle",
                        Label = "Hidden Line Export",
                        Description = "Wireframe will use HLR."
                    }
                }
            });

            var folderDialog = new SyntheticTests.Fixtures.FakeFolderDialogService();
            var vm = new Synthetic.Features.ShadowStudy.ViewModels.ShadowStudyViewModel(adapter, folderDialog, new DateTime(2026, 6, 21));
            vm.PreserveVisualStyle = true;

            Synthetic.Features.ShadowStudy.Views.ShadowStudyWindow? window = null;
            Assert.DoesNotThrow(() =>
            {
                window = new Synthetic.Features.ShadowStudy.Views.ShadowStudyWindow(IntPtr.Zero) { DataContext = vm };
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
            var stored = new Synthetic.Features.ShadowStudy.Settings.ShadowStudySettings
            {
                UserPresets = new System.Collections.Generic.List<Synthetic.Features.ShadowStudy.Settings.ShadowStudyPresetModel>
                {
                    new Synthetic.Features.ShadowStudy.Settings.ShadowStudyPresetModel
                    {
                        Name = "STA Smoke Preset",
                        Dates = new System.Collections.Generic.List<Synthetic.Features.ShadowStudy.Models.ShadowStudyPresetDateSpec>
                        {
                            new Synthetic.Features.ShadowStudy.Models.ShadowStudyPresetDateSpec
                            {
                                AstronomicalEvent = Synthetic.Features.ShadowStudy.Models.AstronomicalEventKind.SummerSolstice
                            }
                        }
                    }
                }
            };

            var vm = new Synthetic.Features.ShadowStudy.ViewModels.ShadowStudySettingsViewModel(
                () => stored,
                s => stored = s.DeepClone(),
                () => { },
                () => true);

            Synthetic.Features.ShadowStudy.Views.ShadowStudySettingsView? view = null;
            Assert.DoesNotThrow(() =>
            {
                view = new Synthetic.Features.ShadowStudy.Views.ShadowStudySettingsView { DataContext = vm };
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
            var adapter = new SyntheticTests.Fixtures.FakeShadowStudyAdapter
            {
                ActiveView = null
            };
            adapter.EligibleViews.Add(new Synthetic.Features.ShadowStudy.Models.ShadowStudyViewItem
            {
                Id = "v1",
                Name = "Level 1",
                ViewType = "FloorPlan",
                IsCropped = true
            });

            var folderDialog = new SyntheticTests.Fixtures.FakeFolderDialogService();
            var vm = new Synthetic.Features.ShadowStudy.ViewModels.ShadowStudyViewModel(adapter, folderDialog);
            vm.OutputFolder = @"C:\Temp\ShadowOut";
            vm.FilteredViews[0].IsSelected = true;

            var window = new Synthetic.Features.ShadowStudy.Views.ShadowStudyWindow(IntPtr.Zero) { DataContext = vm };
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

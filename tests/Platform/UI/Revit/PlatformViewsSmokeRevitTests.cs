using System;
using System.Diagnostics;
using System.Threading;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Platform.Host;
using Synthetic.Platform.UI;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;

namespace SyntheticTests.Platform.UI
{
    /// <summary>
    /// Live Revit STA smoke tests verifying that all shared <c>Platform/UI</c> views
    /// resolve <see cref="SyntheticThemeDictionary"/> BAML resources inside <c>Revit.exe</c>
    /// without <see cref="System.IO.IOException"/> resource failures or WPF
    /// <c>BindingExpression</c> errors.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class PlatformViewsSmokeRevitTests
    {
        private UIApplication? _uiapp;
        private int _initialDocCount;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            HostSettingsRegistrar.EnsureRegistered();
            TestWorkerJournalMarker.EnsureWritten(uiapp);
            _initialDocCount = uiapp.Application.Documents.Size;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_uiapp?.Application != null)
            {
                Assert.AreEqual(
                    _initialDocCount,
                    _uiapp.Application.Documents.Size,
                    "PlatformViewsSmokeRevitTests leaked open Revit document(s).");
            }
        }

        [Test]
        public void SharedPlatformViews_ConstructShowAndBind_WithoutThemeIOExceptionOrBindingErrors()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            IntPtr ownerHandle = _uiapp!.MainWindowHandle;

            using (var harness = new WpfBindingSmokeHarness())
            {
                // 1. DropdownSelectionView
                var dropdownVm = new DropdownSelectionViewModel
                {
                    Title = "Smoke Dropdown",
                    Instruction = "Select an option:",
                    ItemLabel = "Option",
                    Items = new[] { "Option A", "Option B" }
                };
                WpfBindingSmokeHarness.ExerciseWindow(new DropdownSelectionView(ownerHandle) { DataContext = dropdownVm });

                // 2. ExportCompletionWindow
                var exportVm = ExportCompletionViewModel.ForBatch(
                    outputFolder: @"C:\Temp\SyntheticSmoke",
                    generatedCount: 2,
                    primaryFilePath: @"C:\Temp\SyntheticSmoke\Output.png",
                    warningNotices: new[] { "Sample smoke notice." });
                WpfBindingSmokeHarness.ExerciseWindow(new ExportCompletionWindow(exportVm, ownerHandle));

                // 3. GuardrailPromptWindow
                WpfBindingSmokeHarness.ExerciseWindow(new GuardrailPromptWindow(@"C:\Temp\SyntheticSmoke\Master.json", ownerHandle));

                // 4. ListByCheckboxView
                var listVm = new ListByCheckboxViewModel
                {
                    Title = "Smoke Checklist",
                    Instruction = "Check items:"
                };
                listVm.SetItems(new[] { "First", "Second" }, checkAll: true);
                WpfBindingSmokeHarness.ExerciseWindow(new ListByCheckboxView(ownerHandle) { DataContext = listVm });

                // 5. SelectSearchPathsView
                var searchPathsVm = new SelectSearchPathsViewModel
                {
                    Title = "Smoke Search Paths"
                };
                searchPathsVm.UserPaths.Add(new CheckableItem(@"C:\Temp\UserPath", true));
                searchPathsVm.DefaultPaths.Add(new CheckableItem(@"C:\Temp\DefaultPath", false));
                WpfBindingSmokeHarness.ExerciseWindow(new SelectSearchPathsView(ownerHandle) { DataContext = searchPathsVm });

                // 6. SharedProgressWindow
                using (var cts = new CancellationTokenSource())
                {
                    var progressVm = new SharedProgressViewModel(cts)
                    {
                        WindowTitle = "Smoke Progress",
                        MainTaskDescription = "Verifying theme & bindings...",
                        MaximumValue = 10,
                        CurrentValue = 5,
                        CurrentItemName = "Item 5"
                    };
                    var progressWindow = new SharedProgressWindow(ownerHandle, progressVm)
                    {
                        AllowProgrammaticClose = true
                    };
                    WpfBindingSmokeHarness.ExerciseWindow(progressWindow);
                }

                // 7. SingleItemSelectionWindow
                var singleVm = new SingleItemSelectionViewModel<string>(
                    new[] { "Alpha", "Beta" },
                    "Pick one item:",
                    s => s)
                {
                    Title = "Smoke Single Selection"
                };
                WpfBindingSmokeHarness.ExerciseWindow(new SingleItemSelectionWindow(ownerHandle) { DataContext = singleVm });

                // 8. TextInputPromptWindow
                WpfBindingSmokeHarness.ExerciseWindow(new TextInputPromptWindow(
                    title: "Smoke Input",
                    promptMessage: "Enter value:",
                    initialValue: "Initial",
                    parentHandle: ownerHandle));

                harness.AssertNoBindingErrors("shared Platform/UI views");
            }
        }

        [Test]
        public void RevitWindowHelper_MainWindowHandle_FallsBackToCurrentProcessAndSupportsExplicitSeed()
        {
            IntPtr original = RevitWindowHelper.MainWindowHandle;
            try
            {
                RevitWindowHelper.MainWindowHandle = IntPtr.Zero;
                Assert.AreEqual(
                    Process.GetCurrentProcess().MainWindowHandle,
                    RevitWindowHelper.MainWindowHandle,
                    "RevitWindowHelper.MainWindowHandle must fall back to the current process handle when unset.");

                Assert.IsNotNull(_uiapp);
                RevitWindowHelper.MainWindowHandle = _uiapp!.MainWindowHandle;
                Assert.AreEqual(_uiapp.MainWindowHandle, RevitWindowHelper.MainWindowHandle);
            }
            finally
            {
                RevitWindowHelper.MainWindowHandle = original;
            }
        }
    }
}

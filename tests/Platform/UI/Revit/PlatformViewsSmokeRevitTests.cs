using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Platform.UI;
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

        private sealed class CapturingTraceListener : TraceListener
        {
            public List<string> Messages { get; } = new List<string>();

            public override void Write(string? message)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    Messages.Add(message!);
                }
            }

            public override void WriteLine(string? message)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    Messages.Add(message!);
                }
            }
        }

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            TestWorkerJournalMarker.EnsureWritten(uiapp);
        }

        [Test]
        public void SharedPlatformViews_ConstructShowAndBind_WithoutThemeIOExceptionOrBindingErrors()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            IntPtr ownerHandle = _uiapp!.MainWindowHandle;

            var listener = new CapturingTraceListener();
            TraceSource dataBindingSource = PresentationTraceSources.DataBindingSource;
            SourceLevels previousLevel = dataBindingSource.Switch.Level;
            dataBindingSource.Switch.Level = SourceLevels.Error;
            dataBindingSource.Listeners.Add(listener);

            try
            {
                PresentationTraceSources.Refresh();

                // 1. DropdownSelectionView
                var dropdownVm = new DropdownSelectionViewModel
                {
                    Title = "Smoke Dropdown",
                    Instruction = "Select an option:",
                    ItemLabel = "Option",
                    Items = new[] { "Option A", "Option B" }
                };
                ExerciseWindow(new DropdownSelectionView(ownerHandle) { DataContext = dropdownVm });

                // 2. ExportCompletionWindow
                var exportVm = ExportCompletionViewModel.ForBatch(
                    outputFolder: @"C:\Temp\SyntheticSmoke",
                    generatedCount: 2,
                    primaryFilePath: @"C:\Temp\SyntheticSmoke\Output.png",
                    warningNotices: new[] { "Sample smoke notice." });
                ExerciseWindow(new ExportCompletionWindow(exportVm, ownerHandle));

                // 3. GuardrailPromptWindow
                ExerciseWindow(new GuardrailPromptWindow(@"C:\Temp\SyntheticSmoke\Master.json", ownerHandle));

                // 4. ListByCheckboxView
                var listVm = new ListByCheckboxViewModel
                {
                    Title = "Smoke Checklist",
                    Instruction = "Check items:"
                };
                listVm.SetItems(new[] { "First", "Second" }, checkAll: true);
                ExerciseWindow(new ListByCheckboxView(ownerHandle) { DataContext = listVm });

                // 5. SelectSearchPathsView
                var searchPathsVm = new SelectSearchPathsViewModel
                {
                    Title = "Smoke Search Paths"
                };
                searchPathsVm.UserPaths.Add(new CheckableItem(@"C:\Temp\UserPath", true));
                searchPathsVm.DefaultPaths.Add(new CheckableItem(@"C:\Temp\DefaultPath", false));
                ExerciseWindow(new SelectSearchPathsView(ownerHandle) { DataContext = searchPathsVm });

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
                    ExerciseWindow(progressWindow);
                }

                // 7. SingleItemSelectionWindow
                var singleVm = new SingleItemSelectionViewModel<string>(
                    new[] { "Alpha", "Beta" },
                    "Pick one item:",
                    s => s)
                {
                    Title = "Smoke Single Selection"
                };
                ExerciseWindow(new SingleItemSelectionWindow(ownerHandle) { DataContext = singleVm });

                // 8. TextInputPromptWindow
                ExerciseWindow(new TextInputPromptWindow(
                    title: "Smoke Input",
                    promptMessage: "Enter value:",
                    initialValue: "Initial",
                    parentHandle: ownerHandle));

                Assert.IsEmpty(
                    listener.Messages,
                    "Expected zero WPF DataBinding errors across shared Platform/UI views:" + Environment.NewLine
                    + string.Join(Environment.NewLine, listener.Messages));
            }
            finally
            {
                dataBindingSource.Listeners.Remove(listener);
                dataBindingSource.Switch.Level = previousLevel;
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

        private static void ExerciseWindow(Window window)
        {
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.IsTrue(window.IsVisible, $"{window.GetType().Name} should show cleanly without resource exceptions.");
            }
            finally
            {
                window.Close();
            }
        }
    }
}

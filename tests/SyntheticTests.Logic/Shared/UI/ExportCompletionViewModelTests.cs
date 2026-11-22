using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using NUnit.Framework;
using Synthetic.Shared.UI;

namespace SyntheticTests.Shared.UI
{
    [TestFixture]
    public class ExportCompletionViewModelTests
    {
        [Test]
        public void ForSingleFile_SetsExpectedPresentationStateAndCommands()
        {
            const string filePath = @"C:\Exports\Dot Sheet Index.xlsx";

            var vm = ExportCompletionViewModel.ForSingleFile(
                filePath,
                windowTitle: "Export Complete",
                headerTitle: "Sheet Index Export Complete");

            Assert.AreEqual("Export Complete", vm.WindowTitle);
            Assert.AreEqual("Sheet Index Export Complete", vm.HeaderTitle);
            Assert.AreEqual("File saved successfully to:", vm.SummaryText);
            Assert.AreEqual(filePath, vm.DestinationPath);
            Assert.AreEqual("Open File", vm.PrimaryActionLabel);
            Assert.AreEqual("Open Folder", vm.OpenFolderLabel);
            Assert.IsTrue(vm.CanOpenPrimaryFile);
            Assert.IsTrue(vm.CanOpenFolder);
            Assert.IsTrue(vm.HasGeneratedOutput);
            Assert.IsFalse(vm.HasWarnings);
            Assert.AreEqual(0, vm.WarningNotices.Count);
            Assert.IsTrue(vm.OpenPrimaryFileCommand.CanExecute(null));
            Assert.IsTrue(vm.OpenFolderCommand.CanExecute(null));
            Assert.IsTrue(vm.CloseCommand.CanExecute(null));
        }

        [Test]
        public void ForSingleFile_WhenFilePathIsBlank_DisablesOpenActions()
        {
            var vm = ExportCompletionViewModel.ForSingleFile("   ");

            Assert.IsFalse(vm.CanOpenPrimaryFile);
            Assert.IsFalse(vm.CanOpenFolder);
            Assert.IsFalse(vm.OpenPrimaryFileCommand.CanExecute(null));
            Assert.IsFalse(vm.OpenFolderCommand.CanExecute(null));
        }

        [Test]
        public void BuildExplorerSelectArgument_FormatsSelectFlagWithQuotedPath()
        {
            const string filePath = @"C:\Exports\My Folder\Dot Sheet Index.xlsx";

            string argument = ExportCompletionViewModel.BuildExplorerSelectArgument(filePath);

            Assert.AreEqual("/select,\"C:\\Exports\\My Folder\\Dot Sheet Index.xlsx\"", argument);
        }

        [Test]
        public void ForBatch_MultipleOutputs_SetsCountsFolderAndCompositeLabel()
        {
            const string outputFolder = @"C:\Exports\ShadowStudy";
            const string primaryFile = @"C:\Exports\ShadowStudy\Level 1 - 260621 - Composite.png";

            var vm = ExportCompletionViewModel.ForBatch(
                outputFolder,
                generatedCount: 4,
                primaryFilePath: primaryFile,
                outputNounPlural: "composites",
                primaryActionLabel: "Open Composite");

            Assert.AreEqual("Export Complete", vm.WindowTitle);
            Assert.AreEqual("Export Complete", vm.HeaderTitle);
            Assert.AreEqual("Generated 4 composites in:", vm.SummaryText);
            Assert.AreEqual(outputFolder, vm.DestinationPath);
            Assert.AreEqual("Open Composite", vm.PrimaryActionLabel);
            Assert.IsTrue(vm.CanOpenPrimaryFile);
            Assert.IsTrue(vm.CanOpenFolder);
            Assert.IsTrue(vm.HasGeneratedOutput);
            Assert.IsFalse(vm.HasWarnings);
            Assert.IsTrue(vm.OpenPrimaryFileCommand.CanExecute(null));
            Assert.IsTrue(vm.OpenFolderCommand.CanExecute(null));
        }

        [Test]
        public void ForBatch_SingleOutput_UsesSingularNounInSummary()
        {
            var vm = ExportCompletionViewModel.ForBatch(
                outputFolder: @"C:\Exports\ShadowStudy",
                generatedCount: 1,
                primaryFilePath: @"C:\Exports\ShadowStudy\Level 1 - 260621 - Composite.png",
                outputNounPlural: "composites");

            Assert.AreEqual("Generated 1 composite in:", vm.SummaryText);
        }

        [Test]
        public void ForBatch_ZeroOutputsWithWarnings_DisablesPrimaryOpenAndExposesWarnings()
        {
            const string outputFolder = @"C:\Exports\ShadowStudy";
            var warnings = new List<string>
            {
                "261221 - Winter Solstice: 0 frames met minimum 5.0° solar altitude.",
                "  ",
                ""
            };

            var vm = ExportCompletionViewModel.ForBatch(
                outputFolder,
                generatedCount: 0,
                primaryFilePath: null,
                outputNounPlural: "composites",
                primaryActionLabel: "Open Composite",
                warningNotices: warnings);

            Assert.IsFalse(vm.CanOpenPrimaryFile);
            Assert.IsFalse(vm.OpenPrimaryFileCommand.CanExecute(null));
            Assert.IsTrue(vm.CanOpenFolder);
            Assert.IsTrue(vm.OpenFolderCommand.CanExecute(null));
            Assert.IsFalse(vm.HasGeneratedOutput);
            Assert.IsTrue(vm.HasWarnings);
            Assert.AreEqual(1, vm.WarningNotices.Count);
            Assert.AreEqual("261221 - Winter Solstice: 0 frames met minimum 5.0° solar altitude.", vm.WarningNotices[0]);
            Assert.AreEqual("No composites were generated in:", vm.SummaryText);
        }

        [Test]
        public void ForBatch_PartialOutputsWithWarnings_KeepsPrimaryOpenEnabledAndShowsWarnings()
        {
            var vm = ExportCompletionViewModel.ForBatch(
                outputFolder: @"C:\Exports\ShadowStudy",
                generatedCount: 2,
                primaryFilePath: @"C:\Exports\ShadowStudy\Level 1 - 260621 - Composite.png",
                warningNotices: new[] { "Skipped View 2 (0 daylight frames)." });

            Assert.IsTrue(vm.CanOpenPrimaryFile);
            Assert.IsTrue(vm.HasGeneratedOutput);
            Assert.IsTrue(vm.HasWarnings);
            Assert.AreEqual(1, vm.WarningNotices.Count);
        }

        [Test]
        public void Commands_InvokeInjectedCallbacksAndRequestClose()
        {
            string? openedFile = null;
            string? openedFolder = null;
            bool closeRequested = false;

            const string singleFile = @"C:\Exports\Dot Sheet Index.xlsx";
            var singleVm = ExportCompletionViewModel.ForSingleFile(
                singleFile,
                openFileAction: path => openedFile = path,
                openFolderAction: path => openedFolder = path);
            singleVm.RequestClose += () => closeRequested = true;

            singleVm.OpenPrimaryFileCommand.Execute(null);
            singleVm.OpenFolderCommand.Execute(null);
            singleVm.CloseCommand.Execute(null);

            Assert.AreEqual(singleFile, openedFile);
            Assert.AreEqual(singleFile, openedFolder);
            Assert.IsTrue(closeRequested);

            // Batch mode should pass outputFolder to openFolderAction and guard disabled primary open
            string? batchOpenedFile = null;
            string? batchOpenedFolder = null;
            var zeroBatchVm = ExportCompletionViewModel.ForBatch(
                outputFolder: @"C:\Exports\Batch",
                generatedCount: 0,
                primaryFilePath: @"C:\Exports\Batch\Ignored.png",
                openFileAction: path => batchOpenedFile = path,
                openFolderAction: path => batchOpenedFolder = path);

            zeroBatchVm.OpenPrimaryFileCommand.Execute(null);
            zeroBatchVm.OpenFolderCommand.Execute(null);

            Assert.IsNull(batchOpenedFile);
            Assert.AreEqual(@"C:\Exports\Batch", batchOpenedFolder);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ExportCompletionWindow_ConstructsWithThemeAndBindsViewModel()
        {
            if (Application.Current == null)
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }

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
    }
}

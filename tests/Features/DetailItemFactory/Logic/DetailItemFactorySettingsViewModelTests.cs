using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using NUnit.Framework;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Features.DetailItemFactory.ViewModels;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.DetailItemFactory
{
    [TestFixture]
    [UnitTest]
    public class DetailItemFactorySettingsViewModelTests
    {
        [Test]
        public void Constructor_HydratesSubcategoriesWithDeduplicationAndFallback_WithoutMarkingDirty()
        {
            var stored = new DetailItemFactorySettings
            {
                OutputPath = @"C:\Studio\DetailItems",
                TargetSubcategory = "heavy line"
            };

            var vm = new DetailItemFactorySettingsViewModel(
                () => stored,
                s => stored = s.DeepClone(),
                () => { },
                () => true,
                () => new[] { "Thin Line", "Heavy Line", "heavy line", "", "   " });

            Assert.AreEqual("Detail Item Factory", vm.ModuleName);
            Assert.AreEqual(DetailItemFactorySettings.Name, vm.ModuleKey);
            Assert.IsNull(vm.ConfigureCommand);
            Assert.IsTrue(vm.IsOverridden);
            Assert.IsFalse(vm.IsDirty);
            Assert.IsTrue(vm.IsValid);
            Assert.AreEqual(@"C:\Studio\DetailItems", vm.OutputPath);
            Assert.AreEqual("Heavy Line", vm.SelectedSubcategoryName);
            CollectionAssert.AreEqual(
                new[] { "Thin Line", "Heavy Line", "Detail Items" },
                vm.AvailableSubcategories);
        }

        [Test]
        public void Constructor_WhenTargetSubcategoryMissingFromDocument_FallsBackToDetailItems()
        {
            var stored = new DetailItemFactorySettings
            {
                OutputPath = @"C:\Studio\DetailItems",
                TargetSubcategory = "NonExistentSubcategory"
            };

            var vm = new DetailItemFactorySettingsViewModel(
                () => stored,
                s => stored = s.DeepClone(),
                () => { },
                () => true,
                () => new[] { "Thin Line" });

            Assert.AreEqual("Detail Items", vm.SelectedSubcategoryName);
            CollectionAssert.AreEqual(new[] { "Thin Line", "Detail Items" }, vm.AvailableSubcategories);
        }

        [Test]
        public void CancelAndSave_DeepCloneIsolation_NeverMutatesCachedSourceOnCancel_AndIsolatesSavedPayload()
        {
            var cachedSource = new DetailItemFactorySettings
            {
                OutputPath = @"C:\Cached\Initial",
                TargetSubcategory = "Thin Line"
            };
            bool isOverridden = true;
            int deleteCalls = 0;

            var vm = new DetailItemFactorySettingsViewModel(
                () => cachedSource,
                s =>
                {
                    cachedSource = s;
                    isOverridden = true;
                },
                () =>
                {
                    deleteCalls++;
                    isOverridden = false;
                },
                () => isOverridden,
                () => new[] { "Detail Items", "Thin Line", "Heavy Line" });

            // Edit working copy without saving (simulating Settings Dashboard Cancel)
            vm.OutputPath = @"C:\Cancelled\Edit";
            vm.SelectedSubcategoryName = "Heavy Line";
            Assert.IsTrue(vm.IsDirty);

            // Cached source must remain untouched
            Assert.AreEqual(@"C:\Cached\Initial", cachedSource.OutputPath);
            Assert.AreEqual("Thin Line", cachedSource.TargetSubcategory);

            // Reload discards working edits
            vm.Reload();
            Assert.IsFalse(vm.IsDirty);
            Assert.AreEqual(@"C:\Cached\Initial", vm.OutputPath);
            Assert.AreEqual("Thin Line", vm.SelectedSubcategoryName);

            // Edit to offline UNC path and Save -> saved instance is isolated from subsequent VM edits
            vm.OutputPath = @"\\offline-server\bim\DetailItems";
            vm.SelectedSubcategoryName = "Heavy Line";
            Assert.IsTrue(vm.IsValid);
            vm.Save();

            DetailItemFactorySettings savedSnapshot = cachedSource;
            Assert.AreEqual(@"\\offline-server\bim\DetailItems", savedSnapshot.OutputPath);
            Assert.AreEqual("Heavy Line", savedSnapshot.TargetSubcategory);

            vm.OutputPath = @"C:\Subsequent\UnsavedEdit";
            Assert.AreEqual(@"\\offline-server\bim\DetailItems", savedSnapshot.OutputPath);

            // Disabling override invokes delete on Save even if working OutputPath had invalid syntax
            vm.OutputPath = @"relative\invalid";
            vm.IsOverridden = false;
            Assert.IsTrue(vm.IsValid);
            vm.Save();
            Assert.AreEqual(1, deleteCalls);
        }

        [Test]
        public void OutputPathValidation_TracksIsValidAndRaisesChangedNotifications()
        {
            var stored = new DetailItemFactorySettings();
            var changedProperties = new List<string>();

            var vm = new DetailItemFactorySettingsViewModel(
                () => stored,
                s => stored = s.DeepClone(),
                () => { },
                () => true,
                () => new[] { "Detail Items" });

            vm.PropertyChanged += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.PropertyName))
                {
                    changedProperties.Add(e.PropertyName!);
                }
            };

            vm.OutputPath = @"relative\invalid";
            Assert.IsTrue(vm.IsDirty);
            Assert.IsFalse(vm.IsValid);
            CollectionAssert.Contains(changedProperties, nameof(DetailItemFactorySettingsViewModel.IsValid));
            CollectionAssert.Contains(changedProperties, nameof(DetailItemFactorySettingsViewModel.SummaryText));

            vm.OutputPath = @"\\offline-studio\share\DetailItems";
            Assert.IsTrue(vm.IsValid);

            vm.OutputPath = string.Empty;
            Assert.IsTrue(vm.IsValid);
        }

        [Test]
        public void BrowseCommand_RespectsIsOverridden_AndAppliesSelectedFolderFromDialogService()
        {
            var stored = new DetailItemFactorySettings { OutputPath = @"C:\Initial\Path" };
            bool isOverridden = false;
            var folderDialog = new FakeFolderDialogService { NextResult = @"C:\Picked\Folder" };

            var vm = new DetailItemFactorySettingsViewModel(
                () => stored,
                s => stored = s.DeepClone(),
                () => { },
                () => isOverridden,
                () => new[] { "Detail Items" },
                folderDialog);

            Assert.IsFalse(vm.BrowseCommand.CanExecute(null));
            vm.BrowseCommand.Execute(null);
            Assert.AreEqual(0, folderDialog.Calls.Count);

            vm.IsOverridden = true;
            Assert.IsTrue(vm.BrowseCommand.CanExecute(null));

            vm.BrowseCommand.Execute(null);
            Assert.AreEqual(1, folderDialog.Calls.Count);
            Assert.AreEqual(@"C:\Initial\Path", folderDialog.Calls[0].InitialPath);
            Assert.AreEqual(@"C:\Picked\Folder", vm.OutputPath);

            // Cancelling folder dialog preserves OutputPath
            folderDialog.NextResult = null;
            vm.BrowseCommand.Execute(null);
            Assert.AreEqual(@"C:\Picked\Folder", vm.OutputPath);

            // When OutputPath is empty, MyDocuments is used as initial path
            vm.OutputPath = string.Empty;
            vm.BrowseCommand.Execute(null);
            Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), folderDialog.Calls[2].InitialPath);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Reload_RefreshesCachedFirmwideDefaults_Subcategories_AndRaisesIsDirtyAndRequerySuggested()
        {
            var stored = new DetailItemFactorySettings();
            var firmwideDefaults = new DetailItemFactorySettings
            {
                OutputPath = @"\\firmwide\v1\DetailItems",
                TargetSubcategory = "Thin Line"
            };
            bool isOverridden = false;
            IReadOnlyList<string> subcategories = new[] { "Detail Items", "Thin Line" };
            int firmwideLoadCount = 0;

            var vm = new DetailItemFactorySettingsViewModel(
                () => stored,
                s => stored = s.DeepClone(),
                () => { },
                () => isOverridden,
                () => subcategories,
                loadFirmwideDefaults: () =>
                {
                    firmwideLoadCount++;
                    return firmwideDefaults;
                });

            Assert.AreEqual(1, firmwideLoadCount);
            StringAssert.Contains(@"\\firmwide\v1\DetailItems", vm.SummaryText);
            StringAssert.Contains("Thin Line", vm.SummaryText);
            // Reading SummaryText repeatedly uses the cached firmwide defaults
            _ = vm.SummaryText;
            _ = vm.SummaryText;
            Assert.AreEqual(1, firmwideLoadCount);

            // Mutate underlying firmwide defaults + document override without Reload() -> SummaryText remains cached
            firmwideDefaults = new DetailItemFactorySettings
            {
                OutputPath = string.Empty,
                TargetSubcategory = "Heavy Line"
            };
            StringAssert.Contains(@"\\firmwide\v1\DetailItems", vm.SummaryText);

            // Track PropertyChanged(IsDirty) and CommandManager.RequerySuggested during Reload()
            var raisedProperties = new List<string>();
            bool requerySuggestedRaised = false;
            EventHandler requeryHandler = (_, _) => requerySuggestedRaised = true;

            vm.PropertyChanged += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.PropertyName))
                {
                    raisedProperties.Add(e.PropertyName!);
                }
            };
            CommandManager.RequerySuggested += requeryHandler;

            try
            {
                subcategories = new[] { "Detail Items", "Heavy Line" };
                vm.IsOverridden = true;
                Assert.IsTrue(vm.IsDirty);
                raisedProperties.Clear();
                requerySuggestedRaised = false;

                vm.Reload();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

                Assert.AreEqual(2, firmwideLoadCount);
                Assert.IsFalse(vm.IsOverridden);
                Assert.IsFalse(vm.IsDirty);
                CollectionAssert.Contains(raisedProperties, nameof(DetailItemFactorySettingsViewModel.IsDirty));
                Assert.IsTrue(requerySuggestedRaised, "Reload() must invoke CommandManager.InvalidateRequerySuggested().");
                StringAssert.Contains("(Default Dynamic Document Path)", vm.SummaryText);
                StringAssert.Contains("Heavy Line", vm.SummaryText);
                CollectionAssert.AreEqual(new[] { "Detail Items", "Heavy Line" }, vm.AvailableSubcategories);
            }
            finally
            {
                CommandManager.RequerySuggested -= requeryHandler;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.MergeDuplicates.Models;
using Synthetic.Settings;
using Synthetic.Shared.UI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.Utilities;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class DashboardTabAndFamilyFilterTests
    {
        private Document _doc = null!;
        private FakeFileDialogService _fakeDialogService = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeDialogService = new FakeFileDialogService();
            ProgressCoordinator.SuppressUI = true;
        }

        private T CreateMockElement<T>(Document doc, string name, long idVal) where T : Element
        {
            var elem = (T)Activator.CreateInstance(typeof(T), true)!;
            
            // Set Name
            var nameProp = typeof(T).GetProperty("Name");
            nameProp?.SetValue(elem, name);

            // Set Id
            var idProp = typeof(T).GetProperty("Id");
            if (idProp != null && idProp.CanWrite)
            {
                idProp.SetValue(elem, new ElementId(idVal));
            }

            // Add to doc
            var addElementMethod = doc.GetType().GetMethod("AddElement");
            if (addElementMethod != null)
            {
                addElementMethod.Invoke(doc, new object[] { elem, elem.Id });
            }

            return elem;
        }

        private void SetDocumentStringProperty(Document doc, string propertyName, string value)
        {
            var prop = doc.GetType().GetProperty(propertyName);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(doc, value);
            }
        }

        [Test]
        public void VerifyTabCreationAndLifecycle_AddsTabsForSelectedDocuments()
        {
            // Arrange
            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService);
            var mockDoc1 = (Document)Activator.CreateInstance(typeof(Document), true)!;
            SetDocumentStringProperty(mockDoc1, "Title", "Model A");
            SetDocumentStringProperty(mockDoc1, "PathName", "C:\\Projects\\ModelA.rvt");

            var mockDoc2 = (Document)Activator.CreateInstance(typeof(Document), true)!;
            SetDocumentStringProperty(mockDoc2, "Title", "Model B");
            SetDocumentStringProperty(mockDoc2, "PathName", "C:\\Projects\\ModelB.rvt");

            vm.MockOpenDocuments = new List<Document> { mockDoc1, mockDoc2 };

            // Act
            vm.AddRevitModelCommand.Execute(null);

            // Assert
            Assert.AreEqual(2, vm.AvailableSources.Count, "Should have 2 sources loaded.");
            Assert.IsTrue(vm.AvailableSources.Any(s => s.DisplayName == "Model A"));
            Assert.IsTrue(vm.AvailableSources.Any(s => s.DisplayName == "Model B"));
            Assert.IsTrue(vm.AvailableSources.All(s => s.IsRevitSource));
        }

        [Test]
        public void VerifyResourceCleanupOnClose_RemovesTabAndPurgesHierarchy()
        {
            // Arrange
            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService);
            var mockDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            SetDocumentStringProperty(mockDoc, "Title", "Model A");
            SetDocumentStringProperty(mockDoc, "PathName", "C:\\Projects\\ModelA.rvt");
            
            // Add a mock element so the tab has hierarchical data
            CreateMockElement<Material>(mockDoc, "Steel", 501);

            vm.MockOpenDocuments = new List<Document> { mockDoc };
            vm.AddRevitModelCommand.Execute(null);

            var addedSource = vm.AvailableSources.FirstOrDefault(s => s.DisplayName == "Model A");
            Assert.IsNotNull(addedSource, "Tab should be added.");
            Assert.IsTrue(addedSource.SourceHierarchy.Count > 0, "Hierarchy should not be empty.");

            // Act
            vm.CloseSourceCommand.Execute(addedSource);

            // Assert
            Assert.IsFalse(vm.AvailableSources.Contains(addedSource), "Tab should be removed from AvailableSources.");
            Assert.AreEqual(0, addedSource.SourceHierarchy.Count, "Associated hierarchy collections should be cleared to disperse memory.");
        }

        [Test]
        public void VerifyInitialStateConstraints_PrechecksDefaultStandardAndLeavesQueueEmpty()
        {
            // Arrange
            string tempJsonFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            // Write a simple valid standards JSON
            File.WriteAllText(tempJsonFile, @"{
                ""Materials"": {
                    ""Concrete"": {
                        ""$type"": ""Synthetic.Modules.StandardsManagement.Models.MaterialModel, SyntheticShared"",
                        ""Name"": ""Concrete"",
                        ""Class"": ""Autodesk.Revit.DB.Material"",
                        ""UniqueId"": ""abc-123""
                    }
                }
            }");

            var settings = new StandardsSettings { StandardsFilePath = tempJsonFile };
            
            // Act
            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService, (IStandardsExportService?)null, settings);

            try
            {
                // Assert
                Assert.AreEqual(1, vm.AvailableSources.Count, "Should load default firm standard.");
                var defaultSource = vm.AvailableSources[0];
                Assert.AreEqual("Default Firm Standard", defaultSource.DisplayName);
                
                // Assert that checkboxes are checked
                Assert.IsTrue(defaultSource.SourceHierarchy.Count > 0);
                foreach (var group in defaultSource.SourceHierarchy)
                {
                    Assert.IsTrue(group.IsChecked == true, "Hierarchical tree nodes should be checked on launch.");
                }

                // Assert that action queue remains empty
                Assert.AreEqual(0, vm.ActionQueue.Count, "Action queue must remain empty on launch.");
            }
            finally
            {
                if (File.Exists(tempJsonFile)) File.Delete(tempJsonFile);
            }
        }

        [Test]
        public void VerifyMultiModelExtractionIsolation_ExtractsDistinctNonIntersectingHierarchies()
        {
            // Arrange
            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService);

            var doc1 = (Document)Activator.CreateInstance(typeof(Document), true)!;
            SetDocumentStringProperty(doc1, "Title", "Doc 1");
            CreateMockElement<Material>(doc1, "Aluminum", 601);

            var doc2 = (Document)Activator.CreateInstance(typeof(Document), true)!;
            SetDocumentStringProperty(doc2, "Title", "Doc 2");
            CreateMockElement<Material>(doc2, "Copper", 602);

            vm.MockOpenDocuments = new List<Document> { doc1, doc2 };

            // Act
            vm.AddRevitModelCommand.Execute(null);

            // Assert
            var source1 = vm.AvailableSources.FirstOrDefault(s => s.DisplayName == "Doc 1");
            var source2 = vm.AvailableSources.FirstOrDefault(s => s.DisplayName == "Doc 2");

            Assert.IsNotNull(source1);
            Assert.IsNotNull(source2);

            // Verify elements inside hierarchies are isolated
            var elements1 = source1.SourceHierarchy
                .SelectMany(g => g.Children)
                .SelectMany(c => c.Children)
                .Select(e => e.Name)
                .ToList();

            var elements2 = source2.SourceHierarchy
                .SelectMany(g => g.Children)
                .SelectMany(c => c.Children)
                .Select(e => e.Name)
                .ToList();

            Assert.Contains("Aluminum", elements1);
            Assert.IsFalse(elements1.Contains("Copper"));

            Assert.Contains("Copper", elements2);
            Assert.IsFalse(elements2.Contains("Aluminum"));
        }

        [Test]
        public void VerifyStaticFilteringApplication_RestrictsExtractedClassesBasedOnSelection()
        {
            // Arrange
            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService);

            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            SetDocumentStringProperty(doc, "Title", "Filter Model");
            
            CreateMockElement<TextNoteType>(doc, "Arial 3/32", 701);
            CreateMockElement<Material>(doc, "Brass", 702);

            vm.MockOpenDocuments = new List<Document> { doc };

            // Set up document selection dialog mock to NOT select Annotations
            vm.ShowDocumentSelectionDialog = dialogVM =>
            {
                dialogVM.OpenDocuments[0].IsSelected = true;
                var annotationsGroup = dialogVM.FilterHierarchy.FirstOrDefault(g => g.Name == "Annotations");
                if (annotationsGroup != null)
                {
                    annotationsGroup.IsChecked = false;
                }
                return true;
            };

            // Act
            vm.AddRevitModelCommand.Execute(null);

            // Assert
            var source = vm.AvailableSources.FirstOrDefault(s => s.DisplayName == "Filter Model");
            Assert.IsNotNull(source);

            var extractedNames = source.SourceHierarchy
                .SelectMany(g => g.Children)
                .SelectMany(c => c.Children)
                .Select(e => e.Name)
                .ToList();

            Assert.Contains("Brass", extractedNames);
            Assert.IsFalse(extractedNames.Contains("Arial 3/32"), "Annotations should be excluded based on filtering.");
        }

        [Test]
        public void SelectAllCommand_ShouldCheckAllNodes()
        {
            // Arrange
            var source = new ProjectStandardsSourceViewModel
            {
                DisplayName = "Test Source"
            };

            var group = new StandardGroupModel { Name = "Group 1" };
            var childClass = new StandardClassModel { Name = "Class 1", Parent = group };
            group.Children.Add(childClass);
            source.SourceHierarchy.Add(group);

            // Act
            source.SelectAllCommand.Execute(null);

            // Assert
            Assert.IsTrue(group.IsChecked);
            Assert.IsTrue(childClass.IsChecked);
        }

        [Test]
        public void SelectNoneCommand_ShouldUncheckAllNodes()
        {
            // Arrange
            var source = new ProjectStandardsSourceViewModel
            {
                DisplayName = "Test Source"
            };

            var group = new StandardGroupModel { Name = "Group 1" };
            var childClass = new StandardClassModel { Name = "Class 1", Parent = group };
            group.Children.Add(childClass);
            source.SourceHierarchy.Add(group);

            group.IsChecked = true;

            // Act
            source.SelectNoneCommand.Execute(null);

            // Assert
            Assert.IsFalse(group.IsChecked);
            Assert.IsFalse(childClass.IsChecked);
        }

        [Test]
        public void SearchText_ShouldFilterNodesAndSetVisibilityAndExpansion()
        {
            // Arrange
            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService);
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };

            var group = new StandardGroupModel { Name = "Materials" };
            var childClass = new StandardClassModel { Name = "Metal", Parent = group };
            var element1 = new StandardElementModel(new MaterialModel { Name = "Steel" }) { Parent = childClass };
            var element2 = new StandardElementModel(new MaterialModel { Name = "Concrete" }) { Parent = childClass };

            childClass.Children.Add(element1);
            childClass.Children.Add(element2);
            group.Children.Add(childClass);
            source.SourceHierarchy.Add(group);

            vm.AvailableSources.Add(source);

            // Act: Filter by "Steel"
            vm.SearchText = "Steel";

            // Assert: "Steel" is visible, parent class and group are visible and expanded, "Concrete" is hidden.
            Assert.IsTrue(element1.IsVisible);
            Assert.IsFalse(element2.IsVisible);

            Assert.IsTrue(childClass.IsVisible);
            Assert.IsTrue(childClass.IsExpanded);

            Assert.IsTrue(group.IsVisible);
            Assert.IsTrue(group.IsExpanded);

            // Act: Clear search
            vm.SearchText = "";

            // Assert: Everything is visible and collapsed
            Assert.IsTrue(element1.IsVisible);
            Assert.IsTrue(element2.IsVisible);
            Assert.IsTrue(childClass.IsVisible);
            Assert.IsFalse(childClass.IsExpanded);
            Assert.IsTrue(group.IsVisible);
            Assert.IsTrue(group.IsExpanded);
        }
    }
}

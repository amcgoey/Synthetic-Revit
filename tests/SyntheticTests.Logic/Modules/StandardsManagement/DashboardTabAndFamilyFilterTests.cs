using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.Settings;
using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.Utilities;
using Synthetic.Modules.StandardsManagement.Engine;

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
            dynamic elem = Activator.CreateInstance(typeof(T), true)!;
            elem.Name = name;
            elem.Id = new ElementId(idVal);

            dynamic dynamicDoc = doc;
            dynamicDoc.AddElement(elem, elem.Id);

            return (T)elem;
        }

        private class FakeExtractionOrchestrator : IStandardsExtractionOrchestrator
        {
            public List<ObjectModel> ExtractedModels { get; } = new List<ObjectModel>();

            public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null)
            {
                return ExtractedModels;
            }

            public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null, bool isTemplate = false)
            {
                return ExtractedModels;
            }
        }

        private class FakeExtractionOrchestratorForMultipleDocuments : IStandardsExtractionOrchestrator
        {
            public Dictionary<Document, List<ObjectModel>> ExtractedModels { get; } = new Dictionary<Document, List<ObjectModel>>();

            public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null)
            {
                return ExtractedModels.TryGetValue(doc, out var list) ? list : new List<ObjectModel>();
            }

            public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null, bool isTemplate = false)
            {
                return ExtractedModels.TryGetValue(doc, out var list) ? list : new List<ObjectModel>();
            }
        }

        [Test]
        public void VerifyTabCreationAndLifecycle_AddsTabsForSelectedDocuments()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService);
            dynamic mockDoc1 = Activator.CreateInstance(typeof(Document), true)!;
            mockDoc1.Title = "Model A";
            mockDoc1.PathName = @"C:\Projects\ModelA.rvt";

            dynamic mockDoc2 = Activator.CreateInstance(typeof(Document), true)!;
            mockDoc2.Title = "Model B";
            mockDoc2.PathName = @"C:\Projects\ModelB.rvt";

            parent.MockOpenDocuments = new List<Document> { (Document)mockDoc1, (Document)mockDoc2 };

            var fakeOrchestrator = new FakeExtractionOrchestrator();
            var treeVM = new StandardsSourceTreeViewModel(parent, null, _doc, _fakeDialogService, fakeOrchestrator, new StandardSerializationEngine());

            // Act
            treeVM.AddRevitModelCommand.Execute(null);

            // Assert
            Assert.AreEqual(2, treeVM.AvailableSources.Count, "Should have 2 sources loaded.");
            Assert.IsTrue(treeVM.AvailableSources.Any(s => s.DisplayName == "Model A"));
            Assert.IsTrue(treeVM.AvailableSources.Any(s => s.DisplayName == "Model B"));
            Assert.IsTrue(treeVM.AvailableSources.All(s => s.IsRevitSource));
        }

        [Test]
        public void VerifyResourceCleanupOnClose_RemovesTabAndPurgesHierarchy()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService);
            dynamic mockDoc = Activator.CreateInstance(typeof(Document), true)!;
            mockDoc.Title = "Model A";
            mockDoc.PathName = @"C:\Projects\ModelA.rvt";

            // Add a mock element so the tab has hierarchical data
            CreateMockElement<Material>((Document)mockDoc, "Steel", 501);

            parent.MockOpenDocuments = new List<Document> { (Document)mockDoc };

            var treeVM = new StandardsSourceTreeViewModel(parent, null, _doc, _fakeDialogService, new StandardsExtractionOrchestrator(new RevitIdentityService(), new StandardSerializationEngine()), new StandardSerializationEngine());
            treeVM.AddRevitModelCommand.Execute(null);

            var addedSource = treeVM.AvailableSources.FirstOrDefault(s => s.DisplayName == "Model A");
            Assert.IsNotNull(addedSource, "Tab should be added.");
            Assert.IsTrue(addedSource.SourceHierarchy.Count > 0, "Hierarchy should not be empty.");

            // Act
            treeVM.CloseSourceCommand.Execute(addedSource);

            // Assert
            Assert.IsFalse(treeVM.AvailableSources.Contains(addedSource), "Tab should be removed from AvailableSources.");
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
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, settings);

            try
            {
                var treeVM = parent.SourceTreeViewModel;

                // Assert
                Assert.AreEqual(1, treeVM.AvailableSources.Count, "Should load default firm standard.");
                var defaultSource = treeVM.AvailableSources[0];
                Assert.AreEqual("Default Firm Standard", defaultSource.DisplayName);

                // Assert that checkboxes are checked
                Assert.IsTrue(defaultSource.SourceHierarchy.Count > 0);
                foreach (var group in defaultSource.SourceHierarchy)
                {
                    Assert.IsTrue(group.IsChecked == true, "Hierarchical tree nodes should be checked on launch.");
                }

                // Assert that action queue remains empty
                Assert.AreEqual(0, parent.StagingQueue.Count, "Action queue must remain empty on launch.");
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
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService);

            dynamic doc1 = Activator.CreateInstance(typeof(Document), true)!;
            doc1.Title = "Doc 1";
            CreateMockElement<Material>((Document)doc1, "Aluminum", 601);

            dynamic doc2 = Activator.CreateInstance(typeof(Document), true)!;
            doc2.Title = "Doc 2";
            CreateMockElement<Material>((Document)doc2, "Copper", 602);

            parent.MockOpenDocuments = new List<Document> { (Document)doc1, (Document)doc2 };

            var fakeOrchestrator = new FakeExtractionOrchestratorForMultipleDocuments();
            fakeOrchestrator.ExtractedModels[(Document)doc1] = new List<ObjectModel> { new MaterialModel { Name = "Aluminum", UniqueId = "601" } };
            fakeOrchestrator.ExtractedModels[(Document)doc2] = new List<ObjectModel> { new MaterialModel { Name = "Copper", UniqueId = "602" } };

            var treeVM = new StandardsSourceTreeViewModel(parent, null, _doc, _fakeDialogService, fakeOrchestrator, new StandardSerializationEngine());

            // Act
            treeVM.AddRevitModelCommand.Execute(null);

            // Assert
            var source1 = treeVM.AvailableSources.FirstOrDefault(s => s.DisplayName == "Doc 1");
            var source2 = treeVM.AvailableSources.FirstOrDefault(s => s.DisplayName == "Doc 2");

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
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService);

            dynamic doc = Activator.CreateInstance(typeof(Document), true)!;
            doc.Title = "Filter Model";
            CreateMockElement<TextNoteType>((Document)doc, "Arial 3/32", 701);
            CreateMockElement<Material>((Document)doc, "Brass", 702);

            parent.MockOpenDocuments = new List<Document> { (Document)doc };

            // Set up document selection dialog mock to NOT select Annotations
            parent.ShowDocumentSelectionDialog = dialogVM =>
            {
                dialogVM.OpenDocuments[0].IsSelected = true;
                var annotationsGroup = dialogVM.FilterHierarchy.FirstOrDefault(g => g.Name == "Annotations");
                if (annotationsGroup != null)
                {
                    annotationsGroup.IsChecked = false;
                }
                return true;
            };

            var treeVM = new StandardsSourceTreeViewModel(parent, null, _doc, _fakeDialogService, new StandardsExtractionOrchestrator(new RevitIdentityService(), new StandardSerializationEngine()), new StandardSerializationEngine());

            // Act
            treeVM.AddRevitModelCommand.Execute(null);

            // Assert
            var source = treeVM.AvailableSources.FirstOrDefault(s => s.DisplayName == "Filter Model");
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
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var treeVM = new StandardsSourceTreeViewModel(parent, null, _doc, _fakeDialogService, new FakeExtractionOrchestrator(), new StandardSerializationEngine());
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };

            var group = new StandardGroupModel { Name = "Materials" };
            var childClass = new StandardClassModel { Name = "Metal", Parent = group };
            var element1 = new StandardElementModel(new MaterialModel { Name = "Steel" }) { Parent = childClass };
            var element2 = new StandardElementModel(new MaterialModel { Name = "Concrete" }) { Parent = childClass };

            childClass.Children.Add(element1);
            childClass.Children.Add(element2);
            group.Children.Add(childClass);
            source.SourceHierarchy.Add(group);

            treeVM.AvailableSources.Add(source);

            // Act: Filter by "Steel"
            treeVM.SearchText = "Steel";

            // Assert: "Steel" is visible, parent class and group are visible and expanded, "Concrete" is hidden.
            Assert.IsTrue(element1.IsVisible);
            Assert.IsFalse(element2.IsVisible);

            Assert.IsTrue(childClass.IsVisible);
            Assert.IsTrue(childClass.IsExpanded);

            Assert.IsTrue(group.IsVisible);
            Assert.IsTrue(group.IsExpanded);

            // Act: Clear search
            treeVM.SearchText = "";

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

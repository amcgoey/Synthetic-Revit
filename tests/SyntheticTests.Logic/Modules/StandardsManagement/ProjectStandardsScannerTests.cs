using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.Shared.UI;
using Synthetic.Settings;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class ProjectStandardsScannerTests
    {
        private Document _doc = null!;
        private ProjectStandardsDashboardViewModel _vm = null!;
        private System.Reflection.MethodInfo _extractMethod = null!;

        [SetUp]
        public void SetUp()
        {
            // Set up mock Document and ViewModel
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService(GuardrailResult.Overwrite);
            var settings = new StandardsSettings();

            _vm = DashboardTestFactory.Create(_doc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);

            _extractMethod = typeof(StandardsSourceTreeViewModel)
                .GetMethod("ExtractRevitElements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Assert.IsNotNull(_extractMethod, "ExtractRevitElements method not found.");
        }

        private List<ElementModel> InvokeExtract(List<string>? selectedGroupings)
        {
            return (List<ElementModel>)_extractMethod.Invoke(_vm.SourceTreeViewModel, new object?[] { _doc, false, false, selectedGroupings })!;
        }

        [Test]
        public void ExtractRevitElements_WhenGroupingsNull_ExtractsEverything()
        {
            // Arrange
            // 1. TextNoteType
            var textType = (TextNoteType)Activator.CreateInstance(typeof(TextNoteType), true)!;
            textType.Name = "Text Note Type A";
            textType.GetType().GetProperty("Id")?.SetValue(textType, new ElementId(101));
            ((dynamic)_doc).AddElement(textType, textType.Id);

            // 2. WallType
            var wallType = (WallType)Activator.CreateInstance(typeof(WallType), true)!;
            wallType.Name = "Wall Type B";
            wallType.GetType().GetProperty("Id")?.SetValue(wallType, new ElementId(102));
            ((dynamic)_doc).AddElement(wallType, wallType.Id);

            // Act
            var result = InvokeExtract(null);

            // Assert: everything should be extracted
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();
            Assert.Contains("Text Note Type A", names);
            Assert.Contains("Wall Type B", names);
        }

        [Test]
        public void ExtractRevitElements_WhenSpecificClassSelected_ExtractsOnlyThatClass()
        {
            // Arrange
            // 1. TextNoteType
            var textType = (TextNoteType)Activator.CreateInstance(typeof(TextNoteType), true)!;
            textType.Name = "Target Text Note";
            textType.GetType().GetProperty("Id")?.SetValue(textType, new ElementId(201));
            ((dynamic)_doc).AddElement(textType, textType.Id);

            // 2. WallType
            var wallType = (WallType)Activator.CreateInstance(typeof(WallType), true)!;
            wallType.Name = "Excluded Wall Type";
            wallType.GetType().GetProperty("Id")?.SetValue(wallType, new ElementId(202));
            ((dynamic)_doc).AddElement(wallType, wallType.Id);

            // Act: Select only "Text Note Types"
            var selectedGroupings = new List<string> { "Text Note Types" };
            var result = InvokeExtract(selectedGroupings);

            // Assert
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();
            Assert.Contains("Target Text Note", names);
            Assert.False(names.Contains("Excluded Wall Type"), "Wall Type should be excluded since it wasn't selected.");
        }

        [Test]
        public void ExtractRevitElements_WhenViewsSelected_ExtractsOnlyViewsNotTemplates()
        {
            // Arrange
            // 1. Standard View (IsTemplate = false)
            var standardView = (ViewPlan)Activator.CreateInstance(typeof(ViewPlan), true)!;
            standardView.Name = "Mock Standard View";
            ((dynamic)standardView).IsTemplate = false;
            standardView.GetType().GetProperty("Id")?.SetValue(standardView, new ElementId(301));
            ((dynamic)_doc).AddElement(standardView, standardView.Id);

            // 2. View Template (IsTemplate = true)
            var viewTemplate = (ViewPlan)Activator.CreateInstance(typeof(ViewPlan), true)!;
            viewTemplate.Name = "Mock View Template";
            ((dynamic)viewTemplate).IsTemplate = true;
            viewTemplate.GetType().GetProperty("Id")?.SetValue(viewTemplate, new ElementId(302));
            ((dynamic)_doc).AddElement(viewTemplate, viewTemplate.Id);

            // Act: Select only "Views"
            var selectedGroupings = new List<string> { "Views" };
            var result = InvokeExtract(selectedGroupings);

            // Assert
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();
            Assert.Contains("Mock Standard View", names);
            Assert.False(names.Contains("Mock View Template"), "View Template should be excluded.");
        }

        [Test]
        public void ExtractRevitElements_WhenViewTemplatesSelected_ExtractsOnlyTemplatesNotViews()
        {
            // Arrange
            // 1. Standard View (IsTemplate = false)
            var standardView = (ViewPlan)Activator.CreateInstance(typeof(ViewPlan), true)!;
            standardView.Name = "Mock Standard View";
            ((dynamic)standardView).IsTemplate = false;
            standardView.GetType().GetProperty("Id")?.SetValue(standardView, new ElementId(401));
            ((dynamic)_doc).AddElement(standardView, standardView.Id);

            // 2. View Template (IsTemplate = true)
            var viewTemplate = (ViewPlan)Activator.CreateInstance(typeof(ViewPlan), true)!;
            viewTemplate.Name = "Mock View Template";
            ((dynamic)viewTemplate).IsTemplate = true;
            viewTemplate.GetType().GetProperty("Id")?.SetValue(viewTemplate, new ElementId(402));
            ((dynamic)_doc).AddElement(viewTemplate, viewTemplate.Id);

            // Act: Select only "View Templates"
            var selectedGroupings = new List<string> { "View Templates" };
            var result = InvokeExtract(selectedGroupings);

            // Assert
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();
            Assert.Contains("Mock View Template", names);
            Assert.False(names.Contains("Mock Standard View"), "Standard View should be excluded.");
        }

        [Test]
        public void ExtractRevitElements_WhenFamilySymbolsSelected_ExtractsCorrectlyBasedOnCategory()
        {
            // Arrange
            // 1. Title Block symbol
            var titleBlockSymbol = (FamilySymbol)Activator.CreateInstance(typeof(FamilySymbol), true)!;
            titleBlockSymbol.Name = "Mock Title Block";
            var titleBlockCat = (Category)Activator.CreateInstance(typeof(Category), true)!;
            ((dynamic)titleBlockCat).Name = "Title Blocks";
            titleBlockSymbol.GetType().GetProperty("Category")?.SetValue(titleBlockSymbol, titleBlockCat);
            titleBlockSymbol.GetType().GetProperty("Id")?.SetValue(titleBlockSymbol, new ElementId(501));
            titleBlockCat.GetType().GetProperty("Id")?.SetValue(titleBlockCat, new ElementId((int)BuiltInCategory.OST_TitleBlocks));
            ((dynamic)_doc).AddElement(titleBlockSymbol, titleBlockSymbol.Id);

            // 2. Detail Components symbol
            var detailSymbol = (FamilySymbol)Activator.CreateInstance(typeof(FamilySymbol), true)!;
            detailSymbol.Name = "Mock Detail Component";
            var detailCat = (Category)Activator.CreateInstance(typeof(Category), true)!;
            ((dynamic)detailCat).Name = "Detail Items";
            detailSymbol.GetType().GetProperty("Category")?.SetValue(detailSymbol, detailCat);
            detailSymbol.GetType().GetProperty("Id")?.SetValue(detailSymbol, new ElementId(502));
            detailCat.GetType().GetProperty("Id")?.SetValue(detailCat, new ElementId((int)BuiltInCategory.OST_DetailComponents));
            ((dynamic)_doc).AddElement(detailSymbol, detailSymbol.Id);

            // Act: Select only "Title Blocks"
            var selectedGroupings = new List<string> { "Title Blocks" };
            var result = InvokeExtract(selectedGroupings);

            // Assert
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();
            Assert.Contains("Mock Title Block", names);
            Assert.False(names.Contains("Mock Detail Component"), "Detail Components should be excluded.");
        }

        [Test]
        public void ExtractRevitElements_WhenToposolidTypesSelected_ExtractsCorrectly()
        {
            // Arrange
            var toposolidType = (ToposolidType)Activator.CreateInstance(typeof(ToposolidType), true)!;
            toposolidType.Name = "Mock Toposolid Type";
            toposolidType.GetType().GetProperty("Id")?.SetValue(toposolidType, new ElementId(601));
            ((dynamic)_doc).AddElement(toposolidType, toposolidType.Id);

            // Act: Select only "Toposolid Types"
            var selectedGroupings = new List<string> { "Toposolid Types" };
            var result = InvokeExtract(selectedGroupings);

            // Assert
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();
            Assert.Contains("Mock Toposolid Type", names);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.Platform.Revit;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class RevitFamilyEnforcerTests
    {
        private Document _doc = null!;
        private FakeSerializationEngine _fakeEngine = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeEngine = new FakeSerializationEngine();
        }

        [Test]
        public void Enforce_WithTitleBlocksOnlyFilter_ProcessesOnlyTitleBlocks()
        {
            // Arrange
            var enforcer = new RevitFamilyEnforcer(_fakeEngine);

            // Create title block family
            var titleBlockFamily = (Family)Activator.CreateInstance(typeof(Family), true)!;
            titleBlockFamily.Name = "TitleBlockFam";
            dynamic dTitleBlock = titleBlockFamily;
            dTitleBlock.IsEditable = true;
            
            var cat1 = (Category)Activator.CreateInstance(typeof(Category), true)!;
            cat1.GetType().GetProperty("Id")?.SetValue(cat1, new ElementId((long)BuiltInCategory.OST_TitleBlocks));
            titleBlockFamily.FamilyCategory = cat1;
            titleBlockFamily.GetType().GetProperty("Id")?.SetValue(titleBlockFamily, new ElementId(1001));
            ((dynamic)_doc).AddElement(titleBlockFamily, titleBlockFamily.Id);

            // Create non-title block family
            var wallFamily = (Family)Activator.CreateInstance(typeof(Family), true)!;
            wallFamily.Name = "WallFam";
            dynamic dWall = wallFamily;
            dWall.IsEditable = true;
            
            var cat2 = (Category)Activator.CreateInstance(typeof(Category), true)!;
            cat2.GetType().GetProperty("Id")?.SetValue(cat2, new ElementId((long)BuiltInCategory.OST_Walls));
            wallFamily.FamilyCategory = cat2;
            wallFamily.GetType().GetProperty("Id")?.SetValue(wallFamily, new ElementId(1002));
            ((dynamic)_doc).AddElement(wallFamily, wallFamily.Id);

            var options = new StandardsExecutionOptions
            {
                ProcessFamilies = true,
                CategoryFilter = "Title Blocks Only"
            };

            var processedFamilies = new List<string>();
            Action<string, string, int> reportProgress = (task, detail, count) =>
            {
                if (detail.Contains("Updating family"))
                {
                    processedFamilies.Add(detail);
                }
            };

            var results = new List<SerializationResultModel>();

            // Act
            enforcer.Enforce(_doc, new List<ElementModel>(), options, reportProgress, results, CancellationToken.None);

            // Assert
            Assert.AreEqual(1, processedFamilies.Count);
            Assert.IsTrue(processedFamilies[0].Contains("TitleBlockFam"));
            Assert.IsFalse(processedFamilies.Any(f => f.Contains("WallFam")));
        }

        [Test]
        public void Enforce_PassesDeleteAllWarningsRecordedPreprocessorToSerializationEngine()
        {
            // Arrange
            var enforcer = new RevitFamilyEnforcer(_fakeEngine);

            var titleBlockFamily = (Family)Activator.CreateInstance(typeof(Family), true)!;
            titleBlockFamily.Name = "TestFamily";
            dynamic dFam = titleBlockFamily;
            dFam.IsEditable = true;
            
            var cat = (Category)Activator.CreateInstance(typeof(Category), true)!;
            cat.GetType().GetProperty("Id")?.SetValue(cat, new ElementId((long)BuiltInCategory.OST_TitleBlocks));
            titleBlockFamily.FamilyCategory = cat;
            titleBlockFamily.GetType().GetProperty("Id")?.SetValue(titleBlockFamily, new ElementId(2001));
            ((dynamic)_doc).AddElement(titleBlockFamily, titleBlockFamily.Id);

            var options = new StandardsExecutionOptions
            {
                ProcessFamilies = true
            };

            var results = new List<SerializationResultModel>();

            // Act
            enforcer.Enforce(_doc, new List<ElementModel>(), options, (t, d, c) => { }, results, CancellationToken.None);

            // Assert
            Assert.IsNotNull(_fakeEngine.LastFailuresPreprocessor, "FailuresPreprocessor must be passed to ToRevit");
            Assert.IsInstanceOf<CompositeFailuresPreprocessor>(_fakeEngine.LastFailuresPreprocessor);
            var composite = (CompositeFailuresPreprocessor)_fakeEngine.LastFailuresPreprocessor!;
            Assert.IsTrue(composite.AllowBlanketSuppression, "AllowBlanketSuppression must be enabled on DeleteAllWarningsRecorded preprocessor");

            // Verify pure policy warning deletion and telemetry recording on the attached policy
            var warningRecord = new FailureRecord(
                BuiltInFailures.RoomFailures.RoomNotEnclosed.Guid,
                FailureSeverityLevel.Warning,
                "Family enforcement warning");
            FailureRecord evaluated = composite.Policy.Evaluate(warningRecord);
            Assert.AreEqual(FailureHandlingAction.Deleted, evaluated.ActionTaken);
            Assert.IsTrue(composite.Report.WarningTripped);
            Assert.AreEqual(1, composite.Report.Records.Count);
        }

        [Test]
        public void Enforce_DelegatesAllStandardsToSerializationEngine_WithoutStrippingValidFamilyStandards()
        {
            // Arrange
            var enforcer = new RevitFamilyEnforcer(_fakeEngine);

            var family = (Family)Activator.CreateInstance(typeof(Family), true)!;
            family.Name = "AnnotationFamily";
            dynamic dFam = family;
            dFam.IsEditable = true;

            var cat = (Category)Activator.CreateInstance(typeof(Category), true)!;
            cat.GetType().GetProperty("Id")?.SetValue(cat, new ElementId((long)BuiltInCategory.OST_TitleBlocks));
            family.FamilyCategory = cat;
            family.GetType().GetProperty("Id")?.SetValue(family, new ElementId(3001));
            ((dynamic)_doc).AddElement(family, family.Id);

            var options = new StandardsExecutionOptions
            {
                ProcessFamilies = true
            };

            var linePattern = new LinePatternElementModel { Name = "Dash 1/8", Class = "Autodesk.Revit.DB.LinePatternElement" };
            var fillPattern = new FillPatternElementModel { Name = "Diagonal Crosshatch", Class = "Autodesk.Revit.DB.FillPatternElement" };
            var category = new CategoryModel { Name = "Generic Annotations", Class = "Autodesk.Revit.DB.Category" };
            var dimensionType = new DimensionTypeModel { Name = "Linear 3/32 Arial", Class = "Autodesk.Revit.DB.DimensionType" };
            var filledRegionType = new FilledRegionTypeModel { Name = "Solid Black", Class = "Autodesk.Revit.DB.FilledRegionType" };
            var textNoteType = new ElementTypeModel { Name = "1/8 Arial", Class = "Autodesk.Revit.DB.TextNoteType" };
            var textElementType = new ElementTypeModel { Name = "Family Label 1/8", Class = "Autodesk.Revit.DB.TextElementType" };
            var modelTextType = new ElementTypeModel { Name = "3D Text 6in", Class = "Autodesk.Revit.DB.ModelTextType" };
            var wallType = new HostObjTypeModel { Name = "Generic 8\"", Class = "Autodesk.Revit.DB.WallType" };
            var material = new MaterialModel { Name = "Default Material", Class = "Autodesk.Revit.DB.Material" };

            var standards = new List<ElementModel>
            {
                linePattern,
                fillPattern,
                category,
                dimensionType,
                filledRegionType,
                textNoteType,
                textElementType,
                modelTextType,
                wallType,
                material
            };

            var results = new List<SerializationResultModel>();

            // Act
            enforcer.Enforce(_doc, standards, options, (t, d, c) => { }, results, CancellationToken.None);

            // Assert
            Assert.IsNotNull(_fakeEngine.LastPassedModels, "ToRevit must be called by RevitFamilyEnforcer.");
            Assert.AreEqual(10, _fakeEngine.LastPassedModels!.Count, "All standards must be passed to ToRevit without manual stripping.");
            CollectionAssert.Contains(_fakeEngine.LastPassedModels, linePattern);
            CollectionAssert.Contains(_fakeEngine.LastPassedModels, fillPattern);
            CollectionAssert.Contains(_fakeEngine.LastPassedModels, category);
            CollectionAssert.Contains(_fakeEngine.LastPassedModels, dimensionType);
            CollectionAssert.Contains(_fakeEngine.LastPassedModels, filledRegionType);
            CollectionAssert.Contains(_fakeEngine.LastPassedModels, textNoteType);
            CollectionAssert.Contains(_fakeEngine.LastPassedModels, textElementType);
            CollectionAssert.Contains(_fakeEngine.LastPassedModels, modelTextType);
            CollectionAssert.Contains(_fakeEngine.LastPassedModels, wallType);
            CollectionAssert.Contains(_fakeEngine.LastPassedModels, material);
            Assert.IsNotNull(_fakeEngine.LastPassedDoc);
            Assert.IsTrue(_fakeEngine.LastPassedDoc!.IsFamilyDocument, "Document passed to ToRevit must be a family document.");
        }

        private class FakeSerializationEngine : IStandardSerializationEngine
        {
            public IFailuresPreprocessor? LastFailuresPreprocessor { get; private set; }
            public List<ObjectModel>? LastPassedModels { get; private set; }
            public Document? LastPassedDoc { get; private set; }

            public IEnumerable<ObjectModel> ByRevit(IEnumerable<Element> elements, Document doc, bool isTemplate, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
            {
                return new List<ObjectModel>();
            }

            public IEnumerable<DuplicateClusterModel> Analyze(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
            {
                return new List<DuplicateClusterModel>();
            }

            public IEnumerable<SerializationResultModel> ToRevit(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default, IFailuresPreprocessor? failuresPreprocessor = null)
            {
                LastFailuresPreprocessor = failuresPreprocessor;
                LastPassedModels = models.ToList();
                LastPassedDoc = doc;
                return new List<SerializationResultModel>();
            }

            public ObjectModel? ExtractCategory(Category category, Document doc, bool isTemplate)
            {
                return null;
            }

            public IEnumerable<Element> GatherRootElements(Document doc, IReadOnlyCollection<string>? selectedGroupings = null, CancellationToken cancellationToken = default)
            {
                return new List<Element>();
            }
        }
    }
}

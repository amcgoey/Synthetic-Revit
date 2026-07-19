using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Modules.StandardsManagement.Engine;
using Synthetic.Modules.StandardsManagement.Models;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.MergeDuplicates.Models;

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

        private class FakeSerializationEngine : IStandardSerializationEngine
        {
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
                return new List<SerializationResultModel>();
            }

            public ObjectModel? ExtractCategory(Category category, Document doc, bool isTemplate)
            {
                return null;
            }
        }
    }
}

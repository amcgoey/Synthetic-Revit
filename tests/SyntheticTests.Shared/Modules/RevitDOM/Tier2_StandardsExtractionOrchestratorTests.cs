using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.Engine;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_StandardsExtractionOrchestratorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        private Material CreateMaterial(Document doc, string name)
        {
            ElementId matId = Material.Create(doc, name);
            return (Material)doc.GetElement(matId);
        }

        [Test]
        public void Extract_DeepDependencyRetrieval_WallTypeAndMaterial()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material testMaterial;
                WallType wallType;

                using (var t = new Transaction(doc, "Create Test Environment"))
                {
                    t.Start();
                    testMaterial = CreateMaterial(doc, "OrchestratorTestMaterial");

                    wallType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .First();

                    // Assign material to compound structure of wall type
                    CompoundStructure cs = wallType.GetCompoundStructure();
                    if (cs != null)
                    {
                        var layers = cs.GetLayers();
                        if (layers.Count > 0)
                        {
                            layers[0].MaterialId = testMaterial.Id;
                            cs.SetLayers(layers);
                            wallType.SetCompoundStructure(cs);
                        }
                    }
                    t.Commit();
                }

                var identityService = new RevitIdentityService();
                var orchestrator = new StandardsExtractionOrchestrator(identityService);

                // Act: Extract starting ONLY with the WallType
                var rootElements = new List<Element> { wallType };
                var results = orchestrator.Extract(doc, rootElements, null, false);

                // Assert
                Assert.IsNotNull(results, "Extraction results should not be null.");

                // Find WallType POCO and Material POCO in the output list
                var wallTypePoco = results.OfType<HostObjTypeModel>().FirstOrDefault(w => w.Name == wallType.Name);
                var materialPoco = results.OfType<MaterialModel>().FirstOrDefault(m => m.Name == "OrchestratorTestMaterial");

                Assert.IsNotNull(wallTypePoco, "WallType POCO should be extracted.");
                Assert.IsNotNull(materialPoco, "Material POCO should be extracted as a nested dependency.");

                // Assert DependencyOrigin is set correctly
                Assert.AreEqual(wallType.Name, materialPoco!.DependencyOrigin,
                    "Material's DependencyOrigin should match the WallType's Name that pulled it in.");
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}

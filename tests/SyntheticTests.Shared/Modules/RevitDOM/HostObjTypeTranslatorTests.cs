using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class HostObjTypeTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void HostObjTypeTranslator_ExtractAndInject_WallTypeCompoundStructure_RoundtripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Find template WallType to duplicate
                    var templateType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .FirstOrDefault();

                    Assert.IsNotNull(templateType, "No WallType template found in document.");

                    // Retrieve a valid material to use in layers
                    var material = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault();

                    Assert.IsNotNull(material, "No Material found in document.");

                    // Act - Extraction
                    var originalModel = templateType.ToModel(false);
                    Assert.IsNotNull(originalModel);
                    Assert.IsNotNull(originalModel.Structure);
                    Assert.IsNotEmpty(originalModel.Structure!.Layers);

                    // Modify the model for injection
                    var newModel = new HostObjTypeModel
                    {
                        Class = "Autodesk.Revit.DB.WallType",
                        Name = "Test_Custom_WallType",
                        Structure = new CompoundStructureModel
                        {
                            Layers = new List<SerialCompoundStructureLayer>
                            {
                                new SerialCompoundStructureLayer
                                {
                                    Function = "Structure",
                                    Width = 0.5, // 6 inches core
                                    MaterialId = material.Id.ToModel(doc, false),
                                    StructuralMaterial = true,
#if REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
                                    // Priority does not exist
#else
                                    Priority = 2
#endif
                                },
                                new SerialCompoundStructureLayer
                                {
                                    Function = "Finish1",
                                    Width = 0.1, // 1.2 inches finish
                                    MaterialId = material.Id.ToModel(doc, false),
                                    StructuralMaterial = false,
#if REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
                                    // Priority does not exist
#else
                                    Priority = 5
#endif
                                }
                            }
                        }
                    };

                    WallType? injectedType = null;
                    using (var t = new Transaction(doc, "Inject custom WallType"))
                    {
                        t.Start();
                        var translator = new HostObjTypeTranslator(new RevitIdentityService());
                        injectedType = translator.InjectSpecifics(newModel, null, doc) as WallType;
                        Assert.IsNotNull(injectedType);
                        t.Commit();
                    }

                    // Assert
                    Assert.AreEqual("Test_Custom_WallType", injectedType!.Name);
                    var cs = injectedType.GetCompoundStructure();
                    Assert.IsNotNull(cs);
                    Assert.AreEqual(2, cs.GetLayers().Count);
                    Assert.AreEqual(0.5, cs.GetLayers()[0].Width);
                    Assert.AreEqual(0.1, cs.GetLayers()[1].Width);
                    Assert.AreEqual(material.Id, cs.GetLayers()[0].MaterialId);

#if REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
                    // Priority not supported
#else
                    Assert.AreEqual(2, cs.GetLayerPriority(0));
                    Assert.AreEqual(5, cs.GetLayerPriority(1));
#endif

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void HostObjTypeTranslator_Inject_MissingMaterial_DegradesGracefully()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    var model = new HostObjTypeModel
                    {
                        Class = "Autodesk.Revit.DB.WallType",
                        Name = "Test_Degraded_WallType",
                        Structure = new CompoundStructureModel
                        {
                            Layers = new List<SerialCompoundStructureLayer>
                            {
                                new SerialCompoundStructureLayer
                                {
                                    Function = "Structure",
                                    Width = 0.5,
                                    MaterialId = new ElementIdModel
                                    {
                                        Name = "NonExistentMaterialName_xyz",
                                        Class = "Autodesk.Revit.DB.Material"
                                    },
                                    StructuralMaterial = true
                                }
                            }
                        }
                    };

                    SerializationResultModel.ClearWarnings();

                    WallType? injectedType = null;
                    using (var t = new Transaction(doc, "Inject degraded WallType"))
                    {
                        t.Start();
                        var translator = new HostObjTypeTranslator(new RevitIdentityService());
                        injectedType = translator.InjectSpecifics(model, null, doc) as WallType;
                        Assert.IsNotNull(injectedType);
                        t.Commit();
                    }

                    // Assert: Should downgrade to ElementId.InvalidElementId (<By Category>) and log warning
                    var cs = injectedType!.GetCompoundStructure();
                    Assert.AreEqual(ElementId.InvalidElementId, cs.GetLayers()[0].MaterialId);

                    var warnings = SerializationResultModel.CurrentThreadWarnings;
                    Assert.IsTrue(warnings.Any(w => w.Contains("Could not resolve Material: NonExistentMaterialName_xyz")),
                        "Expected warning was not logged to SerializationResultModel.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void HostObjTypeTranslator_Inject_ZeroWidthLayer_ThrowsAndAborts()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Create structure with invalid zero-thickness layer
                    var model = new HostObjTypeModel
                    {
                        Class = "Autodesk.Revit.DB.WallType",
                        Name = "Test_Invalid_WallType",
                        Structure = new CompoundStructureModel
                        {
                            Layers = new List<SerialCompoundStructureLayer>
                            {
                                new SerialCompoundStructureLayer
                                {
                                    Function = "Structure",
                                    Width = 0.0, // Invalid core thickness
                                    StructuralMaterial = true
                                }
                            }
                        }
                    };

                    // Assert: SetCompoundStructure should throw a native exception and hard-abort
                    Assert.Throws<Autodesk.Revit.Exceptions.ArgumentException>(() =>
                    {
                        using (var t = new Transaction(doc, "Try invalid inject"))
                        {
                            t.Start();
                            var translator = new HostObjTypeTranslator(new RevitIdentityService());
                            translator.InjectSpecifics(model, null, doc);
                            t.Commit();
                        }
                    });

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void HostObjTypeTranslator_ExtractSpecifics_ExtractsWallSweepsAsNonNull()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var templateType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(templateType);

                var translator = new HostObjTypeTranslator(new RevitIdentityService());
                var model = new HostObjTypeModel();

                translator.ExtractSpecifics(templateType, model, doc);

                Assert.IsNotNull(model.Structure);
                Assert.IsNotNull(model.Structure!.WallSweeps);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}

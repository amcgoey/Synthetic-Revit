using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class MaterialAssetEngineTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        private AppearanceAssetElement GetOrCreateAppearanceAssetTemplate(Document doc)
        {
            var template = new FilteredElementCollector(doc)
                .OfClass(typeof(AppearanceAssetElement))
                .Cast<AppearanceAssetElement>()
                .FirstOrDefault();

            if (template == null)
            {
                var assets = doc.Application.GetAssets(AssetType.Appearance);
                var asset = assets.FirstOrDefault(a => a.FindByName("generic_diffuse") != null)
                            ?? assets.FirstOrDefault();
                if (asset != null)
                {
                    template = AppearanceAssetElement.Create(doc, "TemplateAppearanceAsset", asset);
                }
            }

            Assert.IsNotNull(template, "An AppearanceAssetElement template should exist or be created from library assets.");
            return template!;
        }

        [Test]
        public void ExtractAppearance_WhenNoAsset_ReturnsNull()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                using (Transaction t = new Transaction(doc, "Setup"))
                {
                    t.Start();
                    ElementId matId = Material.Create(doc, "TestMat_NoAsset");
                    mat = doc.GetElement(matId) as Material;
                    t.Commit();
                }

                Assert.IsNotNull(mat);
                var model = MaterialAssetEngine.ExtractAppearance(mat);
                Assert.IsNull(model);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectAppearance_InPlaceMutation_UpdatesExistingAsset()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                AppearanceAssetElement? originalAsset = null;

                using (Transaction t = new Transaction(doc, "Setup Material"))
                {
                    t.Start();

                    // Find template to duplicate
                    var template = GetOrCreateAppearanceAssetTemplate(doc);

                    originalAsset = template.Duplicate("OriginalAsset_InPlace");
                    ElementId matId = Material.Create(doc, "TestMat_InPlace");
                    mat = doc.GetElement(matId) as Material;
                    mat!.AppearanceAssetId = originalAsset.Id;

                    t.Commit();
                }

                Assert.IsNotNull(mat);
                Assert.IsNotNull(originalAsset);

                var model = new AppearanceAssetModel
                {
                    Name = "OriginalAsset_InPlace", // Keep name same for in-place or mutate
                    Color = new ColorModel(100, 150, 200),
                    Transparency = 0.5,
                    Smoothness = 0.8
                };

                using (Transaction t = new Transaction(doc, "Inject Appearance"))
                {
                    t.Start();

                    MaterialAssetEngine.InjectAppearance(mat, model, doc);

                    t.Commit();
                }

                // Assert that the ID is the same
                Assert.AreEqual(originalAsset.Id, mat.AppearanceAssetId, "AppearanceAssetId should not have changed (in-place mutation).");

                // Extract and assert values updated
                var extracted = MaterialAssetEngine.ExtractAppearance(mat);
                Assert.IsNotNull(extracted);
                Assert.AreEqual("OriginalAsset_InPlace", extracted!.Name);
                Assert.IsNotNull(extracted.Color);
                Assert.AreEqual(100, extracted.Color!.Red);
                Assert.AreEqual(150, extracted.Color.Green);
                Assert.AreEqual(200, extracted.Color.Blue);
                Assert.AreEqual(0.5, extracted.Transparency, 1e-3);
                Assert.AreEqual(0.8, extracted.Smoothness, 1e-3);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectAppearance_NameMatching_AssignsExistingAsset()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                AppearanceAssetElement? existingAsset = null;

                using (Transaction t = new Transaction(doc, "Setup Asset"))
                {
                    t.Start();

                    var template = GetOrCreateAppearanceAssetTemplate(doc);

                    existingAsset = template.Duplicate("Shared_NameMatching_Asset");
                    ElementId matId = Material.Create(doc, "TestMat_NameMatching");
                    mat = doc.GetElement(matId) as Material;
                    // Leave mat.AppearanceAssetId as invalid (no asset assigned)

                    t.Commit();
                }

                Assert.IsNotNull(mat);
                Assert.IsNotNull(existingAsset);
                Assert.AreEqual(ElementId.InvalidElementId, mat.AppearanceAssetId);

                var model = new AppearanceAssetModel
                {
                    Name = "Shared_NameMatching_Asset",
                    Color = new ColorModel(50, 100, 150),
                    Transparency = 0.2,
                    Smoothness = 0.9
                };

                using (Transaction t = new Transaction(doc, "Inject Shared Asset"))
                {
                    t.Start();

                    MaterialAssetEngine.InjectAppearance(mat, model, doc);

                    t.Commit();
                }

                // Assert that the existing asset was assigned to the material
                Assert.AreEqual(existingAsset.Id, mat.AppearanceAssetId, "Material should be assigned to the existing matching asset.");

                // Assert that the properties were updated
                var extracted = MaterialAssetEngine.ExtractAppearance(mat);
                Assert.IsNotNull(extracted);
                Assert.AreEqual("Shared_NameMatching_Asset", extracted!.Name);
                Assert.IsNotNull(extracted.Color);
                Assert.AreEqual(50, extracted.Color!.Red);
                Assert.AreEqual(100, extracted.Color.Green);
                Assert.AreEqual(150, extracted.Color.Blue);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectAppearance_Creation_DuplicatesDefaultTemplate()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                using (Transaction t = new Transaction(doc, "Setup Material"))
                {
                    t.Start();
                    GetOrCreateAppearanceAssetTemplate(doc);
                    ElementId matId = Material.Create(doc, "TestMat_Creation");
                    mat = doc.GetElement(matId) as Material;
                    t.Commit();
                }

                Assert.IsNotNull(mat);
                Assert.AreEqual(ElementId.InvalidElementId, mat.AppearanceAssetId);

                var model = new AppearanceAssetModel
                {
                    Name = "BrandNewUniqueAsset_Creation",
                    Color = new ColorModel(200, 50, 50),
                    Transparency = 0.0,
                    Smoothness = 0.5
                };

                using (Transaction t = new Transaction(doc, "Inject Brand New"))
                {
                    t.Start();

                    MaterialAssetEngine.InjectAppearance(mat, model, doc);

                    t.Commit();
                }

                Assert.AreNotEqual(ElementId.InvalidElementId, mat.AppearanceAssetId, "A new appearance asset should have been created and assigned.");

                var extracted = MaterialAssetEngine.ExtractAppearance(mat);
                Assert.IsNotNull(extracted);
                Assert.AreEqual("BrandNewUniqueAsset_Creation", extracted!.Name);
                Assert.AreEqual(200, extracted.Color!.Red);
                Assert.AreEqual(0.5, extracted.Smoothness, 1e-3);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectStructural_InPlaceAndNameMatching_UpdatesCorrectly()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                PropertySetElement? originalPse = null;

                using (Transaction t = new Transaction(doc, "Setup Material"))
                {
                    t.Start();

                    StructuralAsset asset = new StructuralAsset("OriginalStrucAsset", StructuralAssetClass.Metal);
                    originalPse = PropertySetElement.Create(doc, asset);
                    ElementId matId = Material.Create(doc, "TestMat_Struc");
                    mat = doc.GetElement(matId) as Material;
                    mat!.SetMaterialAspectByPropertySet(MaterialAspect.Structural, originalPse.Id);

                    t.Commit();
                }

                Assert.IsNotNull(mat);
                Assert.IsNotNull(originalPse);

                var model = new StructuralAssetModel
                {
                    Name = "OriginalStrucAsset",
                    Behavior = "Isotropic",
                    StructuralAssetClass = "Metal",
                    Density = 490.0,
                    YoungModulus = 29000000.0,
                    PoissonRatio = 0.3
                };

                using (Transaction t = new Transaction(doc, "Inject Structural"))
                {
                    t.Start();

                    MaterialAssetEngine.InjectStructural(mat, model, doc);

                    t.Commit();
                }

                // Assert ID did not change (in-place)
                Assert.AreEqual(originalPse.Id, mat.StructuralAssetId);

                // Extract and assert
                var extracted = MaterialAssetEngine.ExtractStructural(mat);
                Assert.IsNotNull(extracted);
                Assert.AreEqual("OriginalStrucAsset", extracted!.Name);
                Assert.AreEqual("Isotropic", extracted.Behavior);
                Assert.AreEqual(490.0, extracted.Density, 1e-3);
                Assert.AreEqual(29000000.0, extracted.YoungModulus, 1e-3);
                Assert.AreEqual(0.3, extracted.PoissonRatio, 1e-3);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectThermal_InPlaceAndNameMatching_UpdatesCorrectly()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                PropertySetElement? originalPse = null;

                using (Transaction t = new Transaction(doc, "Setup Material"))
                {
                    t.Start();

                    ThermalAsset asset = new ThermalAsset("OriginalThermalAsset", ThermalMaterialType.Solid);
                    originalPse = PropertySetElement.Create(doc, asset);
                    ElementId matId = Material.Create(doc, "TestMat_Thermal");
                    mat = doc.GetElement(matId) as Material;
                    mat!.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, originalPse.Id);

                    t.Commit();
                }

                Assert.IsNotNull(mat);
                Assert.IsNotNull(originalPse);

                var model = new ThermalAssetModel
                {
                    Name = "OriginalThermalAsset",
                    ThermalMaterialType = "Solid",
                    Density = 150.0,
                    ThermalConductivity = 1.5,
                    SpecificHeat = 0.2,
                    Emissivity = 0.9
                };

                using (Transaction t = new Transaction(doc, "Inject Thermal"))
                {
                    t.Start();

                    MaterialAssetEngine.InjectThermal(mat, model, doc);

                    t.Commit();
                }

                // Assert ID did not change (in-place)
                Assert.AreEqual(originalPse.Id, mat.ThermalAssetId);

                // Extract and assert
                var extracted = MaterialAssetEngine.ExtractThermal(mat);
                Assert.IsNotNull(extracted);
                Assert.AreEqual("OriginalThermalAsset", extracted!.Name);
                Assert.AreEqual("Solid", extracted.ThermalMaterialType);
                Assert.AreEqual(150.0, extracted.Density, 1e-3);
                Assert.AreEqual(1.5, extracted.ThermalConductivity, 1e-3);
                Assert.AreEqual(0.2, extracted.SpecificHeat, 1e-3);
                Assert.AreEqual(0.9, extracted.Emissivity, 1e-3);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}

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
    public class MaterialTranslatorTests
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

        private FillPatternElement? GetFirstFillPattern(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault();
        }

        [Test]
        public void ExtractSpecifics_CorrectlyPopulatesColorsAndPatterns()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                FillPatternElement? fp = GetFirstFillPattern(doc);

                using (Transaction t = new Transaction(doc, "Setup Material"))
                {
                    t.Start();
                    var matId = Material.Create(doc, "TestMat_Extract_Colors");
                    mat = doc.GetElement(matId) as Material;

                    Assert.IsNotNull(mat);
                    mat!.CutForegroundPatternColor = new Color(255, 0, 0);
                    mat.CutBackgroundPatternColor = new Color(0, 255, 0);
                    mat.SurfaceForegroundPatternColor = new Color(0, 0, 255);
                    mat.SurfaceBackgroundPatternColor = new Color(128, 128, 128);

                    if (fp != null)
                    {
                        mat.CutForegroundPatternId = fp.Id;
                        mat.CutBackgroundPatternId = fp.Id;
                        mat.SurfaceForegroundPatternId = fp.Id;
                        mat.SurfaceBackgroundPatternId = fp.Id;
                    }
                    t.Commit();
                }

                var translator = new MaterialTranslator(new RevitIdentityService());
                var model = new MaterialModel();

                // Act
                translator.ExtractSpecifics(mat!, model, doc);

                // Assert
                Assert.IsNotNull(model.CutForegroundPatternColor);
                Assert.AreEqual(255, model.CutForegroundPatternColor!.Red);
                Assert.AreEqual(0, model.CutForegroundPatternColor!.Green);
                Assert.AreEqual(0, model.CutForegroundPatternColor!.Blue);

                Assert.IsNotNull(model.CutBackgroundPatternColor);
                Assert.AreEqual(0, model.CutBackgroundPatternColor!.Red);
                Assert.AreEqual(255, model.CutBackgroundPatternColor!.Green);
                Assert.AreEqual(0, model.CutBackgroundPatternColor!.Blue);

                Assert.IsNotNull(model.SurfaceForegroundPatternColor);
                Assert.AreEqual(0, model.SurfaceForegroundPatternColor!.Red);
                Assert.AreEqual(0, model.SurfaceForegroundPatternColor!.Green);
                Assert.AreEqual(255, model.SurfaceForegroundPatternColor!.Blue);

                Assert.IsNotNull(model.SurfaceBackgroundPatternColor);
                Assert.AreEqual(128, model.SurfaceBackgroundPatternColor!.Red);
                Assert.AreEqual(128, model.SurfaceBackgroundPatternColor!.Green);
                Assert.AreEqual(128, model.SurfaceBackgroundPatternColor!.Blue);

                if (fp != null)
                {
                    Assert.IsNotNull(model.CutForegroundPatternId);
                    Assert.AreEqual(fp.Name, model.CutForegroundPatternId!.Name);
                    Assert.IsNotNull(model.CutBackgroundPatternId);
                    Assert.AreEqual(fp.Name, model.CutBackgroundPatternId!.Name);
                    Assert.IsNotNull(model.SurfaceForegroundPatternId);
                    Assert.AreEqual(fp.Name, model.SurfaceForegroundPatternId!.Name);
                    Assert.IsNotNull(model.SurfaceBackgroundPatternId);
                    Assert.AreEqual(fp.Name, model.SurfaceBackgroundPatternId!.Name);
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ExtractSpecifics_DelegatesToMaterialAssetEngine()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                using (Transaction t = new Transaction(doc, "Setup Appearance Asset"))
                {
                    t.Start();
                    var template = GetOrCreateAppearanceAssetTemplate(doc);
                    var newAsset = template.Duplicate("ExtractAsset_Appearance");
                    var matId = Material.Create(doc, "TestMat_Extract_Asset");
                    mat = doc.GetElement(matId) as Material;
                    Assert.IsNotNull(mat);
                    mat!.AppearanceAssetId = newAsset.Id;
                    t.Commit();
                }

                var translator = new MaterialTranslator(new RevitIdentityService());
                var model = new MaterialModel();

                // Act
                translator.ExtractSpecifics(mat!, model, doc);

                // Assert
                Assert.IsNotNull(model.AppearanceAsset);
                Assert.AreEqual("ExtractAsset_Appearance", model.AppearanceAsset!.Name);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectSpecifics_CreatesMaterialWithProperties()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? createdElem = null;
                FillPatternElement? fp = GetFirstFillPattern(doc);

                using (Transaction trans = new Transaction(doc, "Test Create Material"))
                {
                    trans.Start();

                    var template = GetOrCreateAppearanceAssetTemplate(doc);
                    var translator = new MaterialTranslator(new RevitIdentityService());
                    var model = new MaterialModel
                    {
                        Name = "TestMat_Inject_Create",
                        CutForegroundPatternColor = new ColorModel(255, 100, 50),
                        SurfaceBackgroundPatternColor = new ColorModel(10, 20, 30),
                        AppearanceAsset = new AppearanceAssetModel
                        {
                            Name = "InjectAsset_Appearance",
                            Color = new ColorModel(50, 60, 70),
                            Transparency = 0.5,
                            Smoothness = 0.8
                        }
                    };

                    if (fp != null)
                    {
                        model.CutForegroundPatternId = fp.Id.ToModel(doc);
                    }

                    // Act
                    createdElem = translator.InjectSpecifics(model, null, doc);

                    trans.Commit();
                }

                // Assert
                Assert.IsNotNull(createdElem);
                Assert.AreEqual("TestMat_Inject_Create", createdElem!.Name);

                Assert.AreEqual(255, createdElem.CutForegroundPatternColor.Red);
                Assert.AreEqual(100, createdElem.CutForegroundPatternColor.Green);
                Assert.AreEqual(50, createdElem.CutForegroundPatternColor.Blue);

                Assert.AreEqual(10, createdElem.SurfaceBackgroundPatternColor.Red);
                Assert.AreEqual(20, createdElem.SurfaceBackgroundPatternColor.Green);
                Assert.AreEqual(30, createdElem.SurfaceBackgroundPatternColor.Blue);

                if (fp != null)
                {
                    Assert.AreEqual(fp.Id, createdElem.CutForegroundPatternId);
                }

                Assert.AreNotEqual(ElementId.InvalidElementId, createdElem.AppearanceAssetId);
                var assetElem = doc.GetElement(createdElem.AppearanceAssetId) as AppearanceAssetElement;
                Assert.IsNotNull(assetElem);
                Assert.AreEqual("InjectAsset_Appearance", assetElem!.Name);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectSpecifics_UpdatesExistingMaterial()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                using (Transaction t = new Transaction(doc, "Setup Material"))
                {
                    t.Start();
                    var matId = Material.Create(doc, "TestMat_Inject_Update");
                    mat = doc.GetElement(matId) as Material;
                    Assert.IsNotNull(mat);
                    mat!.CutForegroundPatternColor = new Color(0, 0, 0);
                    t.Commit();
                }

                var translator = new MaterialTranslator(new RevitIdentityService());
                var model = new MaterialModel
                {
                    Name = "TestMat_Inject_Update",
                    CutForegroundPatternColor = new ColorModel(100, 200, 255)
                };

                Material? updatedElem = null;

                using (Transaction trans = new Transaction(doc, "Test Update Material"))
                {
                    trans.Start();

                    // Act
                    updatedElem = translator.InjectSpecifics(model, mat, doc);

                    trans.Commit();
                }

                // Assert
                Assert.IsNotNull(updatedElem);
                Assert.AreEqual(mat!.Id, updatedElem!.Id);
                Assert.AreEqual(100, updatedElem.CutForegroundPatternColor.Red);
                Assert.AreEqual(200, updatedElem.CutForegroundPatternColor.Green);
                Assert.AreEqual(255, updatedElem.CutForegroundPatternColor.Blue);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}

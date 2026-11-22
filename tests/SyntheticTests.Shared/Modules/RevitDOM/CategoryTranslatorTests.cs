using System;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class CategoryTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void CategoryTranslator_ExtractSpecifics_OST_Walls_PresentsCorrectValues()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var wallsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                Assert.IsNotNull(wallsCat, "OST_Walls category should exist.");

                var translator = new CategoryTranslator(new RevitIdentityService());
                var model = new CategoryModel();

                translator.ExtractSpecifics(wallsCat, model, doc);

                Assert.AreEqual("Walls", model.Name);
                Assert.IsTrue(model.IsCuttable);
                Assert.IsNotNull(model.LineWeightProjection);
                Assert.IsNotNull(model.LineColor);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void CategoryTranslator_InjectSpecifics_ModifiesOST_WallsStyles()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var wallsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                Assert.IsNotNull(wallsCat);

                var translator = new CategoryTranslator(new RevitIdentityService());
                var model = new CategoryModel
                {
                    Name = "Walls",
                    LineWeightProjection = 5,
                    LineColor = new ColorModel(255, 0, 0)
                };

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Modify Walls Style"))
                    {
                        trans.Start();
                        var result = translator.InjectSpecifics(model, wallsCat, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(wallsCat);
                    Assert.AreEqual(5, wallsCat.GetLineWeight(GraphicsStyleType.Projection));
                    Assert.AreEqual(255, wallsCat.LineColor.Red);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void CategoryTranslator_InjectSpecifics_CreatesSubcategory()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new CategoryTranslator(new RevitIdentityService());
                var model = new CategoryModel
                {
                    Name = "TestSubcategory_" + Guid.NewGuid().ToString().Substring(0, 8),
                    ParentCategoryName = "Walls"
                };

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    Category? createdSub = null;
                    using (Transaction trans = new Transaction(doc, "Create Subcategory"))
                    {
                        trans.Start();
                        createdSub = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(createdSub);
                    Assert.AreEqual(model.Name, createdSub!.Name);
                    Assert.IsNotNull(createdSub.Parent);
                    Assert.AreEqual("Walls", createdSub.Parent.Name);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void CategoryTranslator_InjectSpecifics_UncuttableCategory_ThrowsNoException()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var dimsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Dimensions);
                Assert.IsNotNull(dimsCat, "OST_Dimensions category should exist.");
                Assert.IsFalse(dimsCat.IsCuttable, "Dimensions category should be uncuttable.");

                var translator = new CategoryTranslator(new RevitIdentityService());
                var model = new CategoryModel
                {
                    Name = "Dimensions",
                    LineWeightCut = 8,
                    LinePatternCut = new ElementIdModel { Id = -3000010, Name = "Solid" }
                };

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Inject Style"))
                    {
                        trans.Start();
                        Assert.DoesNotThrow(() => translator.InjectSpecifics(model, dimsCat, doc));
                        trans.Commit();
                    }

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void CategoryTranslator_InjectSpecifics_MissingParent_LogsWarningToResultModel()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new CategoryTranslator(new RevitIdentityService());
                var model = new CategoryModel
                {
                    Name = "TestSubcategory",
                    ParentCategoryName = "NonExistentParentCategory"
                };

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    SerializationResultModel.ClearWarnings();

                    using (Transaction trans = new Transaction(doc, "Inject Subcategory"))
                    {
                        trans.Start();
                        var result = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    var warnings = SerializationResultModel.CurrentThreadWarnings;
                    Assert.IsTrue(warnings.Any(w => w.Contains("Parent category 'NonExistentParentCategory' not found")), "Warning should be logged about missing parent category.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void CategoryMapping_SymmetricConversions_ResolvesCategory()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Category liveCategory = Category.GetCategory(doc, BuiltInCategory.OST_Walls);
                Assert.IsNotNull(liveCategory, "OST_Walls category should exist.");

                // Act
                CategoryIdModel idModel = liveCategory.ToCategoryIdModel(doc);
                CategoryModel catModel = liveCategory.ToCategoryModel(doc);

                Category resolvedFromIdModel = idModel.GetCategory(doc);
                Category resolvedFromCatModel = catModel.GetCategory(doc);

                // Assert
                Assert.IsNotNull(resolvedFromIdModel, "Should resolve from CategoryIdModel.");
                Assert.AreEqual(liveCategory.Id, resolvedFromIdModel.Id, "Resolved category ID should match.");
                Assert.IsNotNull(resolvedFromCatModel, "Should resolve from CategoryModel.");
                Assert.AreEqual(liveCategory.Id, resolvedFromCatModel.Id, "Resolved category ID should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}

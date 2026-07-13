using System;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class DimensionTypeTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void DimensionTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsDimensionType()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new DimensionTypeTranslator();
                var model = new DimensionTypeModel
                {
                    Name = "TestDimensionType_Duplicated",
                    DimensionStyle = "Linear"
                };

                DimensionType? duplicatedElem = null;

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Duplicate DimensionType"))
                    {
                        trans.Start();
                        duplicatedElem = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(duplicatedElem);
                    Assert.AreEqual("TestDimensionType_Duplicated", duplicatedElem!.Name);
                    Assert.AreEqual(DimensionStyleType.Linear, duplicatedElem!.StyleType);
                    Assert.AreNotEqual(DimensionTypeModel.InternalDimStyleName, duplicatedElem!.Name);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void DimensionTypeTranslator_InjectSpecifics_ThrowsIfNoTemplateFound()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new DimensionTypeTranslator();
                var model = new DimensionTypeModel
                {
                    Name = "TestDimensionType_NonExistentStyle",
                    DimensionStyle = "NonExistentStyleType"
                };

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    var ex = Assert.Throws<InvalidOperationException>(() =>
                    {
                        using (Transaction trans = new Transaction(doc, "Try Duplicate"))
                        {
                            trans.Start();
                            translator.InjectSpecifics(model, null, doc);
                            trans.Commit();
                        }
                    });

                    Assert.IsTrue(ex.Message.Contains("No template element of type 'DimensionType' with StyleType 'NonExistentStyleType' found"));

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}

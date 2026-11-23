using System;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using SyntheticTests.Fixtures;

namespace SyntheticTests.RevitDOM
{
    [TestFixture]
    [RevitAdapterTest]
    public class DimensionTypeTranslatorRevitTests : RevitTemplateRollbackFixture
    {
        [Test]
        public void DimensionTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsDimensionType()
        {
            WithRolledBackDocument(doc =>
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
            });
        }

        [Test]
        public void DimensionTypeTranslator_InjectSpecifics_ThrowsIfNoTemplateFound()
        {
            WithRolledBackDocument(doc =>
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
            });
        }
    }
}

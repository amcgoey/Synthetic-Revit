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
    public class DatumAndAnnotationTranslatorRevitTests : RevitTemplateRollbackFixture
    {
        [Test]
        public void GridTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsGridType()
        {
            WithRolledBackDocument(doc =>
            {
                var translator = new GridTypeTranslator();
                var model = new ElementTypeModel
                {
                    Name = "TestGridType_Duplicated",
                    Class = "Autodesk.Revit.DB.GridType"
                };

                GridType? duplicatedElem = null;

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Duplicate GridType"))
                    {
                        trans.Start();
                        duplicatedElem = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(duplicatedElem);
                    Assert.AreEqual("TestGridType_Duplicated", duplicatedElem!.Name);

                    // Roll back to keep the document clean
                    tg.RollBack();
                }
            });
        }

        [Test]
        public void LevelTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsLevelType()
        {
            WithRolledBackDocument(doc =>
            {
                var translator = new LevelTypeTranslator();
                var model = new ElementTypeModel
                {
                    Name = "TestLevelType_Duplicated",
                    Class = "Autodesk.Revit.DB.LevelType"
                };

                LevelType? duplicatedElem = null;

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Duplicate LevelType"))
                    {
                        trans.Start();
                        duplicatedElem = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(duplicatedElem);
                    Assert.AreEqual("TestLevelType_Duplicated", duplicatedElem!.Name);

                    tg.RollBack();
                }
            });
        }

        [Test]
        public void TextElementTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsTextNoteType()
        {
            WithRolledBackDocument(doc =>
            {
                var translator = new TextElementTypeTranslator();
                var model = new ElementTypeModel
                {
                    Name = "TestTextNoteType_Duplicated",
                    Class = "Autodesk.Revit.DB.TextNoteType"
                };

                ElementType? duplicatedElem = null;

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Duplicate TextNoteType"))
                    {
                        trans.Start();
                        duplicatedElem = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(duplicatedElem);
                    Assert.AreEqual("TestTextNoteType_Duplicated", duplicatedElem!.Name);

                    tg.RollBack();
                }
            });
        }

        [Test]
        public void GridTypeTranslator_InjectSpecifics_UsingElementTypeModel_DuplicatesTemplateAndReturnsGridType()
        {
            WithRolledBackDocument(doc =>
            {
                var translator = new GridTypeTranslator();
                var model = new ElementTypeModel
                {
                    Name = "TestGridType_Duplicated_ElementTypeModel",
                    Class = "Autodesk.Revit.DB.GridType"
                };

                GridType? duplicatedElem = null;

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Duplicate GridType"))
                    {
                        trans.Start();
                        duplicatedElem = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(duplicatedElem);
                    Assert.AreEqual("TestGridType_Duplicated_ElementTypeModel", duplicatedElem!.Name);

                    tg.RollBack();
                }
            });
        }

        [Test]
        public void LevelTypeTranslator_InjectSpecifics_UsingElementTypeModel_DuplicatesTemplateAndReturnsLevelType()
        {
            WithRolledBackDocument(doc =>
            {
                var translator = new LevelTypeTranslator();
                var model = new ElementTypeModel
                {
                    Name = "TestLevelType_Duplicated_ElementTypeModel",
                    Class = "Autodesk.Revit.DB.LevelType"
                };

                LevelType? duplicatedElem = null;

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Duplicate LevelType"))
                    {
                        trans.Start();
                        duplicatedElem = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(duplicatedElem);
                    Assert.AreEqual("TestLevelType_Duplicated_ElementTypeModel", duplicatedElem!.Name);

                    tg.RollBack();
                }
            });
        }

        [Test]
        public void SpotDimensionTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsSpotDimensionType()
        {
            WithRolledBackDocument(doc =>
            {
                var template = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpotDimensionType))
                    .FirstOrDefault();

                if (template != null)
                {
                    var translator = new SpotDimensionTypeTranslator();
                    var model = new ElementTypeModel
                    {
                        Name = "TestSpotDimensionType_Duplicated",
                        Class = "Autodesk.Revit.DB.SpotDimensionType"
                    };

                    ElementType? duplicatedElem = null;

                    using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                    {
                        tg.Start();

                        using (Transaction trans = new Transaction(doc, "Duplicate SpotDimensionType"))
                        {
                            trans.Start();
                            duplicatedElem = translator.InjectSpecifics(model, null, doc);
                            trans.Commit();
                        }

                        Assert.IsNotNull(duplicatedElem);
                        Assert.AreEqual("TestSpotDimensionType_Duplicated", duplicatedElem!.Name);

                        tg.RollBack();
                    }
                }
            });
        }
    }
}

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
    public class DatumAndAnnotationTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void GridTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsGridType()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new GridTypeTranslator();
                var model = new GridTypeModel
                {
                    Name = "TestGridType_Duplicated"
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
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void LevelTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsLevelType()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new LevelTypeTranslator();
                var model = new LevelTypeModel
                {
                    Name = "TestLevelType_Duplicated"
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
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void TextElementTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsTextNoteType()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
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
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}

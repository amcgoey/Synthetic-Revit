using System;
using NUnit.Framework;
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
    public class ColorTranslatorRevitTests : RevitTemplateRollbackFixture
    {
        protected override RevitBaselineDocumentKind BaselineKind => RevitBaselineDocumentKind.None;

        [Test]
        public void ColorConversions_Symmetric()
        {
            var color = new Color(255, 128, 64);
            ColorModel colorModel = color.ToModel();
            Color colorBack = colorModel.ToColor();

            Assert.IsTrue(colorModel.IsValid);
            Assert.AreEqual(color.Red, colorModel.Red);
            Assert.AreEqual(color.Green, colorModel.Green);
            Assert.AreEqual(color.Blue, colorModel.Blue);
            Assert.AreEqual(color.Red, colorBack.Red);
            Assert.AreEqual(color.Green, colorBack.Green);
            Assert.AreEqual(color.Blue, colorBack.Blue);
        }
    }
}

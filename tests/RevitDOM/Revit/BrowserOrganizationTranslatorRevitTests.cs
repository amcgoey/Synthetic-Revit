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
    public class BrowserOrganizationTranslatorRevitTests : RevitTemplateRollbackFixture
    {
        [Test]
        public void BrowserOrganizationTranslator_Inject_Null_LogsWarning()
        {
            WithRolledBackDocument(doc =>
            {
                SerializationResultModel.ClearWarnings();

                var model = new BrowserOrganizationModel
                {
                    Name = "NonExistentBrowserOrg",
                    IsTemplate = false
                };

                var translator = new BrowserOrganizationTranslator();

                // Act
                BrowserOrganization? result = null;
                using (var t = new Transaction(doc, "Inject BrowserOrg"))
                {
                    t.Start();
                    result = translator.InjectSpecifics(model, null, doc);
                    t.Commit();
                }

                // Assert
                Assert.IsNull(result, "Programmatic creation of BrowserOrganization should return null.");
                
                var warnings = SerializationResultModel.CurrentThreadWarnings;
                Assert.IsTrue(warnings.Count > 0, "A warning should be logged.");
                Assert.IsTrue(warnings.Any(w => w.Contains("Revit API prevents the programmatic creation of new Browser Organizations")), 
                    "Warning message should inform the user about the Revit API limitation.");
            });
        }
    }
}

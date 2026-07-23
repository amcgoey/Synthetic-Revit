using System;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;

namespace SyntheticTests
{
    [TestFixture]
    public class ParameterDefinitionSpecIntegrationTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ParameterDefinitionSpec_FromParameter_HydratesFromLiveParameter()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                Parameter? testParam = doc.ProjectInformation.get_Parameter(BuiltInParameter.PROJECT_NAME);
                Assert.IsNotNull(testParam, "PROJECT_NAME parameter should exist on ProjectInformation.");

                var spec = ParameterDefinitionSpec.FromParameter(testParam);

                Assert.IsNotNull(spec, "ParameterDefinitionSpec should be non-null.");
                Assert.IsNotNull(spec.Group, "Group should be populated from live parameter.");
                Assert.IsNotNull(spec.SpecType, "SpecType should be populated from live parameter.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterDefinitionSpec_CreateDefault_ReturnsValidDefaults()
        {
            var spec = ParameterDefinitionSpec.CreateDefault();

            Assert.IsNotNull(spec, "Default spec should not be null.");
            Assert.IsNotNull(spec.Group, "Default group should not be null.");
            Assert.IsNotNull(spec.SpecType, "Default spec type should not be null.");
        }
    }
}

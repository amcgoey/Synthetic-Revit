using System;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;

namespace SyntheticTests
{
    [TestFixture]
    public class ParameterDefinitionSpecIntegrationTests
    {
        [Test]
        public void ParameterDefinitionSpec_CreateDefault_ReturnsValidDefaults()
        {
            var spec = ParameterDefinitionSpec.CreateDefault();

            Assert.IsNotNull(spec, "Default spec should not be null.");
            Assert.IsNotNull(spec.Group, "Default group should not be null.");
            Assert.IsNotNull(spec.SpecType, "Default spec type should not be null.");
            Assert.AreEqual("PG_DATA", spec.Group);
            Assert.AreEqual("Text", spec.SpecType);
        }

        [Test]
        public void ParameterDefinitionSpec_Constructor_SetsGroupAndType()
        {
            var spec = new ParameterDefinitionSpec("PG_GEOMETRY", "Length");

            Assert.AreEqual("PG_GEOMETRY", spec.Group);
            Assert.AreEqual("Length", spec.SpecType);
            Assert.AreEqual("PG_GEOMETRY", spec.ParameterGroup);
            Assert.AreEqual("Length", spec.ParameterType);
        }
    }
}

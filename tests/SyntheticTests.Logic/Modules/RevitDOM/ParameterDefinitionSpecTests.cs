using System;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;

namespace SyntheticTests.Logic.Modules.RevitDOM
{
    [TestFixture]
    public class ParameterDefinitionSpecTests
    {
        [Test]
        public void Constructor_Default_InitializesPropertiesToNull()
        {
            var spec = new ParameterDefinitionSpec();
            Assert.IsNull(spec.Group);
            Assert.IsNull(spec.SpecType);
            Assert.IsNull(spec.ParameterGroup);
            Assert.IsNull(spec.ParameterType);
        }

        [Test]
        public void Constructor_WithArguments_SetsPropertiesAndAliases()
        {
            var groupObj = "PG_DATA";
            var typeObj = "Text";
            var spec = new ParameterDefinitionSpec(groupObj, typeObj);
            Assert.AreEqual(groupObj, spec.Group);
            Assert.AreEqual(typeObj, spec.SpecType);
            Assert.AreEqual(groupObj, spec.ParameterGroup);
            Assert.AreEqual(typeObj, spec.ParameterType);
        }

        [Test]
        public void AliasProperties_MutateUnderlyingState()
        {
            var spec = new ParameterDefinitionSpec();
            spec.ParameterGroup = "PG_IDENTITY";
            spec.ParameterType = "Integer";
            Assert.AreEqual("PG_IDENTITY", spec.Group);
            Assert.AreEqual("Integer", spec.SpecType);
        }

        [Test]
        public void CreateDefault_ReturnsNonNullSpecWithDefaults()
        {
            var spec = ParameterDefinitionSpec.CreateDefault();
            Assert.IsNotNull(spec);
            Assert.IsNotNull(spec.Group);
            Assert.IsNotNull(spec.SpecType);
        }

        [Test]
        public void FromParameter_NullParameter_ReturnsDefaultSpec()
        {
            var spec = ParameterDefinitionSpec.FromParameter(null);
            Assert.IsNotNull(spec);
            Assert.IsNotNull(spec.Group);
            Assert.IsNotNull(spec.SpecType);
        }
    }
}

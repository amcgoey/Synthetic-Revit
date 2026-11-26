using System;
using System.Reflection;
using NUnit.Framework;
using Synthetic.Platform.Revit;
using SyntheticTests.Helpers;

namespace SyntheticTests.Platform.Revit
{
    [TestFixture]
    [UnitTest]
    public class RevitVersionMetadataTests
    {
        [TestCase("2023", true, 2023)]
        [TestCase("2027", true, 2027)]
        [TestCase("  2026  ", true, 2026)]
        [TestCase("2000", true, 2000)]
        [TestCase("9999", true, 9999)]
        [TestCase(null, false, 0)]
        [TestCase("", false, 0)]
        [TestCase("   ", false, 0)]
        [TestCase("27", false, 0)]
        [TestCase("20270", false, 0)]
        [TestCase("1999", false, 0)]
        [TestCase("0999", false, 0)]
        [TestCase("abcd", false, 0)]
        [TestCase("-202", false, 0)]
        [TestCase("+202", false, 0)]
        public void TryParseRevitYear_ValidatesFourDigitYearsAndTrimsWhitespace(
            string? rawValue,
            bool expectedValid,
            int expectedYear)
        {
            bool actualValid = RevitVersionMetadata.TryParseRevitYear(rawValue, out int actualYear);

            Assert.AreEqual(expectedValid, actualValid);
            Assert.AreEqual(expectedYear, actualYear);
        }

        [Test]
        public void ReadRevitYear_AndReadRevitYearString_ReturnNormalizedYearForValidMetadata()
        {
            Assembly asm = DynamicAssemblyTestHelper.CreateDynamicAssembly(
                null,
                (RevitVersionMetadata.MetadataKey, "  2027  "));

            Assert.AreEqual(2027, RevitVersionMetadata.ReadRevitYear(asm));
            Assert.AreEqual("2027", RevitVersionMetadata.ReadRevitYearString(asm));
        }

        [Test]
        public void ReadRevitYear_AndReadRevitYearString_ThrowArgumentNullExceptionOnNullAssembly()
        {
            Assert.Throws<ArgumentNullException>(() => RevitVersionMetadata.ReadRevitYear(null!));
            Assert.Throws<ArgumentNullException>(() => RevitVersionMetadata.ReadRevitYearString(null!));
        }

        [Test]
        public void ReadRevitYear_AndReadRevitYearString_ThrowInvalidOperationExceptionOnMissingMetadata()
        {
            Assembly missingAsm = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, ("OtherKey", "2027"));

            InvalidOperationException yearEx = Assert.Throws<InvalidOperationException>(
                () => RevitVersionMetadata.ReadRevitYear(missingAsm))!;
            StringAssert.Contains(RevitVersionMetadata.MetadataKey, yearEx.Message);

            InvalidOperationException stringEx = Assert.Throws<InvalidOperationException>(
                () => RevitVersionMetadata.ReadRevitYearString(missingAsm))!;
            StringAssert.Contains(RevitVersionMetadata.MetadataKey, stringEx.Message);
        }

        [TestCase("not-a-year")]
        [TestCase("26")]
        [TestCase("1999")]
        [TestCase("   ")]
        [TestCase("20270")]
        public void ReadRevitYear_AndReadRevitYearString_ThrowInvalidOperationExceptionOnInvalidMetadata(string rawMetadata)
        {
            Assembly badAsm = DynamicAssemblyTestHelper.CreateDynamicAssembly(
                null,
                (RevitVersionMetadata.MetadataKey, rawMetadata));

            InvalidOperationException yearEx = Assert.Throws<InvalidOperationException>(
                () => RevitVersionMetadata.ReadRevitYear(badAsm))!;
            StringAssert.Contains(RevitVersionMetadata.MetadataKey, yearEx.Message);

            InvalidOperationException stringEx = Assert.Throws<InvalidOperationException>(
                () => RevitVersionMetadata.ReadRevitYearString(badAsm))!;
            StringAssert.Contains(RevitVersionMetadata.MetadataKey, stringEx.Message);
        }
    }
}

using System;
using System.IO;
using NUnit.Framework;
using Synthetic.Features.MultiModelPdfExport.Services;
using SyntheticTests;

namespace SyntheticTests.Features.MultiModelPdfExport
{
    [TestFixture]
    [UnitTest]
    public class MultiModelPdfPathHelperTests
    {
        [Test]
        public void SanitizeDocumentTitleToken_ReplacesInvalidCharsStripsExtensionAndTrailingDotsSpaces()
        {
            Assert.AreEqual("TowerA_Core_Shell_1_", MultiModelPdfPathHelper.SanitizeDocumentTitleToken("TowerA:Core/Shell<1>.rvt"));
            Assert.AreEqual("Model Name", MultiModelPdfPathHelper.SanitizeDocumentTitleToken("  Model Name . .RVT  "));
            Assert.AreEqual("Project_v2", MultiModelPdfPathHelper.SanitizeDocumentTitleToken("Project\tv2. . "));
            Assert.AreEqual("DotName.Phase1", MultiModelPdfPathHelper.SanitizeDocumentTitleToken("DotName.Phase1.rvt"));
        }

        [Test]
        public void SanitizeDocumentTitleToken_ReturnsUntitledWhenDetachedOrEmpty()
        {
            Assert.AreEqual("Untitled", MultiModelPdfPathHelper.SanitizeDocumentTitleToken(null));
            Assert.AreEqual("Untitled", MultiModelPdfPathHelper.SanitizeDocumentTitleToken(string.Empty));
            Assert.AreEqual("Untitled", MultiModelPdfPathHelper.SanitizeDocumentTitleToken("   "));
            Assert.AreEqual("Untitled", MultiModelPdfPathHelper.SanitizeDocumentTitleToken(".rvt"));
            Assert.AreEqual("Untitled", MultiModelPdfPathHelper.SanitizeDocumentTitleToken(" . .rvt "));
            Assert.AreEqual("Untitled", MultiModelPdfPathHelper.SanitizeDocumentTitleToken("..."));
        }

        [TestCase("CON", "_CON")]
        [TestCase("prn.rvt", "_prn")]
        [TestCase("AUX", "_AUX")]
        [TestCase("nul", "_nul")]
        [TestCase("COM1", "_COM1")]
        [TestCase("com9.rvt", "_com9")]
        [TestCase("LPT1", "_LPT1")]
        [TestCase("LPT9.archive.rvt", "_LPT9.archive")]
        [TestCase("CONFIG.rvt", "CONFIG")]
        [TestCase("COM10.rvt", "COM10")]
        public void SanitizeDocumentTitleToken_NeutralizesDosReservedNames(string input, string expected)
        {
            Assert.AreEqual(expected, MultiModelPdfPathHelper.SanitizeDocumentTitleToken(input));
        }

        [TestCase(@"C:\Exports\PDFs", true)]
        [TestCase(@"C:\", true)]
        [TestCase(@"\\fileserver\Projects\Deliverables", true)]
        [TestCase(@"\\fileserver\share", true)]
        [TestCase("", false)]
        [TestCase("   ", false)]
        [TestCase(@"Exports\PDFs", false)]
        [TestCase(@"C:Exports", false)]
        [TestCase(@"\Exports", false)]
        [TestCase(@"\\fileserver", false)]
        [TestCase(@"C:\Exports\Bad|Folder", false)]
        [TestCase(@"C:\Exports\Bad:Sub", false)]
        [TestCase(@"C:\Exports\CON\PDFs", false)]
        [TestCase(@"C:\Exports\TrailingDot.", false)]
        public void TryValidateOutputFolder_EnforcesRootedOrUncAndWindowsRules(string folder, bool expectedValid)
        {
            bool actual = MultiModelPdfPathHelper.TryValidateOutputFolder(folder, out string error);
            Assert.AreEqual(expectedValid, actual, $"Folder '{folder}' validation returned unexpected result. Error='{error}'.");
            if (!expectedValid)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            }
        }

        [Test]
        public void ValidateOutputPathLength_Accepts254CharsAndRejects255Chars()
        {
            string root = @"C:\Exports\";
            int stemLen254 = 254 - root.Length - 4; // + ".pdf" == 254
            string path254 = root + new string('a', stemLen254) + ".pdf";
            Assert.AreEqual(254, path254.Length);
            Assert.IsTrue(MultiModelPdfPathHelper.IsPathWithinSafeLimit(path254));
            Assert.DoesNotThrow(() => MultiModelPdfPathHelper.ValidateOutputPathLength(path254));

            string path255 = root + new string('a', stemLen254 + 1) + ".pdf";
            Assert.AreEqual(255, path255.Length);
            Assert.IsFalse(MultiModelPdfPathHelper.IsPathWithinSafeLimit(path255));
            Assert.Throws<PathTooLongException>(() => MultiModelPdfPathHelper.ValidateOutputPathLength(path255));
        }
    }
}
